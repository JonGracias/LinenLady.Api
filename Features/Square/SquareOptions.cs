namespace LinenLady.API.Square;

public sealed class SquareOptions
{
    public const string SectionName = "Square";

    public string AccessToken { get; set; } = "";
    public string LocationId  { get; set; } = "";
}