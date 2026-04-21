namespace LinenLady.API.AI.Client;

using System.Text;
using System.Text.Json;
using LinenLady.API.AI.Options;
using Microsoft.Extensions.Options;

public sealed class AzureOpenAiChatClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<AzureOpenAiChatClient> _logger;
    private readonly AzureOpenAiOptions _opts;

    public AzureOpenAiChatClient(
        HttpClient http,
        ILogger<AzureOpenAiChatClient> logger,
        IOptions<AzureOpenAiOptions> opts)
    {
        _http = http;
        _logger = logger;
        _opts = opts.Value;
    }

    public async Task<T?> CompleteJsonAsync<T>(
        object messages,
        CancellationToken ct,
        double temperature = 0.2,
        int maxTokens = 400)
    {
        if (string.IsNullOrWhiteSpace(_opts.Endpoint)
            || string.IsNullOrWhiteSpace(_opts.ApiKey)
            || string.IsNullOrWhiteSpace(_opts.Deployment))
        {
            throw new InvalidOperationException("Missing Azure OpenAI options (Endpoint/ApiKey/Deployment).");
        }

        var url = $"{_opts.Endpoint.TrimEnd('/')}/openai/deployments/{_opts.Deployment}"
                + $"/chat/completions?api-version={_opts.ApiVersion}";

        var payload = new { messages, temperature, max_tokens = maxTokens };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("api-key", _opts.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Azure OpenAI error {Status}: {Body}", (int)resp.StatusCode, body);
            throw new InvalidOperationException($"Azure OpenAI error {(int)resp.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var contentText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return JsonSerializer.Deserialize<T>(ExtractFirstJsonObject(contentText), JsonOpts);
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s.Substring(start, end - start + 1) : "{}";
    }
}
