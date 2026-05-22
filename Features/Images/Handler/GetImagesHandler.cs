// Application/Images/GetImagesHandler.cs
using LinenLady.API.Contracts;
using LinenLady.API.Blob;
using LinenLady.API.Inventory.Images.Sql;

namespace LinenLady.API.Inventory.Images.Handler;

public sealed class GetImagesHandler
{
    private readonly IInventoryImagesQuery _sql;

    public GetImagesHandler(IInventoryImagesQuery sql)
    {
        _sql = sql;
    }

    public async Task<(bool itemExists, IReadOnlyList<InventoryImageDto> images)> Handle(
        int inventoryId,
        int? ttlMinutes, // accepted for backward compat; ignored
        string blobConnStr,
        string containerName,
        CancellationToken ct)
    {
        var exists = await _sql.ItemExists(inventoryId, ct);
        if (!exists)
            return (false, Array.Empty<InventoryImageDto>());

        var images = (await _sql.GetImages(inventoryId, ct)).ToList();

        // Public blob URLs — no SAS, no expiry. Cache-friendly across
        // browser sessions and CDN. ttlMinutes is accepted for backward
        // compatibility with existing clients but is now ignored.
        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img.ImagePath))
                continue;

            var blobName = img.ImagePath.TrimStart('/');

            img.ReadUrl = BlobSas.BuildPublicUrl(
                blobConnStr,
                containerName,
                blobName
            );
        }

        return (true, images);
    }
}