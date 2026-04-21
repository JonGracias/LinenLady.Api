namespace LinenLady.API.Inventory.Sql;

using LinenLady.API.Contracts;

public interface IInventoryRepository
{
    Task<bool> SoftDelete(int inventoryId, CancellationToken ct);

    /// <summary>
    /// Loads a single item (with images + KeywordsJson) by either id or SKU.
    /// Used for detail views.
    /// </summary>
    Task<InventoryItemDto?> GetByKey(ItemKey key, CancellationToken ct);

    /// <summary>
    /// Lightweight lookup — no images, no keywords. Used by the update handler
    /// for the existence check and current-field resolution.
    /// </summary>
    Task<InventoryItemDto?> GetById(int inventoryId, CancellationToken ct);

    Task<(List<InventoryItemDto> Items, long TotalCount)> GetItems(
        GetItemsQuery query,
        CancellationToken ct);

    Task<GetCountsResponse> GetCounts(CancellationToken ct);

    Task<bool> Update(int inventoryId, UpdateItemFields fields, CancellationToken ct);
}
