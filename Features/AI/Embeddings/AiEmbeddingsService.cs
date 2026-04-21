namespace LinenLady.API.AI.Embeddings.Service;

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LinenLady.API.AI.Client;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

public sealed class AiEmbeddingsService : IAiEmbeddingsService
{
    private const int MaxPurposeLength = 50;

    private readonly IConfiguration _configuration;
    private readonly AzureOpenAiEmbeddingsClient _embeddings;
    private readonly ILogger<AiEmbeddingsService> _logger;

    public AiEmbeddingsService(
        IConfiguration configuration,
        AzureOpenAiEmbeddingsClient embeddings,
        ILogger<AiEmbeddingsService> logger)
    {
        _configuration = configuration;
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<RefreshVectorOutcome> RefreshAsync(
        int inventoryId,
        RefreshVectorRequest request,
        CancellationToken ct)
    {
        if (inventoryId <= 0)
            return new RefreshVectorOutcome(RefreshVectorStatus.InvalidId, null, "Invalid id.");

        var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "item_text" : request.Purpose.Trim();
        if (purpose.Length > MaxPurposeLength)
            return new RefreshVectorOutcome(RefreshVectorStatus.PurposeTooLong, null,
                $"purpose too long (max {MaxPurposeLength}).");

        var sqlConnStr = _configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");

        var model = _embeddings.DeploymentName;

        // 1) Load item text
        var item = await LoadItemText(sqlConnStr, inventoryId, ct);
        if (item is null)
            return new RefreshVectorOutcome(RefreshVectorStatus.NotFound, null, "Item not found.");

        var inputText = BuildEmbeddingText(item.Value.Name, item.Value.Description, item.Value.KeywordsJson);
        if (string.IsNullOrWhiteSpace(inputText))
            return new RefreshVectorOutcome(RefreshVectorStatus.NoTextToEmbed, null,
                "Item has no text to embed (name/description empty).");

        // 2) Hash for idempotency
        var hash = Sha256Bytes(inputText);

        // 3) Check existing vector
        var existing = await LoadExistingVector(sqlConnStr, inventoryId, purpose, model, ct);
        if (!request.Force && existing is not null && ByteArrayEqual(existing.Value.ContentHash, hash))
        {
            return new RefreshVectorOutcome(
                RefreshVectorStatus.Ok,
                new RefreshVectorResult(
                    InventoryId: inventoryId,
                    Purpose: purpose,
                    Model: model,
                    ChangeStatus: "unchanged",
                    Dimensions: existing.Value.Dimensions,
                    VectorId: existing.Value.VectorId),
                null);
        }

        // 4) Create embedding via shared client
        var embedding = await _embeddings.EmbedAsync(inputText, ct);
        var dims = embedding.Length;
        var vectorJson = JsonSerializer.Serialize(embedding);

        // 5) Upsert (UPDATE-then-INSERT in a transaction — avoids MERGE concurrency pitfalls)
        var (vectorId, wasInsert) = await UpsertVector(
            sqlConnStr, inventoryId, purpose, model, dims, hash, vectorJson, ct);

        return new RefreshVectorOutcome(
            RefreshVectorStatus.Ok,
            new RefreshVectorResult(
                InventoryId: inventoryId,
                Purpose: purpose,
                Model: model,
                ChangeStatus: wasInsert ? "created" : "updated",
                Dimensions: dims,
                VectorId: vectorId),
            null);
    }

    // ---------- Text assembly ----------

    private static string BuildEmbeddingText(string name, string? description, string? keywordsJson)
    {
        name = (name ?? "").Trim();
        var desc = (description ?? "").Trim();

        var sb = new StringBuilder();
        sb.Append(name);

        if (!string.IsNullOrWhiteSpace(desc))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(desc);
        }

        // Flatten keywords JSON into a readable string for the embedding
        if (!string.IsNullOrWhiteSpace(keywordsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(keywordsJson);
                var keywords = new List<string>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var val = item.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            keywords.Add(val);
                    }
                }

                if (keywords.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append("Keywords: ");
                    sb.Append(string.Join(", ", keywords));
                }
            }
            catch
            {
                // malformed JSON — skip keywords
            }
        }

        return sb.ToString().Trim();
    }

    private static byte[] Sha256Bytes(string s)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(s));
    }

    private static bool ByteArrayEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // ---------- SQL ----------

    private static async Task<(string Name, string? Description, string? KeywordsJson)?> LoadItemText(
        string connStr, int id, CancellationToken ct)
    {
        const string sql = """
            SELECT i.Name, i.Description, m.KeywordsJson
            FROM inv.Inventory i
            LEFT JOIN inv.InventoryAiMeta m ON m.InventoryId = i.InventoryId
            WHERE i.InventoryId = @Id AND i.IsDeleted = 0;
            """;

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", id);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return (
            r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2)
        );
    }

    private static async Task<(int VectorId, int Dimensions, byte[] ContentHash)?> LoadExistingVector(
        string connStr, int inventoryId, string purpose, string model, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) VectorId, Dimensions, ContentHash
