namespace LinenLady.API.AI.Client;

using System.Text;
using System.Text.Json;
using LinenLady.API.AI.Options;
using Microsoft.Extensions.Options;

public sealed class AzureOpenAiEmbeddingsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AzureOpenAiEmbeddingsClient> _logger;
    private readonly AzureOpenAiOptions _opts;

    public AzureOpenAiEmbeddingsClient(
        HttpClient http,
        ILogger<AzureOpenAiEmbeddingsClient> logger,
        IOptions<AzureOpenAiOptions> opts)
    {
        _http = http;
        _logger = logger;
        _opts = opts.Value;
    }

    /// <summary>
    /// Returns the deployment name used for embeddings. Exposed so callers
    /// (e.g. the vector upsert) can record which model produced the vector.
    /// </summary>
    public string DeploymentName => _opts.EmbeddingsDeployment;

    public async Task<float[]> EmbedAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.Endpoint)
            || string.IsNullOrWhiteSpace(_opts.ApiKey)
            || string.IsNullOrWhiteSpace(_opts.EmbeddingsDeployment))
        {
            throw new InvalidOperationException(
                "Missing Azure OpenAI options (Endpoint/ApiKey/EmbeddingsDeployment).");
        }

        var url = $"{_opts.Endpoint.TrimEnd('/')}/openai/deployments/{_opts.EmbeddingsDeployment}"
                + $"/embeddings?api-version={_opts.ApiVersion}";

        var payload = new { input };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("api-key", _opts.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Azure OpenAI embeddings error {Status}: {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException($"Azure OpenAI embeddings error {(int)resp.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var embArray = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var result = new float[embArray.GetArrayLength()];
        for (var i = 0; i < result.Length; i++)
            result[i] = embArray[i].GetSingle();

        return result;
    }
}
