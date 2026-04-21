// /Application/Images/ReplaceImageHandler.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using LinenLady.API.Blob;

namespace LinenLady.API.Inventory.Images.Handler;

public record ReplaceImageUploadInfo(
    string UploadUrl,
    Dictionary<string, string> RequiredHeaders,
    string ContentType,
    string BlobName
);

public sealed class ReplaceImageHandler
{
    private readonly ILogger<ReplaceImageHandler> _logger;

    public ReplaceImageHandler(ILogger<ReplaceImageHandler> logger)
    {
        _logger = logger;
    }

    public async Task<ReplaceImageUploadInfo> HandleAsync(
        int inventoryId,
        int imageId,
        CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid inventory id.");
        if (imageId <= 0)     throw new ArgumentException("Invalid image id.");

        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException("Missing SQL_CONNECTION_STRING.");

        var blobConnStr = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(blobConnStr))
            throw new InvalidOperationException("Missing BLOB_CONNECTION_STRING.");

        var containerName = Environment.GetEnvironmentVariable("IMAGE_CONTAINER_NAME") ?? "inventory";

        const string sql = """
            SELECT ii.ImagePath
            FROM inv.InventoryImage ii
            JOIN inv.Inventory i ON i.InventoryId = ii.InventoryId
            WHERE ii.ImageId     = @ImageId
              AND ii.InventoryId = @InventoryId
              AND i.IsDeleted    = 0;
            """;

        string imagePath;
        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add(new SqlParameter("@ImageId",     System.Data.SqlDbType.Int) { Value = imageId });
            cmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is null or DBNull)
                throw new KeyNotFoundException($"Image {imageId} not found on item {inventoryId}.");

            imagePath = (string)result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error looking up image path.");
            throw new InvalidOperationException("Database error.");
        }

        var ext = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        var contentType = ext switch
        {
            "png"  => "image/png",
            "webp" => "image/webp",
            "gif"  => "image/gif",
            _      => "image/jpeg",
        };

        var (uploadUrl, requiredHeaders) = BlobSas.BuildUploadUrl(
            blobConnStr,
            containerName,
            imagePath,
            contentType,
            TimeSpan.FromMinutes(15)
        );

        return new ReplaceImageUploadInfo(uploadUrl, requiredHeaders, contentType, BlobName: imagePath);
    }
}