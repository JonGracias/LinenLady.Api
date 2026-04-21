namespace LinenLady.API.AI.Rewrite.Service;

using System.Text;
using LinenLady.API.AI.Client;
using LinenLady.API.Contracts;

public sealed class AiRewriteService : IAiRewriteService
{
    private readonly AzureOpenAiChatClient _chat;

    public AiRewriteService(AzureOpenAiChatClient chat)
    {
        _chat = chat;
    }

    public async Task<AiPrefillResult?> Rewrite(AiRewriteInput input, CancellationToken ct)
    {
        var userPrompt = BuildUserPrompt(input);

        var messages = new object[]
        {
            new
            {
                role = "system",
                content = "You are a JSON API. You MUST return ONLY valid JSON. "
                        + "All string values MUST be wrapped in double quotes. "
                        + "No markdown, no backticks, no explanation."
            },
            new { role = "user", content = userPrompt }
        };

        return await _chat.CompleteJsonAsync<AiPrefillResult>(messages, ct);
    }

    private static string BuildUserPrompt(AiRewriteInput input)
    {
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
            sb.AppendLine(@"  ""unitPriceCents"": 12345,");

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- name: short, product-style title");
        sb.AppendLine("- description: 1-2 sentences, factual, appealing");
        sb.AppendLine("- unitPriceCents: integer cents (USD)");
        sb.AppendLine("- Only include fields that were requested");
        sb.AppendLine("- All string values MUST be in double quotes");

        return sb.ToString();
    }
}
