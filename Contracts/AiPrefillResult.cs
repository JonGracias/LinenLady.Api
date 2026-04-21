namespace LinenLady.API.Contracts;

public enum PrefillMode
{
    All,
    Title,
    Description,
    Price
}

public sealed class AiPrefillResult
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? UnitPriceCents { get; set; }
}
