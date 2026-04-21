using LinenLady.API.Contracts;

namespace LinenLady.API.Inventory.Sql;
public interface IInventoryRepository
{
    Task<bool> SoftDelete(int inventoryId, CancellationToken ct);
    Task<InventoryItemDto?> GetById(int inventoryId, CancellationToken ct);
    Task<bool> Update(int inventoryId, UpdateItemRequest fields, CancellationToken ct);
}

