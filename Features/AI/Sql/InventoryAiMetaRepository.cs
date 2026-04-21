namespace LinenLady.API.Inventory.AiMeta.Sql;

using Microsoft.Data.SqlClient;

public sealed record AiMetaRow(
    string? AdminNotes,
    string? KeywordsJson,
    DateTime? KeywordsGeneratedAt,
    string? SeoJson,
    DateTime? SeoGeneratedAt,
    DateTime? UpdatedAt);

public sealed record AiMetaItemContext(
    string Name,
    string? Description,
    int UnitPriceCents,
    string? AdminNotes,
    string? KeywordsJson);

public interface IInventoryAiMetaRepository
{
    Task<AiMetaRow?> GetAsync(int inventoryId, CancellationToken ct);
    Task<AiMetaItemContext?> GetItemContextAsync(int inventoryId, CancellationToken ct);
    Task UpsertKeywordsAsync(int inventoryId, string? adminNotes, string keywordsJson, CancellationToken ct);
    Task UpsertSeoAsync(int inventoryId, string seoJson, CancellationToken ct);
    Task UpsertAdminNotesAsync(int inventoryId, string? adminNotes, CancellationToken ct);
}

public sealed class InventoryAiMetaRepository : IInventoryAiMetaRepository
{
    private readonly string _connStr;

    public InventoryAiMetaRepository(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");
    }

    public async Task<AiMetaRow?> GetAsync(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT m.AdminNotes, m.KeywordsJson, m.KeywordsGeneratedAt,
                   m.SeoJson, m.SeoGeneratedAt, m.UpdatedAt
            FROM inv.InventoryAiMeta m
            WHERE m.InventoryId = @Id;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", inventoryId);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new AiMetaRow(
            AdminNotes:          r.IsDBNull(0) ? null : r.GetString(0),
            KeywordsJson:        r.IsDBNull(1) ? null : r.GetString(1),
            KeywordsGeneratedAt: r.IsDBNull(2) ? null : r.GetDateTime(2),
            SeoJson:             r.IsDBNull(3) ? null : r.GetString(3),
            SeoGeneratedAt:      r.IsDBNull(4) ? null : r.GetDateTime(4),
            UpdatedAt:           r.IsDBNull(5) ? null : r.GetDateTime(5));
    }

    public async Task<AiMetaItemContext?> GetItemContextAsync(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT i.Name, i.Description, i.UnitPriceCents,
                   m.AdminNotes, m.KeywordsJson
            FROM inv.Inventory i
            LEFT JOIN inv.InventoryAiMeta m ON m.InventoryId = i.InventoryId
            WHERE i.InventoryId = @Id AND i.IsDeleted = 0;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", inventoryId);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new AiMetaItemContext(
            Name:           r.GetString(0),
            Description:    r.IsDBNull(1) ? null : r.GetString(1),
            UnitPriceCents: r.GetInt32(2),
            AdminNotes:     r.IsDBNull(3) ? null : r.GetString(3),
            KeywordsJson:   r.IsDBNull(4) ? null : r.GetString(4));
    }

    public async Task UpsertKeywordsAsync(
        int inventoryId, string? adminNotes, string keywordsJson, CancellationToken ct)
    {
        const string sql = """
            MERGE inv.InventoryAiMeta AS t
            USING (SELECT @InventoryId AS InventoryId) AS s
            ON t.InventoryId = s.InventoryId
            WHEN MATCHED THEN
                UPDATE SET
                    KeywordsJson        = @KeywordsJson,
                    KeywordsGeneratedAt = SYSUTCDATETIME(),
                    UpdatedAt           = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (InventoryId, AdminNotes, KeywordsJson, KeywordsGeneratedAt)
                VALUES (@InventoryId, @AdminNotes, @KeywordsJson, SYSUTCDATETIME());
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@AdminNotes",  (object?)adminNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@KeywordsJson", keywordsJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertSeoAsync(int inventoryId, string seoJson, CancellationToken ct)
    {
        const string sql = """
            MERGE inv.InventoryAiMeta AS t
            USING (SELECT @InventoryId AS InventoryId) AS s
            ON t.InventoryId = s.InventoryId
            WHEN MATCHED THEN
                UPDATE SET
                    SeoJson        = @SeoJson,
                    SeoGeneratedAt = SYSUTCDATETIME(),
                    UpdatedAt      = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (InventoryId, SeoJson, SeoGeneratedAt)
                VALUES (@InventoryId, @SeoJson, SYSUTCDATETIME());
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@SeoJson",     seoJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAdminNotesAsync(
        int inventoryId, string? adminNotes, CancellationToken ct)
    {
        const string sql = """
            MERGE inv.InventoryAiMeta AS t
            USING (SELECT @InventoryId AS InventoryId) AS s
            ON t.InventoryId = s.InventoryId
            WHEN MATCHED THEN
                UPDATE SET AdminNotes = @AdminNotes, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (InventoryId, AdminNotes)
                VALUES (@InventoryId, @AdminNotes);
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@AdminNotes",  (object?)adminNotes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
