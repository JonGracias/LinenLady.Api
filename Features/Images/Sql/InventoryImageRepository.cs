// Infrastructure/Sql/InventoryImageRepository.cs
using Microsoft.Data.SqlClient;

namespace LinenLady.API.Inventory.Images.Sql;

public sealed class InventoryImageRepository : IInventoryImageRepository
{
    private readonly string _connStr;

    public InventoryImageRepository(IConfiguration configuration)
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

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<int> GetImageCount(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM inv.InventoryImage
            WHERE InventoryId = @InventoryId;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool?> IsPrimaryImage(int inventoryId, int imageId, CancellationToken ct)
    {
        const string sql = """
            SELECT IsPrimary
            FROM inv.InventoryImage
            WHERE ImageId = @ImageId AND InventoryId = @InventoryId;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@ImageId", imageId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToBoolean(result);
    }

    public async Task<bool> DeleteImage(int inventoryId, int imageId, CancellationToken ct)
    {
        const string sql = """
            DELETE FROM inv.InventoryImage
            WHERE ImageId = @ImageId AND InventoryId = @InventoryId;
            SELECT @@ROWCOUNT;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@ImageId", imageId);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<int?> PickNewPrimaryImage(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 ImageId
            FROM inv.InventoryImage
            WHERE InventoryId = @InventoryId
            ORDER BY SortOrder, ImageId;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    public async Task SetPrimaryImage(int inventoryId, int primaryImageId, CancellationToken ct)
    {
        const string sql = """
            UPDATE inv.InventoryImage
            SET IsPrimary = CASE WHEN ImageId = @PrimaryImageId THEN 1 ELSE 0 END
            WHERE InventoryId = @InventoryId;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@PrimaryImageId", primaryImageId);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}