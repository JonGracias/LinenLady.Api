namespace LinenLady.API.AI.Seo.Service;

using System.Text;
using LinenLady.API.AI.Client;
using LinenLady.API.Inventory.AiMeta.Sql;

public sealed class AiSeoService : IAiSeoService
{
    private readonly AzureOpenAiChatClient _chat;
    private readonly IInventoryAiMetaRepository _aiMeta;

    public AiSeoService(AzureOpenAiChatClient chat, IInventoryAiMetaRepository aiMeta)
    {
        _chat = chat;
        _aiMeta = aiMeta;
    }

    public async Task<GenerateSeoResult> GenerateAsync(int inventoryId, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid id.");

        var item = await _aiMeta.GetItemContextAsync(inventoryId, ct)
            ?? throw new KeyNotFoundException($"Item {inventoryId} not found.");

        var userPrompt = BuildPrompt(item);

        var messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user",   content = userPrompt }
        };

        var seoJson = await _chat.CompleteRawJsonAsync(
            messages, ct,
            temperature: 0.3,
            maxTokens: 1000,
            forceJsonObject: true);

        await _aiMeta.UpsertSeoAsync(inventoryId, seoJson, ct);

        return new GenerateSeoResult(inventoryId, seoJson);
    }

    private static string BuildPrompt(AiMetaItemContext item)
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
}
