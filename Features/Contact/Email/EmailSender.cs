namespace LinenLady.API.Features.Contact.Email;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IEmailSender
{
    /// <summary>
    /// Send a transactional email. Returns the provider's message id on success.
    /// Throws <see cref="EmailSendException"/> on any non-success response.
    /// </summary>
    Task<string> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string FromEmail,
    string FromName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyToEmail = null,
    string? ReplyToName  = null);

public sealed class EmailSendException : Exception
{
    public EmailSendException(string message) : base(message) { }
    public EmailSendException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Resend implementation. Uses IHttpClientFactory ("resend" client, BaseAddress configured in Program.cs).
/// </summary>
public sealed class ResendEmailSender(
    IHttpClientFactory httpFactory,
    IOptions<ContactOptions> options,
    ILogger<ResendEmailSender> log) : IEmailSender
{
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly ContactOptions _opts = options.Value;
    private readonly ILogger<ResendEmailSender> _log = log;

    public async Task<string> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ResendApiKey))
            throw new EmailSendException("Resend API key is not configured.");

        var http = _httpFactory.CreateClient("resend");

        var payload = new ResendSendRequest
        {
            From    = $"{message.FromName} <{message.FromEmail}>",
            To      = new[] { $"{message.ToName} <{message.ToEmail}>" },
            Subject = message.Subject,
            Html    = message.HtmlBody,
            Text    = message.TextBody,
            ReplyTo = message.ReplyToEmail is null
                ? null
                : new[] { $"{message.ReplyToName ?? message.ReplyToEmail} <{message.ReplyToEmail}>" }
        };

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsJsonAsync("emails", payload, ct);
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "Resend transport failure");
            throw new EmailSendException("Failed to reach email provider.", ex);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("Resend returned {Status}: {Body}", resp.StatusCode, body);
            throw new EmailSendException($"Email provider returned {(int)resp.StatusCode}.");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<ResendSendResponse>(cancellationToken: ct)
            ?? throw new EmailSendException("Email provider returned empty response.");

        return parsed.Id;
    }

    private sealed class ResendSendRequest
    {
        [JsonPropertyName("from")]     public string   From    { get; set; } = "";
        [JsonPropertyName("to")]       public string[] To      { get; set; } = Array.Empty<string>();
        [JsonPropertyName("subject")]  public string   Subject { get; set; } = "";
        [JsonPropertyName("html")]     public string   Html    { get; set; } = "";
        [JsonPropertyName("text")]     public string   Text    { get; set; } = "";
        [JsonPropertyName("reply_to")] public string[]? ReplyTo { get; set; }
    }

    private sealed class ResendSendResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }
}
