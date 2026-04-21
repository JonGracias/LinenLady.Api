// Infrastructure/Sql/InventoryRepository.cs
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

namespace LinenLady.API.Inventory.Sql;

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly string _connStr;

    public InventoryRepository(string connStr)
    {
        _connStr = connStr;
    }

    public async Task<bool> SoftDelete(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            UPDATE inv.Inventory
            SET IsDeleted = 1
            WHERE InventoryId = @InventoryId AND IsDeleted = 0;
            SELECT @@ROWCOUNT;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<InventoryItemDto?> GetById(int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT InventoryId, Sku, Name, Description,
                   QuantityOnHand, UnitPriceCents, PublicId,
                   IsActive, IsDraft, IsDeleted, IsFeatured,
                   CreatedAt, UpdatedAt
            FROM inv.Inventory
            WHERE InventoryId = @InventoryId AND IsDeleted = 0;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new InventoryItemDto
        {
            InventoryId    = r.GetInt32(r.GetOrdinal("InventoryId")),
            Sku            = r.GetString(r.GetOrdinal("Sku")),
            Name           = r.GetString(r.GetOrdinal("Name")),
            Description    = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
            QuantityOnHand = r.GetInt32(r.GetOrdinal("QuantityOnHand")),
            UnitPriceCents = r.GetInt32(r.GetOrdinal("UnitPriceCents")),
            PublicId       = r.GetGuid(r.GetOrdinal("PublicId")),
            IsActive       = r.GetBoolean(r.GetOrdinal("IsActive")),
            IsDraft        = r.GetBoolean(r.GetOrdinal("IsDraft")),
            IsDeleted      = r.GetBoolean(r.GetOrdinal("IsDeleted")),
            IsFeatured     = r.GetBoolean(r.GetOrdinal("IsFeatured")),
            CreatedAt      = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            UpdatedAt      = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<bool> Update(int inventoryId, UpdateItemFields fields, CancellationToken ct)
    {
        const string sql = """
            UPDATE inv.Inventory
            SET Name           = @Name,
                Description    = @Description,
                UnitPriceCents = @UnitPriceCents,
                QuantityOnHand = @QuantityOnHand,
                IsActive       = @IsActive,
                IsDraft        = @IsDraft,
                IsFeatured     = @IsFeatured,
                UpdatedAt      = SYSUTCDATETIME()
            WHERE InventoryId = @InventoryId
              AND IsDeleted = 0;
            SELECT @@ROWCOUNT;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@InventoryId",    inventoryId);
        cmd.Parameters.AddWithValue("@Name",           fields.Name);
        cmd.Parameters.AddWithValue("@Description",    (object?)fields.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UnitPriceCents", fields.UnitPriceCents);
        cmd.Parameters.AddWithValue("@QuantityOnHand", fields.QuantityOnHand);
        cmd.Parameters.AddWithValue("@IsActive",       fields.IsActive);
        cmd.Parameters.AddWithValue("@IsDraft",        fields.IsDraft);
        cmd.Parameters.AddWithValue("@IsFeatured",     fields.IsFeatured);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }
}