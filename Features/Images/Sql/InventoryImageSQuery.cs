using Dapper;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

namespace LinenLady.API.Inventory.Images.Sql;

public sealed class InventoryImagesQuery : IInventoryImagesQuery
{
    private readonly string _connStr;

    public InventoryImagesQuery(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");
    }

    public async Task<bool> ItemExists(int inventoryId, CancellationToken ct)
    {
        const string sql = """
        SELECT COUNT(1)
        FROM inv.Inventory
        WHERE InventoryId = @InventoryId AND IsDeleted = 0;
        """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<IReadOnlyList<InventoryImageDto>> GetImages(int inventoryId, CancellationToken ct)
    {
        const string sql = """
        SELECT ImageId, ImagePath, IsPrimary, SortOrder
        FROM inv.InventoryImage
        WHERE InventoryId = @InventoryId
        ORDER BY SortOrder, ImageId;
        """;

        var images = new List<InventoryImageDto>();

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            images.Add(new InventoryImageDto
            {
                ImageId = reader.GetInt32(0),
                ImagePath = reader.GetString(1),
                IsPrimary = reader.GetBoolean(2),
                SortOrder = reader.GetInt32(3),
            });
        }

        return images;
    }

    public async Task<Dictionary<int, string>> GetPrimaryImagePaths(
        IReadOnlyCollection<int> inventoryIds, CancellationToken ct)
    {
        if (inventoryIds.Count == 0) return new Dictionary<int, string>();

        // One row per item: the primary image, falling back to the first by
        // sort order when nothing is flagged primary. Dapper expands @Ids.
        const string sql = """
        SELECT InventoryId, ImagePath
        FROM (
            SELECT InventoryId, ImagePath,
                   ROW_NUMBER() OVER (
                       PARTITION BY InventoryId
                       ORDER BY IsPrimary DESC, SortOrder, ImageId) AS rn
            FROM inv.InventoryImage
            WHERE InventoryId IN @Ids
        ) ranked
        WHERE rn = 1;
        """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<(int InventoryId, string ImagePath)>(
            new CommandDefinition(sql, new { Ids = inventoryIds }, cancellationToken: ct));

        return rows.ToDictionary(r => r.InventoryId, r => r.ImagePath);
    }
}
