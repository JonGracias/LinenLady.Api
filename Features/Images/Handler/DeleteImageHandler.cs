// /Application/Images/DeleteImageHandler.cs
// Deletes an image row from inv.InventoryImage and removes the blob from Azure.
// If the deleted image was primary, promotes the lowest-SortOrder remaining image.

using Azure.Storage.Blobs;
using LinenLady.API.Blob.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace LinenLady.API.Inventory.Images.Handler;

public sealed class DeleteImageHandler
{
    private readonly string _connStr;
    private readonly string _blobConnStr;
    private readonly string _containerName;
    private readonly ILogger<DeleteImageHandler> _logger;

    public DeleteImageHandler(
        IConfiguration configuration,
        IOptions<BlobStorageOptions> blobOptions,
        ILogger<DeleteImageHandler> logger)
    {
        _connStr = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");

        var opts = blobOptions.Value;
        _blobConnStr = opts.ConnectionString;
        _containerName = string.IsNullOrWhiteSpace(opts.ImageContainerName)
            ? "inventory-images" : opts.ImageContainerName;

        _logger = logger;
    }

    public async Task HandleAsync(int inventoryId, int imageId, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid inventory id.");
        if (imageId     <= 0) throw new ArgumentException("Invalid image id.");

        // 1. Load image row — confirms it belongs to this item
        const string loadSql = """
            SELECT ii.ImagePath, ii.IsPrimary
            FROM inv.InventoryImage ii
            JOIN inv.Inventory i ON i.InventoryId = ii.InventoryId
            WHERE ii.ImageId     = @ImageId
              AND ii.InventoryId = @InventoryId
              AND i.IsDeleted    = 0;
            """;

        // 2. Delete the DB row
        const string deleteSql = """
            DELETE FROM inv.InventoryImage
            WHERE ImageId = @ImageId AND InventoryId = @InventoryId;
            """;

        // 3. If it was primary, promote the lowest-SortOrder remaining image
        const string promoteSql = """
            UPDATE inv.InventoryImage
            SET IsPrimary = 1
            WHERE ImageId = (
                SELECT TOP 1 ImageId
                FROM inv.InventoryImage
                WHERE InventoryId = @InventoryId
                ORDER BY SortOrder ASC
            );
            """;

        string imagePath;
        bool   wasPrimary;

        try
        {
            using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // Load
            using (var loadCmd = new SqlCommand(loadSql, conn, tx) { CommandTimeout = 30 })
            {
                loadCmd.Parameters.Add(new SqlParameter("@ImageId",     System.Data.SqlDbType.Int) { Value = imageId });
                loadCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });

                using var reader = await loadCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new KeyNotFoundException($"Image {imageId} not found on item {inventoryId}.");

                imagePath  = reader.GetString(0);
                wasPrimary = reader.GetBoolean(1);
            }

            // Delete row
            using (var deleteCmd = new SqlCommand(deleteSql, conn, tx) { CommandTimeout = 30 })
            {
                deleteCmd.Parameters.Add(new SqlParameter("@ImageId",     System.Data.SqlDbType.Int) { Value = imageId });
                deleteCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });
                await deleteCmd.ExecuteNonQueryAsync(ct);
            }

            // Promote next image if needed
            if (wasPrimary)
            {
                using var promoteCmd = new SqlCommand(promoteSql, conn, tx) { CommandTimeout = 30 };
                promoteCmd.Parameters.Add(new SqlParameter("@InventoryId", System.Data.SqlDbType.Int) { Value = inventoryId });
                await promoteCmd.ExecuteNonQueryAsync(ct);
            }

            tx.Commit();
        }
        catch (KeyNotFoundException) { throw; }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error deleting image {ImageId} on item {InventoryId}.", imageId, inventoryId);
            throw new InvalidOperationException("Database error.");
        }

        // 2. Delete the blob — best-effort, non-fatal
        if (!string.IsNullOrWhiteSpace(_blobConnStr))
        {
            try
            {
                var blobServiceClient   = new BlobServiceClient(_blobConnStr);
                var containerClient     = blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient          = containerClient.GetBlobClient(imagePath);
                await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                // Blob delete failure is non-fatal — the DB row is already gone
                _logger.LogWarning(ex, "Blob delete failed for {Path} (non-fatal).", imagePath);
            }
        }
    }
}