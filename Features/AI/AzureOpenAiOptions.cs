namespace LinenLady.API.AI.Options;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";

    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-02-15-preview";
}
