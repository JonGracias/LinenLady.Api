// /Application/Search/SimilarItemsHandler.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace LinenLady.API.Search;

public record SimilarItemResult(
    int     InventoryId,
    string  PublicId,
    string  Name,
    string? Description,
    int     UnitPriceCents,
    bool    IsActive,
    bool    IsDraft,
    double  Score
);



public sealed class SimilarItemsHandler
{
    private readonly ILogger<SimilarItemsHandler> _logger;

    public SimilarItemsHandler(ILogger<SimilarItemsHandler> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<SimilarItemResult>> HandleAsync(
        int inventoryId,
        int top,
        bool publishedOnly,
        double minScore,
        CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid id.");
        if (top is < 1 or > 100) top = 10;

        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Missing SQL_CONNECTION_STRING.");

        // 1. Load the source item's vector
        var sourceVector = await LoadVectorAsync(connStr, inventoryId, ct);
        if (sourceVector is null)
        {
            _logger.LogWarning("No vector found for item {Id}.", inventoryId);
            return Array.Empty<SimilarItemResult>();
        }

        // 2. Load candidate items' vectors + metadata in one query
        var sql = $"""
            SELECT
                i.InventoryId,
                CAST(i.PublicId AS nvarchar(36)) AS PublicId,
                i.Name,
                i.Description,
                i.UnitPriceCents,
                i.IsActive,
                i.IsDraft,
                v.VectorJson
            FROM inv.InventoryVector v
            JOIN inv.Inventory i ON i.InventoryId = v.InventoryId
            WHERE v.VectorPurpose = 'item_text'
              AND i.IsDeleted     = 0
              AND i.InventoryId  <> @SourceId
              {(publishedOnly ? "AND i.IsActive = 1 AND i.IsDraft = 0" : "")};
            """;

        var candidates = new List<(SimilarItemResult Meta, float[] Vector)>();

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@SourceId", inventoryId);

            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var vector = ParseVector(r.GetString(7));
                if (vector is null) continue;

                candidates.Add((
                    new SimilarItemResult(
                        InventoryId:    r.GetInt32(0),
                        PublicId:       r.GetString(1),
                        Name:           r.GetString(2),
                        Description:    r.IsDBNull(3) ? null : r.GetString(3),
                        UnitPriceCents: r.GetInt32(4),
                        IsActive:       r.GetBoolean(5),
                        IsDraft:        r.GetBoolean(6),
                        Score:          0
                    ),
                    vector
                ));
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error loading vectors for item {Id}.", inventoryId);
            throw new InvalidOperationException("Database error.");
        }

        if (candidates.Count == 0)
            return Array.Empty<SimilarItemResult>();

        return candidates
            .Select(c => c.Meta with { Score = CosineSimilarity(sourceVector, c.Vector) })
            .Where(x => x.Score >= minScore)   // ← filter before take
            .OrderByDescending(x => x.Score)
            .Take(top)
            .ToList();
            }

    private static async Task<float[]?> LoadVectorAsync(
        string connStr, int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) VectorJson
            FROM inv.InventoryVector
            WHERE InventoryId   = @Id
              AND VectorPurpose = 'item_text';
            """;

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", inventoryId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        return ParseVector((string)result);
    }

    private static float[]? ParseVector(string json)
    {
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0 ? 0 : dot / denom;
    }
}