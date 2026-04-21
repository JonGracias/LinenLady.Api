// Infrastructure/Sql/IInventoryImageRepository.cs
namespace LinenLady.API.Inventory.Images.Sql;

public interface IInventoryImageRepository
{
    Task<bool> ItemExists(int inventoryId, CancellationToken ct);
    Task<int> GetImageCount(int inventoryId, CancellationToken ct);
    Task<bool?> IsPrimaryImage(int inventoryId, int imageId, CancellationToken ct);
    Task<bool> DeleteImage(int inventoryId, int imageId, CancellationToken ct);
    Task<int?> PickNewPrimaryImage(int inventoryId, CancellationToken ct);
    Task SetPrimaryImage(int inventoryId, int primaryImageId, CancellationToken ct);
}