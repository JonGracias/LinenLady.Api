// /Application/Images/NewBlobUrlHandler.cs
// Returns a SAS upload URL for a brand-new image blob on an existing item.
// Does NOT write to the DB — the caller uploads the blob, then calls AddImages
// to register the path.

using Microsoft.Data.SqlClient;
using LinenLady.API.Blob;

namespace LinenLady.API.Inventory.Images.Handler;

public record NewBlobUrlInfo(
    string UploadUrl,
    Dictionary<string, string> RequiredHeaders,
    string ContentType,
    string BlobName
);

public sealed class NewBlobUrlHandler
{
    private readonly ILogger<NewBlobUrlHandler> _logger;

    public NewBlobUrlHandler(ILogger<NewBlobUrlHandler> logger)
    {
        _logger = logger;
    }

    public async Task<NewBlobUrlInfo> HandleAsync(
        int    inventoryId,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        if (inventoryId <= 0)
            throw new ArgumentException("Invalid inventory id.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required.");

        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException("Missing SQL_CONNECTION_STRING.");

        var blobConnStr = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(blobConnStr))
            throw new InvalidOperationException("Missing BLOB_STORAGE_CONNECTION_STRING.");

        var containerName = Environment.GetEnvironmentVariable("IMAGE_CONTAINER_NAME") ?? "inventory";

        // 1. Load PublicId — validates item exists and is not deleted
        const string sql = """
            SELECT PublicId
            FROM inv.Inventory
            WHERE InventoryId = @InventoryId AND IsDeleted = 0;
            """;

        Guid publicId;
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is null or DBNull)
                throw new KeyNotFoundException($"Item {inventoryId} not found.");

            publicId = (Guid)result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in NewBlobUrlHandler for item {Id}.", inventoryId);
            throw new InvalidOperationException("Database error.");
        }

        // 2. Build a unique blob name under this item's path
        //    images/{publicId:N}/{uniqueId}-{sanitisedFileName}
        var ext          = Path.GetExtension(fileName.Trim()).TrimStart('.').ToLowerInvariant();
        var safeExt      = ext is "jpg" or "jpeg" or "png" or "webp" or "gif" ? ext : "jpg";
        var uniquePrefix = Guid.NewGuid().ToString("N")[..8];
        var blobName     = $"images/{publicId:N}/{uniquePrefix}.{safeExt}";

        // 3. Resolve content-type from extension (don't trust the client blindly)
        var resolvedContentType = safeExt switch
        {
            "png"  => "image/png",
            "webp" => "image/webp",
            "gif"  => "image/gif",
            _      => "image/jpeg",
        };

        // 4. Generate SAS upload URL
        var (uploadUrl, requiredHeaders) = BlobSas.BuildUploadUrl(
            blobConnStr,
            containerName,
            blobName,
            resolvedContentType,
            TimeSpan.FromMinutes(15)
        );

        return new NewBlobUrlInfo(uploadUrl, requiredHeaders, resolvedContentType, blobName);
    }
}