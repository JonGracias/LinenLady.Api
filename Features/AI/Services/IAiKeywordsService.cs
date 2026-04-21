namespace LinenLady.API.AI.Keywords.Service;

public interface IAiKeywordsService
{
    Task<GenerateKeywordsResult> GenerateAsync(int inventoryId, string? hint, CancellationToken ct);
}

public sealed record GenerateKeywordsResult(
    int InventoryId,
    string KeywordsJson,
    bool VectorRefreshed,
    bool SeoRefreshed);
