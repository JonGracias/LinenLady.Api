namespace LinenLady.API.Inventory.Sql;

using System.Data;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly string _connStr;

    public InventoryRepository(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");
    }

    // ---------- Column maps (kept aligned with SELECTs below) ----------

    private const string DetailColumns = """
        i.InventoryId,
        i.PublicId,
        i.Sku,
        i.Name,
        i.Description,
        i.QuantityOnHand,
        i.UnitPriceCents,
        i.IsActive,
        i.IsDraft,
        i.IsDeleted,
        i.IsFeatured,
        i.CreatedAt,
        i.UpdatedAt,
        m.KeywordsJson,
        img.ImageId,
        img.ImagePath,
        img.IsPrimary,
        img.SortOrder
        """;

    private const string DetailFrom = """
        FROM inv.Inventory i
        LEFT JOIN inv.InventoryAiMeta m   ON m.InventoryId  = i.InventoryId
        LEFT JOIN inv.InventoryImage  img ON img.InventoryId = i.InventoryId
        """;

    // Ordinals for the detail SELECT above — single source of truth for both GetByKey and GetItems mapping
    private const int O_InventoryId    = 0;
    private const int O_PublicId       = 1;
    private const int O_Sku            = 2;
    private const int O_Name           = 3;
    private const int O_Description    = 4;
    private const int O_QuantityOnHand = 5;
    private const int O_UnitPriceCents = 6;
    private const int O_IsActive       = 7;
    private const int O_IsDraft        = 8;
    private const int O_IsDeleted      = 9;
    private const int O_IsFeatured     = 10;
    private const int O_CreatedAt      = 11;
    private const int O_UpdatedAt      = 12;
    private const int O_KeywordsJson   = 13;
    private const int O_ImageId        = 14;
    private const int O_ImagePath      = 15;
    private const int O_IsPrimary      = 16;
    private const int O_SortOrder      = 17;

    // ---------- Reads ----------

    public async Task<InventoryItemDto?> GetByKey(ItemKey key, CancellationToken ct)
    {
        if (!key.IsId && !key.IsSku)
            throw new ArgumentException("ItemKey must specify Id or Sku.", nameof(key));

        var predicate = key.IsId
            ? "i.InventoryId = @InventoryId"
            : "i.Sku = @Sku";

        var sql = $"""
            SELECT {DetailColumns}
            {DetailFrom}
            WHERE {predicate} AND i.IsDeleted = 0
            ORDER BY img.SortOrder;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        if (key.IsId)
            cmd.Parameters.AddWithValue("@InventoryId", key.Id!.Value);
        else
            cmd.Parameters.Add("@Sku", SqlDbType.NVarChar, 64).Value = key.Sku!.Trim();

        using var reader = await cmd.ExecuteReaderAsync(ct);

        InventoryItemDto? item = null;

        while (await reader.ReadAsync(ct))
        {
            item ??= MapItem(reader, includeKeywords: true);
            TryAddImage(reader, item);
        }

        return item;
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

        var publicIdOrdinal = r.GetOrdinal("PublicId");

        return new InventoryItemDto
        {
            InventoryId    = r.GetInt32(r.GetOrdinal("InventoryId")),
            Sku            = r.GetString(r.GetOrdinal("Sku")),
            Name           = r.GetString(r.GetOrdinal("Name")),
            Description    = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
            QuantityOnHand = r.GetInt32(r.GetOrdinal("QuantityOnHand")),
            UnitPriceCents = r.GetInt32(r.GetOrdinal("UnitPriceCents")),
            PublicId       = r.IsDBNull(publicIdOrdinal) ? null : r.GetGuid(publicIdOrdinal),
            IsActive       = r.GetBoolean(r.GetOrdinal("IsActive")),
            IsDraft        = r.GetBoolean(r.GetOrdinal("IsDraft")),
            IsDeleted      = r.GetBoolean(r.GetOrdinal("IsDeleted")),
            IsFeatured     = r.GetBoolean(r.GetOrdinal("IsFeatured")),
            CreatedAt      = r.GetDateTime(r.GetOrdinal("CreatedAt")),
            UpdatedAt      = r.GetDateTime(r.GetOrdinal("UpdatedAt")),
        };
    }

    public async Task<(List<InventoryItemDto> Items, long TotalCount)> GetItems(
        GetItemsQuery query,
        CancellationToken ct)
    {
        var mode = StatusToMode(query.Status);
        var category = string.IsNullOrWhiteSpace(query.Category) ? null : query.Category.Trim().ToLowerInvariant();
        var hasCategory = category is not null;

        const string categoryJoin = """
            INNER JOIN inv.InventoryAiMeta m_cat
                ON m_cat.InventoryId = i.InventoryId
               AND LOWER(m_cat.KeywordsJson) LIKE @CategoryPattern
            """;

        const string statusClause = """
            i.IsDeleted = 0
            AND (
                @Mode = 0
                OR (@Mode = 1 AND i.IsDraft  = 1)
                OR (@Mode = 2 AND i.IsDraft  = 0 AND i.IsActive  = 1)
                OR (@Mode = 3 AND i.IsFeatured = 1)
            )
            """;

        var countSql = $"""
            SELECT COUNT_BIG(1)
            FROM inv.Inventory i
            {(hasCategory ? categoryJoin : "")}
            WHERE {statusClause};
            """;

        var pageSql = $"""
            WITH page AS (
                SELECT i.InventoryId
                FROM inv.Inventory i
                {(hasCategory ? categoryJoin : "")}
                WHERE {statusClause}
                ORDER BY i.InventoryId DESC
                OFFSET @Offset ROWS
                FETCH NEXT @Limit ROWS ONLY
            )
            SELECT {DetailColumns}
            FROM page p
            JOIN inv.Inventory i          ON i.InventoryId  = p.InventoryId
            LEFT JOIN inv.InventoryAiMeta m   ON m.InventoryId  = i.InventoryId
            LEFT JOIN inv.InventoryImage  img ON img.InventoryId = i.InventoryId
            ORDER BY i.InventoryId DESC, img.SortOrder;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        // 1) Count
        long totalCount;
        using (var countCmd = new SqlCommand(countSql, conn) { CommandTimeout = 30 })
        {
            countCmd.Parameters.AddWithValue("@Mode", mode);
            if (hasCategory)
                countCmd.Parameters.AddWithValue("@CategoryPattern", $"%{category}%");

            var scalar = await countCmd.ExecuteScalarAsync(ct);
            totalCount = scalar is null or DBNull ? 0L : Convert.ToInt64(scalar);
        }

        // Clamp page + recompute offset
        var limit = Math.Clamp(query.Limit, 1, 200);
        var totalPages = (int)Math.Max(1, (totalCount + limit - 1) / limit);
        var page = Math.Clamp(query.Page, 1, totalPages);
        var offset = (page - 1) * limit;

        // Mutate the query so callers see what actually ran
        query.Page = page;
        query.Limit = limit;

        // 2) Page
        using var cmd = new SqlCommand(pageSql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Mode",   mode);
        cmd.Parameters.AddWithValue("@Limit",  limit);
        cmd.Parameters.AddWithValue("@Offset", offset);
        if (hasCategory)
            cmd.Parameters.AddWithValue("@CategoryPattern", $"%{category}%");

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var itemsById = new Dictionary<int, InventoryItemDto>();

        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(O_InventoryId);
            if (!itemsById.TryGetValue(id, out var item))
            {
                item = MapItem(reader, includeKeywords: true);
                itemsById.Add(id, item);
            }
            TryAddImage(reader, item);
        }

        var items = itemsById.Values.OrderByDescending(i => i.InventoryId).ToList();
        return (items, totalCount);
    }

    public async Task<GetCountsResponse> GetCounts(CancellationToken ct)
    {
        const string sql = """
            SELECT
                allCount       = (SELECT COUNT_BIG(1) FROM inv.Inventory WHERE IsDeleted = 0),
                draftsCount    = (SELECT COUNT_BIG(1) FROM inv.Inventory WHERE IsDeleted = 0 AND IsDraft = 1),
                publishedCount = (SELECT COUNT_BIG(1) FROM inv.Inventory WHERE IsDeleted = 0 AND IsDraft = 0 AND IsActive = 1);
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        using var r = await cmd.ExecuteReaderAsync(ct);

        var response = new GetCountsResponse();
        if (await r.ReadAsync(ct))
        {
            response.All       = r.IsDBNull(0) ? 0L : Convert.ToInt64(r.GetValue(0));
            response.Drafts    = r.IsDBNull(1) ? 0L : Convert.ToInt64(r.GetValue(1));
            response.Published = r.IsDBNull(2) ? 0L : Convert.ToInt64(r.GetValue(2));
        }
        return response;
    }

    // ---------- Writes ----------

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
        cmd.Parameters.Add(new SqlParameter("@InventoryId", SqlDbType.Int) { Value = inventoryId });

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
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

    // ---------- Mapping helpers ----------

    private static InventoryItemDto MapItem(SqlDataReader r, bool includeKeywords)
    {
        return new InventoryItemDto
        {
            InventoryId    = r.GetInt32(O_InventoryId),
            PublicId       = r.IsDBNull(O_PublicId) ? null : r.GetGuid(O_PublicId),
            Sku            = r.GetString(O_Sku),
            Name           = r.GetString(O_Name),
            Description    = r.IsDBNull(O_Description) ? null : r.GetString(O_Description),
            QuantityOnHand = r.GetInt32(O_QuantityOnHand),
            UnitPriceCents = r.GetInt32(O_UnitPriceCents),
            IsActive       = r.GetBoolean(O_IsActive),
            IsDraft        = r.GetBoolean(O_IsDraft),
            IsDeleted      = r.GetBoolean(O_IsDeleted),
            IsFeatured     = r.GetBoolean(O_IsFeatured),
            CreatedAt      = r.GetDateTime(O_CreatedAt),
            UpdatedAt      = r.GetDateTime(O_UpdatedAt),
            KeywordsJson   = includeKeywords && !r.IsDBNull(O_KeywordsJson) ? r.GetString(O_KeywordsJson) : null,
        };
    }

    private static void TryAddImage(SqlDataReader r, InventoryItemDto item)
    {
        if (r.IsDBNull(O_ImageId)) return;

        var imageId = r.GetInt32(O_ImageId);
        // Guard against duplicate rows when images appear across grouping boundaries
        if (item.Images.Any(i => i.ImageId == imageId)) return;

        item.Images.Add(new InventoryImageDto
        {
            ImageId   = imageId,
            ImagePath = r.GetString(O_ImagePath),
            IsPrimary = r.GetBoolean(O_IsPrimary),
            SortOrder = r.GetInt32(O_SortOrder),
        });
    }

    private static int StatusToMode(string? status) => (status ?? "all").Trim().ToLowerInvariant() switch
    {
        "all"      => 0,
        "draft"    => 1,
        "active"   => 2,
        "featured" => 3,
        _          => -1
    };
}
