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
        int? ttlMinutes,
        string blobConnStr,
        string containerName,
        CancellationToken ct)
    {
        var exists = await _sql.ItemExists(inventoryId, ct);
        if (!exists)
            return (false, Array.Empty<InventoryImageDto>());

        var images = (await _sql.GetImages(inventoryId, ct)).ToList();

        // TTL rules copied from your existing GetImageReadUrls
        var ttl = ClampTtl(ttlMinutes);

        // Build SAS URL per image (same as GetImageReadUrls)
        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img.ImagePath))
                continue;

            var blobName = img.ImagePath.TrimStart('/');

            img.ReadUrl = BlobSas.BuildReadUrl(
                blobConnStr,
                containerName,
                blobName,
                ttl
            );
        }

        return (true, images);
    }

    private static TimeSpan ClampTtl(int? ttlMinutes)
    {
        var minutes = ttlMinutes ?? 60;
        if (minutes < 5) minutes = 5;
        if (minutes > 240) minutes = 240;
        return TimeSpan.FromMinutes(minutes);
    }
}
