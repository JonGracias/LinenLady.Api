namespace LinenLady.Api.Features.Contact.Sql;

using Dapper;
using LinenLady.Api.Features.Contact.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public interface IContactRepository
{
    Task<long> InsertAsync(ContactSubmissionRow row, CancellationToken ct = default);
    Task MarkSentAsync(long submissionId, string providerMessageId, CancellationToken ct = default);
    Task MarkFailedAsync(long submissionId, string error, CancellationToken ct = default);

    Task<int> CountByIpSinceAsync(string ip,    DateTime sinceUtc, CancellationToken ct = default);
    Task<int> CountByEmailSinceAsync(string em, DateTime sinceUtc, CancellationToken ct = default);
}

public sealed record ContactSubmissionRow(
    string  FromName,
    string  FromEmail,
    string? Subject,
    string  Body,
    string? ProductSku,
    string? IpAddress,
    string? UserAgent);

public sealed class ContactRepository(IConfiguration cfg) : IContactRepository
{
    private readonly string _conn = cfg.GetConnectionString("Sql")
        ?? throw new InvalidOperationException("ConnectionStrings:Sql is not configured.");

    private SqlConnection Connect() => new(_conn);

    public async Task<long> InsertAsync(ContactSubmissionRow row, CancellationToken ct = default)
    {
        using var db = Connect();
        return await db.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT INTO cust.ContactSubmission
                (FromName, FromEmail, Subject, Body, ProductSku, IpAddress, UserAgent, Status, CreatedAt)
            OUTPUT inserted.SubmissionId
            VALUES (@FromName, @FromEmail, @Subject, @Body, @ProductSku, @IpAddress, @UserAgent, 'Pending', SYSUTCDATETIME());
            """,
            row, cancellationToken: ct));
    }

    public Task MarkSentAsync(long submissionId, string providerMessageId, CancellationToken ct = default)
    {
        using var db = Connect();
        return db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE cust.ContactSubmission
            SET Status = 'Sent',
                ProviderMessageId = @ProviderMessageId,
                SentAt = SYSUTCDATETIME()
            WHERE SubmissionId = @SubmissionId;
            """,
            new { SubmissionId = submissionId, ProviderMessageId = providerMessageId },
            cancellationToken: ct));
    }

    public Task MarkFailedAsync(long submissionId, string error, CancellationToken ct = default)
    {
        using var db = Connect();
        return db.ExecuteAsync(new CommandDefinition(
            """
            UPDATE cust.ContactSubmission
            SET Status = 'Failed', ErrorMessage = @Error
            WHERE SubmissionId = @SubmissionId;
            """,
            new { SubmissionId = submissionId, Error = Truncate(error, 500) },
            cancellationToken: ct));
    }

    public async Task<int> CountByIpSinceAsync(string ip, DateTime sinceUtc, CancellationToken ct = default)
    {
        using var db = Connect();
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM cust.ContactSubmission WHERE IpAddress = @Ip AND CreatedAt >= @Since;",
            new { Ip = ip, Since = sinceUtc }, cancellationToken: ct));
    }

    public async Task<int> CountByEmailSinceAsync(string email, DateTime sinceUtc, CancellationToken ct = default)
    {
        using var db = Connect();
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM cust.ContactSubmission WHERE FromEmail = @Email AND CreatedAt >= @Since;",
            new { Email = email, Since = sinceUtc }, cancellationToken: ct));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
