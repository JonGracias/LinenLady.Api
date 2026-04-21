namespace LinenLady.API.Contracts;

public sealed class ReadUrlsRequest
{
    public List<string> Paths { get; set; } = new();
    public int? TtlMinutes { get; set; }
}

public sealed class ReadUrlsResponse
{
    public Dictionary<string, string> Urls { get; set; } = new();
}
