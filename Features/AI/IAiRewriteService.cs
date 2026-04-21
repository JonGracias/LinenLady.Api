// Application/Items/IAiRewriteService.cs
namespace LinenLady.API.AI.Rewrite.Service;

public interface IAiRewriteService
{
    Task<AiRewriteOutput?> Rewrite(AiRewriteInput input, CancellationToken ct);
}

public sealed class AiRewriteInput
{
    public string CurrentName { get; set; } = "";
    public string CurrentDescription { get; set; } = "";
    public int CurrentPriceCents { get; set; }
    public string Hint { get; set; } = "";
    public List<string> Fields { get; set; } = new();
}

public sealed class AiRewriteOutput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? PriceCents { get; set; }
}