FROM inv.InventoryVector
WHERE InventoryId = @InventoryId AND VectorPurpose = @Purpose AND Model = @Model;
";
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@Purpose", purpose);
        cmd.Parameters.AddWithValue("@Model", model);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return (r.GetInt32(0), r.GetInt32(1), (byte[])r.GetValue(2));
    }

    /// <summary>
    /// UPDATE-then-INSERT inside a serializable transaction. Safer than MERGE,
    /// which has known concurrency issues (see SQL Server team guidance).
    /// Returns (VectorId, WasInsert).
    /// </summary>
    private static async Task<(int VectorId, bool WasInsert)> UpsertVector(
        string connStr, int inventoryId, string purpose, string model,
        int dimensions, byte[] contentHash, string vectorJson, CancellationToken ct)
    {
        const string updateSql = @"
UPDATE inv.InventoryVector
SET Dimensions = @Dimensions,
    ContentHash = @ContentHash,
    VectorJson = @VectorJson,
    UpdatedAt = SYSUTCDATETIME()
OUTPUT inserted.VectorId
WHERE InventoryId = @InventoryId
  AND VectorPurpose = @Purpose
  AND Model = @Model;
";
        const string insertSql = @"
INSERT INTO inv.InventoryVector
    (InventoryId, VectorPurpose, Model, Dimensions, ContentHash, VectorJson)
OUTPUT inserted.VectorId
VALUES
    (@InventoryId, @Purpose, @Model, @Dimensions, @ContentHash, @VectorJson);
";
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            // Try UPDATE first
            using (var updateCmd = new SqlCommand(updateSql, conn, tx) { CommandTimeout = 30 })
            {
                updateCmd.Parameters.AddWithValue("@InventoryId", inventoryId);
                updateCmd.Parameters.AddWithValue("@Purpose", purpose);
                updateCmd.Parameters.AddWithValue("@Model", model);
                updateCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                updateCmd.Parameters.AddWithValue("@ContentHash", contentHash);
                updateCmd.Parameters.AddWithValue("@VectorJson", vectorJson);

                var updated = await updateCmd.ExecuteScalarAsync(ct);
                if (updated is not null and not DBNull)
                {
                    await tx.CommitAsync(ct);
                    return (Convert.ToInt32(updated), WasInsert: false);
                }
            }

            // No row existed — INSERT
            using (var insertCmd = new SqlCommand(insertSql, conn, tx) { CommandTimeout = 30 })
            {
                insertCmd.Parameters.AddWithValue("@InventoryId", inventoryId);
                insertCmd.Parameters.AddWithValue("@Purpose", purpose);
                insertCmd.Parameters.AddWithValue("@Model", model);
                insertCmd.Parameters.AddWithValue("@Dimensions", dimensions);
                insertCmd.Parameters.AddWithValue("@ContentHash", contentHash);
                insertCmd.Parameters.AddWithValue("@VectorJson", vectorJson);

                var inserted = await insertCmd.ExecuteScalarAsync(ct);
                await tx.CommitAsync(ct);
                return (Convert.ToInt32(inserted), WasInsert: true);
            }
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
