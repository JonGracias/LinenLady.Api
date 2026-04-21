namespace LinenLady.API.AI.Keywords.Service;

using System.Text;
using LinenLady.API.AI.Client;
using LinenLady.API.AI.Embeddings.Service;
using LinenLady.API.AI.Seo.Service;
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.AiMeta.Sql;

public sealed class AiKeywordsService : IAiKeywordsService
{
    private readonly AzureOpenAiChatClient _chat;
    private readonly IInventoryAiMetaRepository _aiMeta;
    private readonly IAiEmbeddingsService _embeddings;
    private readonly IAiSeoService _seo;
    private readonly ILogger<AiKeywordsService> _logger;

    public AiKeywordsService(
        AzureOpenAiChatClient chat,
        IInventoryAiMetaRepository aiMeta,
        IAiEmbeddingsService embeddings,
        IAiSeoService seo,
        ILogger<AiKeywordsService> logger)
    {
        _chat = chat;
        _aiMeta = aiMeta;
        _embeddings = embeddings;
        _seo = seo;
        _logger = logger;
    }

    public async Task<GenerateKeywordsResult> GenerateAsync(
        int inventoryId, string? hint, CancellationToken ct)
    {
        if (inventoryId <= 0) throw new ArgumentException("Invalid id.");

        var item = await _aiMeta.GetItemContextAsync(inventoryId, ct)
            ?? throw new KeyNotFoundException($"Item {inventoryId} not found.");

        var userPrompt = BuildPrompt(item, hint);

        var messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user",   content = userPrompt }
        };

        var keywordsJson = await _chat.CompleteRawJsonAsync(
            messages, ct,
            temperature: 0.2,
            maxTokens: 800,
            forceJsonObject: true);

        await _aiMeta.UpsertKeywordsAsync(inventoryId, item.AdminNotes, keywordsJson, ct);

        var vectorRefreshed = await TryRefreshVector(inventoryId, ct);
        var seoRefreshed    = await TryRefreshSeo(inventoryId, ct);

        return new GenerateKeywordsResult(inventoryId, keywordsJson, vectorRefreshed, seoRefreshed);
    }

    private async Task<bool> TryRefreshVector(int inventoryId, CancellationToken ct)
    {
        try
        {
            var outcome = await _embeddings.RefreshAsync(
                inventoryId,
                new RefreshVectorRequest { Purpose = "item_text", Force = true },
                ct);

            return outcome.Status == RefreshVectorStatus.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector refresh failed for item {Id}.", inventoryId);
            return false;
        }
    }

    private async Task<bool> TryRefreshSeo(int inventoryId, CancellationToken ct)
    {
        try
        {
            await _seo.GenerateAsync(inventoryId, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SEO generation failed for item {Id} — keywords saved but SEO not updated.",
                inventoryId);
            return false;
        }
    }

    private static string BuildPrompt(AiMetaItemContext item, string? hint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Item name: {item.Name}");
        sb.AppendLine($"Price: ${item.UnitPriceCents / 100.0:F2}");

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"Description: {item.Description}");
        }

        if (!string.IsNullOrWhiteSpace(item.AdminNotes))
        {
            sb.AppendLine();
            sb.AppendLine($"Additional seller notes (private context, not shown publicly): {item.AdminNotes}");
        }

        if (!string.IsNullOrWhiteSpace(hint))
        {
            sb.AppendLine();
            sb.AppendLine($"Seller preference for keywords: {hint}");
        }

        return sb.ToString().Trim();
    }

    private const string SystemPrompt = """
        You are a product cataloguing assistant for an antique and vintage linen shop.
        The shop owner sells tablecloths, napkins, runners, lace, bed linens, and similar textile items
        at a weekend flea market. Your job is to help organize her inventory for her online shop.

        Given item details, extract structured keywords that will help buyers find this item through search.
        Be specific and thorough — think about what a buyer might type to find this item.

        Return ONLY a JSON object. Use only the categories that are relevant — omit categories that don't apply.
        You may invent category names appropriate to the item type.

        Example structure (adapt categories to the item):
        {
          "colors": ["blue", "gold", "ivory"],
          "materials": ["linen", "cotton"],
          "patterns": ["floral", "geometric"],
          "style": ["art deco", "Victorian", "farmhouse"],
          "era": ["1920s", "mid-century"],
          "condition": ["excellent", "minor wear"],
          "use_case": ["dining", "wedding", "decorative"],
          "item_type": ["tablecloth", "napkin", "runner"],
          "dimensions": ["rectangular", "60x120 inches"],
          "descriptors": ["ornate", "hand-embroidered", "delicate"],
          "search_keywords": ["vintage linen tablecloth", "antique embroidered runner"]
        }

        The "search_keywords" array should contain 3-8 natural language phrases a buyer might search for.

        If seller preferences are provided, incorporate them into your keyword selection accordingly.
        """;
}
