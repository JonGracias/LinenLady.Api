namespace LinenLady.API.Contracts;

public sealed class RefreshVectorRequest
{
    public string? Purpose { get; set; } = "item_text";
    public bool Force { get; set; } = false;
}
