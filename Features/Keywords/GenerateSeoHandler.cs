// /Application/Keywords/GenerateSeoHandler.cs
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace LinenLady.API.Keywords;

public record SeoData(
    string Title,
    string MetaDescription,
    string OgTitle,
    string OgDescription,
    object JsonLd
);

public record GenerateSeoResult(
    int     InventoryId,
    string  SeoJson
);

public sealed class GenerateSeoHandler
{
    private readonly ILogger<GenerateSeoHandler> _logger;
    private static readonly HttpClient Http = new();

    public GenerateSeoHandler(ILogger<GenerateSeoHandler> logger)
    {
        _logger = logger;
    }

    public async Task<GenerateSeoResult> HandleAsync(int inventoryId, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid id.");

        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Missing SQL_CONNECTION_STRING.");

        // 1. Load item + existing keywords
        var item = await LoadItemAsync(connStr, inventoryId, ct)
            ?? throw new KeyNotFoundException($"Item {inventoryId} not found.");

        // 2. Generate SEO JSON via OpenAI
        var seoJson = await GenerateSeoAsync(item, ct);

        // 3. Save to inv.InventoryAiMeta
        await SaveSeoAsync(connStr, inventoryId, seoJson, ct);

        return new GenerateSeoResult(inventoryId, seoJson);
    }

    // ── Data loading ────────────────────────────────────────────────────────

    private record ItemData(
        string  Name,
        string? Description,
        int     UnitPriceCents,
        string? KeywordsJson
    );

    private static async Task<ItemData?> LoadItemAsync(
        string connStr, int inventoryId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                i.Name,
                i.Description,
                i.UnitPriceCents,
                m.KeywordsJson
            FROM inv.Inventory i
            LEFT JOIN inv.InventoryAiMeta m ON m.InventoryId = i.InventoryId
            WHERE i.InventoryId = @Id AND i.IsDeleted = 0;
            """;

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", inventoryId);

        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        return new ItemData(
            Name:           r.GetString(0),
            Description:    r.IsDBNull(1) ? null : r.GetString(1),
            UnitPriceCents: r.GetInt32(2),
            KeywordsJson:   r.IsDBNull(3) ? null : r.GetString(3)
        );
    }

    // ── Azure OpenAI ────────────────────────────────────────────────────────

    private async Task<string> GenerateSeoAsync(ItemData item, CancellationToken ct)
    {
        var endpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")?.TrimEnd('/');
        var apiKey     = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
        var apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-02-15-preview";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Missing Azure OpenAI chat config.");

        var prompt = BuildPrompt(item);
        var url    = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user",   content = prompt }
            },
            temperature     = 0.3,
            max_tokens      = 1000,
            response_format = new { type = "json_object" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", apiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure OpenAI error {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        try { JsonDocument.Parse(content); }
        catch { content = "{}"; }

        return content;
    }

    private static string BuildPrompt(ItemData item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Item name: {item.Name}");
        sb.AppendLine($"Price: ${item.UnitPriceCents / 100.0:F2}");

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"Item description (IMPORTANT — use this as the basis for metaDescription and jsonLd.description): {item.Description}");
        }

        if (!string.IsNullOrWhiteSpace(item.KeywordsJson))
        {
            sb.AppendLine();
            sb.AppendLine($"Structured keywords already generated for this item: {item.KeywordsJson}");
        }

        return sb.ToString().Trim();
    }

    private const string SystemPrompt = """
        You are an SEO specialist for an antique and vintage item marketplace.

        Given item details and keywords, generate SEO metadata optimized for Google discovery
        of antique and vintage items. Be specific, descriptive, and keyword-rich but natural.

        Return ONLY a JSON object with exactly these fields:

        {
          "title": "string — page <title> tag, max 60 chars, include item name + 1-2 key descriptors",
          "metaDescription": "- metaDescription: expand slightly on the item description for SEO, but it must be recognizably based on it — do not invent new details, includes key search terms",
          "ogTitle": "string — Open Graph title for social sharing, can be slightly more engaging than title",
          "ogDescription": "string — OG description for social sharing, 1-2 sentences, evocative",
          "jsonLd": {
            "@context": "https://schema.org",
            "@type": "Product",
            "name": "string",
            "description": "use the item description verbatim or with only minor grammatical cleanup",
            "offers": {
              "@type": "Offer",
              "priceCurrency": "USD",
              "price": "number as string e.g. '45.00'",
              "availability": "https://schema.org/InStock",
              "itemCondition": "https://schema.org/UsedCondition"
            },
            "category": "string — best category for this item",
            "keywords": "string — comma-separated keywords from the structured keywords"
          }
        }

        Keep title under 60 characters. Keep metaDescription between 140-155 characters.
        The jsonLd.description should be the full, human-readable item description.
        """;

    // ── DB save ─────────────────────────────────────────────────────────────

    private static async Task SaveSeoAsync(
        string connStr, int inventoryId, string seoJson, CancellationToken ct)
    {
        const string sql = """
            MERGE inv.InventoryAiMeta AS t
            USING (SELECT @InventoryId AS InventoryId) AS s
            ON t.InventoryId = s.InventoryId
            WHEN MATCHED THEN
                UPDATE SET
                    SeoJson          = @SeoJson,
                    SeoGeneratedAt   = SYSUTCDATETIME(),
                    UpdatedAt        = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (InventoryId, SeoJson, SeoGeneratedAt)
                VALUES (@InventoryId, @SeoJson, SYSUTCDATETIME());
            """;

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@InventoryId", inventoryId);
        cmd.Parameters.AddWithValue("@SeoJson",     seoJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}