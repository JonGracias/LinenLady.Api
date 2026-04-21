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

    /// <summary>
    /// Sends a chat-completion request and deserializes the model's reply
    /// (expected JSON object) into T.
    /// </summary>
    public async Task<T?> CompleteJsonAsync<T>(
        object messages,
        CancellationToken ct,
        double temperature = 0.2,
        int maxTokens = 400,
        bool forceJsonObject = false)
    {
        var raw = await CompleteRawJsonAsync(messages, ct, temperature, maxTokens, forceJsonObject);
        return JsonSerializer.Deserialize<T>(ExtractFirstJsonObject(raw), JsonOpts);
    }

    /// <summary>
    /// Sends a chat-completion request and returns the raw JSON text the model
    /// produced, guaranteed to be parseable (malformed responses return "{}").
    /// Useful when the caller wants to persist the JSON as-is.
    /// </summary>
    public async Task<string> CompleteRawJsonAsync(
        object messages,
        CancellationToken ct,
        double temperature = 0.2,
        int maxTokens = 400,
        bool forceJsonObject = false)
    {
        if (string.IsNullOrWhiteSpace(_opts.Endpoint)
            || string.IsNullOrWhiteSpace(_opts.ApiKey)
            || string.IsNullOrWhiteSpace(_opts.Deployment))
        {
            throw new InvalidOperationException("Missing Azure OpenAI options (Endpoint/ApiKey/Deployment).");
        }

        var url = $"{_opts.Endpoint.TrimEnd('/')}/openai/deployments/{_opts.Deployment}"
                + $"/chat/completions?api-version={_opts.ApiVersion}";

        object payload = forceJsonObject
            ? new { messages, temperature, max_tokens = maxTokens, response_format = new { type = "json_object" } }
            : new { messages, temperature, max_tokens = maxTokens };

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

            // Drill into content-filter details if present
            try
            {
                using var errDoc = JsonDocument.Parse(body);
                if (errDoc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("innererror", out var inner))
                {
                    _logger.LogError("Azure OpenAI content filter innererror: {Inner}", inner.ToString());
                }
            }
            catch { /* raw body already logged */ }

            throw new InvalidOperationException($"Azure OpenAI error {(int)resp.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var contentText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        // Guarantee parseable JSON — fall back to empty object on malformed output
        try { JsonDocument.Parse(contentText); return contentText; }
        catch { return "{}"; }
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s.Substring(start, end - start + 1) : "{}";
    }
}
