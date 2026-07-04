using LinenLady.API.Contracts;

namespace LinenLady.API.Inventory.Images.Sql;

public interface IInventoryImagesQuery
{
    Task<bool> ItemExists(int inventoryId, CancellationToken ct);
    Task<IReadOnlyList<InventoryImageDto>> GetImages(int inventoryId, CancellationToken ct);

    /// <summary>
    /// Primary (or first, when none is flagged primary) image path per
    /// inventory id. Items with no images are absent from the result.
    /// </summary>
    Task<Dictionary<int, string>> GetPrimaryImagePaths(
        IReadOnlyCollection<int> inventoryIds, CancellationToken ct);
}
