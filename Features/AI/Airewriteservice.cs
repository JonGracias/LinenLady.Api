// Application/Items/AiRewriteService.cs
using System.Text;
using System.Text.Json;

namespace LinenLady.API.AI.Rewrite.Service;

public sealed class AiRewriteService : IAiRewriteService
{
    private static readonly HttpClient Http = new();

    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _deployment;
    private readonly string _apiVersion;

    public AiRewriteService(
        string endpoint,
        string apiKey,
        string deployment,
        string? apiVersion = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _apiKey = apiKey;
        _deployment = deployment;
        _apiVersion = apiVersion ?? "2024-02-15-preview";
    }

    public async Task<AiRewriteOutput?> Rewrite(AiRewriteInput input, CancellationToken ct)
    {
        var url = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";

        var sb = new StringBuilder();
        sb.AppendLine("You are a product listing editor for an online resale store.");
        sb.AppendLine("Rewrite ONLY the requested fields. Return ONLY valid JSON (no markdown, no backticks).");
        sb.AppendLine();
        sb.AppendLine("Current listing:");
        sb.AppendLine($"  Name: {input.CurrentName}");
        sb.AppendLine($"  Description: {input.CurrentDescription}");
        sb.AppendLine($"  Price (cents): {input.CurrentPriceCents}");
        sb.AppendLine();
        sb.AppendLine($"Fields to rewrite: {string.Join(", ", input.Fields)}");

        if (!string.IsNullOrWhiteSpace(input.Hint))
        {
            sb.AppendLine();
            sb.AppendLine($"User hint: {input.Hint.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine("Return JSON with ONLY the fields being rewritten:");
        sb.AppendLine("{");

        if (input.Fields.Contains("name"))
            sb.AppendLine(@"  ""name"": ""rewritten product name"",");

        if (input.Fields.Contains("description"))
            sb.AppendLine(@"  ""description"": ""rewritten description"",");

        if (input.Fields.Contains("price"))
            sb.AppendLine(@"  ""priceCents"": 12345,");

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- name: short, product-style title");
        sb.AppendLine("- description: 1-2 sentences, factual, appealing");
        sb.AppendLine("- priceCents: integer cents (USD)");
        sb.AppendLine("- Only include fields that were requested");
        sb.AppendLine("- All string values MUST be in double quotes");

        var payload = new
        {
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are a JSON API. You MUST return ONLY valid JSON. All string values MUST be wrapped in double quotes. No markdown, no backticks, no explanation."
                },
                new
                {
                    role = "user",
                    content = sb.ToString()
                }
            },
            temperature = 0.2,  // lower temp for more reliable formatting
            max_tokens = 400
        };

        var json = JsonSerializer.Serialize(payload);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
        httpReq.Headers.Add("api-key", _apiKey);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(httpReq, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(respBody);
            var contentText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            var clean = ExtractFirstJsonObject(contentText);
            Console.WriteLine($"AI raw content: {clean}");

            var raw = JsonSerializer.Deserialize<RawAiResponse>(clean, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (raw is null) return null;

            return new AiRewriteOutput
            {
                Name = raw.Name,
                Description = raw.Description,
                PriceCents = raw.PriceCents ?? raw.UnitPriceCents
            };
        }
        catch
        {
            // AI returned malformed JSON — fail gracefully
            return null;
        }
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start)
            return s.Substring(start, end - start + 1);
        return "{}";
    }

    private sealed class RawAiResponse
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? PriceCents { get; set; }
        public int? UnitPriceCents { get; set; }
    }
}