using LinenLady.API.Contracts;

namespace LinenLady.API.Inventory.Images.Sql;

public interface IInventoryImagesQuery
{
    Task<bool> ItemExists(int inventoryId, CancellationToken ct);
    Task<IReadOnlyList<InventoryImageDto>> GetImages(int inventoryId, CancellationToken ct);
}
