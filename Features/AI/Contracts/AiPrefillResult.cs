namespace LinenLady.API.Contracts;

/// <summary>
/// Shared AI-output DTO. Returned by both prefill (vision) and rewrite (text)
/// services as the model's proposed values for an inventory item.
/// </summary>
public sealed class AiPrefillResult
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? UnitPriceCents { get; set; }
}
