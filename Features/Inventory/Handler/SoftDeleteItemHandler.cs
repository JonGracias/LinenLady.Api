// Application/Items/SoftDeleteItemHandler.cs
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Images.Sql;
using LinenLady.API.Inventory.Sql;

namespace LinenLady.API.Inventory.Items.Handler;

public sealed class SoftDeleteItemHandler
{
    private readonly IInventoryRepository _repo;
    private readonly IInventoryImageRepository _imageRepo;

    public SoftDeleteItemHandler(
        IInventoryRepository repo,
        IInventoryImageRepository imageRepo)
    {
        _repo = repo;
        _imageRepo = imageRepo;
    }

    public async Task<SoftDeleteItemResult> Handle(int inventoryId, CancellationToken ct)
    {
        var exists = await _imageRepo.ItemExists(inventoryId, ct);
        if (!exists)
            return SoftDeleteItemResult.NotFound;

        var deleted = await _repo.SoftDelete(inventoryId, ct);
        return deleted
            ? SoftDeleteItemResult.Deleted
            : SoftDeleteItemResult.NotFound;
    }
}