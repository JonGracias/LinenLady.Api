namespace LinenLady.API.Contracts;

public sealed class InventoryImageDto
{
    public int ImageId { get; set; }
    public string ImagePath { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public string? ReadUrl { get; set; }
}
