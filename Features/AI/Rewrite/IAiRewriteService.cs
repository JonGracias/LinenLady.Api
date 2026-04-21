namespace LinenLady.API.AI.Rewrite.Service;

using LinenLady.API.Contracts;

public interface IAiRewriteService
{
    Task<AiPrefillResult?> Rewrite(AiRewriteInput input, CancellationToken ct);
}

public sealed class AiRewriteInput
{
    public string CurrentName { get; set; } = "";
    public string CurrentDescription { get; set; } = "";
    public int CurrentPriceCents { get; set; }
    public string Hint { get; set; } = "";
    public List<string> Fields { get; set; } = new();
}
