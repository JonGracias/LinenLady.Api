namespace LinenLady.API.Contracts;

public sealed class InventoryImageDto
{
    public int ImageId { get; set; }
    public string ImagePath { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public string? ReadUrl { get; set; }
}
public sealed class ReadUrlsRequest
{
    public List<string> Paths { get; set; } = new();
    public int? TtlMinutes { get; set; }
}

public sealed class ReadUrlsResponse
{
    public Dictionary<string, string> Urls { get; set; } = new();
}