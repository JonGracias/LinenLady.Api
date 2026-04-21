// /Application/Images/SetPrimaryImageHandler.cs
using Microsoft.Extensions.Logging;
using LinenLady.API.Inventory.Images.Sql;

namespace LinenLady.API.Inventory.Images.Handler;

public sealed class SetPrimaryImageHandler
{
    private readonly IInventoryImageRepository _repo;
    private readonly ILogger<SetPrimaryImageHandler> _logger;

    public SetPrimaryImageHandler(
        IInventoryImageRepository repo,
        ILogger<SetPrimaryImageHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task HandleAsync(int inventoryId, int imageId, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid inventory id.");
        if (imageId <= 0)     throw new ArgumentException("Invalid image id.");

        // Confirm the inventory item exists and is not deleted
        var itemExists = await _repo.ItemExists(inventoryId, ct);
        if (!itemExists)
            throw new KeyNotFoundException($"Item {inventoryId} not found.");

        // Confirm the image belongs to this inventory item
        // IsPrimaryImage returns null when the image doesn't exist on this item
        var currentPrimary = await _repo.IsPrimaryImage(inventoryId, imageId, ct);
        if (currentPrimary is null)
            throw new KeyNotFoundException($"Image {imageId} not found on item {inventoryId}.");

        // Nothing to do if already primary — still return success
        if (currentPrimary == true)
            return;

        await _repo.SetPrimaryImage(inventoryId, imageId, ct);

        _logger.LogInformation(
            "Primary image for inventory {InventoryId} set to image {ImageId}.",
            inventoryId, imageId);
    }
}