namespace LinenLady.API.AI.Seo.Service;

public interface IAiSeoService
{
    Task<GenerateSeoResult> GenerateAsync(int inventoryId, CancellationToken ct);
}

public sealed record GenerateSeoResult(
    int InventoryId,
    string SeoJson);
