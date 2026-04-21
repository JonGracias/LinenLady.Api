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

public sealed class AiPrefillRequest
{
    public bool Overwrite { get; set; } = false;

    // Existing behavior: cap images used for vision
    public int MaxImages { get; set; } = 4;

    // NEW: optional explicit selection of which InventoryImage.ImageId values to analyze
    public int[]? ImageIds { get; set; }

    // Optional extra context for the model
    public string? TitleHint { get; set; }
    public string? Notes { get; set; }
}
