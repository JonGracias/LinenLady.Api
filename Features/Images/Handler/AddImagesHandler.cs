// /Application/Images/AddImagesHandler.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using LinenLady.API.Contracts;


namespace LinenLady.API.Inventory.Images.Handler;

public sealed class AddImagesHandler
{
    private readonly string _connStr;
    private readonly ILogger<AddImagesHandler> _logger;
    public AddImagesHandler(
        IConfiguration configuration,
        ILogger<AddImagesHandler> logger)
    {
        _connStr = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'");
        _logger = logger;
    }

    public async Task<AddImagesResult> HandleAsync(int inventoryId, AddImagesRequest body, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid id.");

        if (body.Images is null || body.Images.Count == 0)
            throw new ArgumentException("Body must include images[] with at least one entry.");

        if (body.Images.Count > 20)
            throw new ArgumentException("Too many images (max 20 per request).");

        // Basic validation first (blob-name only, no URL/SAS) :contentReference[oaicite:3]{index=3}
        foreach (var img in body.Images)
        {
            if (string.IsNullOrWhiteSpace(img.ImagePath))
                throw new ArgumentException("Each image requires imagePath.");

            var p = img.ImagePath.Trim();

            if (p.Length > 1024)
                throw new ArgumentException("imagePath too long (max 1024).");

            if (p.Contains("://", StringComparison.OrdinalIgnoreCase) ||
                p.Contains('?', StringComparison.Ordinal) ||
                p.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("imagePath must be a blob name (no URL, no SAS token).");

            if (!p.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("imagePath must start with 'images/'.");

            if (img.SortOrder is not null && img.SortOrder <= 0)
                throw new ArgumentException("sortOrder must be > 0 when provided.");
        }

        const string loadPublicIdSql = @"
SELECT PublicId
FROM inv.Inventory
WHERE InventoryId = @InventoryId AND IsDeleted = 0;";

        const string maxSortSql = @"
SELECT ISNULL(MAX(SortOrder), 0)
FROM inv.InventoryImage
WHERE InventoryId = @InventoryId;";

        const string clearPrimarySql = @"
UPDATE inv.InventoryImage
SET IsPrimary = 0
WHERE InventoryId = @InventoryId;";

        const string insertSql = @"
INSERT INTO inv.InventoryImage (InventoryId, ImagePath, IsPrimary, SortOrder)
OUTPUT INSERTED.ImageId
VALUES (@InventoryId, @ImagePath, @IsPrimary, @SortOrder);";

        try
        {
            using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(ct);

            using var tx = conn.BeginTransaction();

            // Load PublicId (validates existence + not deleted) :contentReference[oaicite:4]{index=4}
            Guid publicId;
            using (var loadCmd = new SqlCommand(loadPublicIdSql, conn, tx) { CommandTimeout = 30 })
            {
                loadCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

                var obj = await loadCmd.ExecuteScalarAsync(ct);
                if (obj is null || obj is DBNull)
                    throw new KeyNotFoundException("Item not found.");

                publicId = (Guid)obj;
            }

            // Enforce blobNames belong to this item :contentReference[oaicite:5]{index=5}
            var expectedPrefix = $"images/{publicId:N}/";
            foreach (var img in body.Images)
            {
                var p = img.ImagePath.Trim();
                if (!p.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"imagePath must start with '{expectedPrefix}'.");
            }

            int currentMaxSort;
            using (var maxCmd = new SqlCommand(maxSortSql, conn, tx) { CommandTimeout = 30 })
            {
                maxCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });
                currentMaxSort = Convert.ToInt32(await maxCmd.ExecuteScalarAsync(ct));
            }

            var created = new List<InventoryImageDto>(body.Images.Count);

            foreach (var img in body.Images)
            {
                var isPrimary = img.IsPrimary ?? false;

                // If this image is primary, clear existing primary first (last primary wins) :contentReference[oaicite:6]{index=6}
                if (isPrimary)
                {
                    using var clearCmd = new SqlCommand(clearPrimarySql, conn, tx) { CommandTimeout = 30 };
                    clearCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });
                    await clearCmd.ExecuteNonQueryAsync(ct);
                }

                var sortOrder = img.SortOrder ?? (++currentMaxSort);
                var imagePath = img.ImagePath.Trim();

                using var insertCmd = new SqlCommand(insertSql, conn, tx) { CommandTimeout = 30 };
                insertCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });
                insertCmd.Parameters.Add(new SqlParameter("@ImagePath", System.Data.SqlDbType.NVarChar, 1024) { Value = imagePath });
                insertCmd.Parameters.Add(new SqlParameter("@IsPrimary", System.Data.SqlDbType.Bit) { Value = isPrimary });
                insertCmd.Parameters.Add(new SqlParameter("@SortOrder", System.Data.SqlDbType.Int) { Value = sortOrder });

                var imageId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync(ct));

                created.Add(new InventoryImageDto
                {
                    ImageId = imageId,
                    ImagePath = imagePath,
                    IsPrimary = isPrimary,
                    SortOrder = sortOrder
                });
            }

            tx.Commit();

            return new AddImagesResult(inventoryId, created);
        }
        catch (KeyNotFoundException)
        {
            // preserve semantics: 404 at route layer
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in AddImagesHandler.");
            throw new InvalidOperationException("Database error.");
        }
        catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException && ex is not KeyNotFoundException)
        {
            _logger.LogError(ex, "Unhandled error in AddImagesHandler.");
            throw new InvalidOperationException("Server error.");
        }
    }
}
