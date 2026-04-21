namespace LinenLady.API.Contracts;

public sealed class AiPrefillRequest
{
    public bool Overwrite { get; set; } = false;

    // Existing behavior: cap images used for vision
    public int MaxImages { get; set; } = 4;

    // Optional explicit selection of which InventoryImage.ImageId values to analyze
    public int[]? ImageIds { get; set; }

    // Optional extra context for the model
    public string? TitleHint { get; set; }
    public string? Notes { get; set; }
}
