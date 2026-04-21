using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using LinenLady.Inventory.Api.Contracts;
using LinenLady.Inventory.Api.Contracts.Requests;
using LinenLady.Inventory.Api.Options;
using LinenLady.Inventory.Api.Services.Interfaces;
using LinenLady.Inventory.Functions.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace LinenLady.API.AI.Service;

public sealed class AiPrefillService : IAiPrefillService
{
    private readonly ILogger<AiPrefillService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly AzureOpenAiOptions _openAi;
    private readonly BlobStorageOptions _blobOptions;

    public AiPrefillService(
        ILogger<AiPrefillService> logger,
        HttpClient httpClient,
        IConfiguration configuration,
        IOptions<AzureOpenAiOptions> openAi,
        IOptions<BlobStorageOptions> blobOptions)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
        _openAi = openAi.Value;
        _blobOptions = blobOptions.Value;
    }

    public async Task<PrefillOutcome> PrefillAsync(
        int inventoryId,
        PrefillMode mode,
        AiPrefillRequest request,
        CancellationToken cancellationToken)
    {
        request.MaxImages = Math.Clamp(request.MaxImages, 1, 8);

        var sqlConnStr = _configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");

        // 0) Validate item exists and get its PublicId (blob prefix guard)
        var publicId = await LoadPublicId(sqlConnStr, inventoryId, cancellationToken);
        if (publicId is null)
            return new PrefillOutcome(PrefillStatus.NotFound, null, "Item not found (missing PublicId).");

        // 1) Load item + images
        var item = await LoadItem(sqlConnStr, inventoryId, cancellationToken);
        if (item is null)
            return new PrefillOutcome(PrefillStatus.NotFound, null, "Item not found.");

        if (item.Images.Count == 0)
            return new PrefillOutcome(PrefillStatus.NoImages, null, "Item has no images to analyze.");

        // 2) Pick images (explicit ids first, else top N by SortOrder)
        var selectedImages = SelectImages(item, request.ImageIds, request.MaxImages);
        if (selectedImages.Count == 0)
            return new PrefillOutcome(PrefillStatus.NoValidImages, null, "No valid images selected for analysis.");

        // 3) Build read SAS URLs
        var blobService = new BlobServiceClient(_blobOptions.ConnectionString);
        var container = blobService.GetBlobContainerClient(_blobOptions.ImageContainerName);

        var imageSasUrls = selectedImages
            .Select(img => ToBlobName(img.ImagePath, _blobOptions.ImageContainerName, publicId.Value))
            .Where(blobName => !string.IsNullOrWhiteSpace(blobName))
            .Select(blobName => MakeReadSas(container, blobName!, TimeSpan.FromMinutes(15)))
            .ToList();

        if (imageSasUrls.Count == 0)
        {
            return new PrefillOutcome(
                PrefillStatus.InvalidBlobPaths,
                null,
                $"Could not resolve valid blob names for images. Expected prefix: images/{publicId.Value:N}/");
        }

        // 4) Call Azure OpenAI
        var ai = await CallAzureOpenAi(imageSasUrls, request.TitleHint, request.Notes, cancellationToken);

        // 5) Merge proposed values based on mode + overwrite flag
        var overwrite = request.Overwrite;
        var newName = item.Name;
        var newDesc = item.Description;
        var newPrice = item.UnitPriceCents;

        if (mode is PrefillMode.All or PrefillMode.Title)
        {
            newName = PickString(
                overwrite,
                item.Name,
                ai.Name,
                s => string.IsNullOrWhiteSpace(s) || s.Equals("Draft", StringComparison.OrdinalIgnoreCase));
        }

        if (mode is PrefillMode.All or PrefillMode.Description)
        {
            newDesc = PickNullableString(
                overwrite,
                item.Description,
                ai.Description,
                s => string.IsNullOrWhiteSpace(s));
        }

        if (mode is PrefillMode.All or PrefillMode.Price)
        {
            newPrice = PickInt(
                overwrite,
                item.UnitPriceCents,
                SanitizePrice(ai.UnitPriceCents),
                p => p <= 0);
        }

        // No changes — return item as-is
        if (newName == item.Name && newDesc == item.Description && newPrice == item.UnitPriceCents)
            return new PrefillOutcome(PrefillStatus.Ok, item, null);

        // 6) Persist partial update
        await UpdateItemPartial(sqlConnStr, inventoryId, mode, newName, newDesc, newPrice, cancellationToken);

        // 7) Reload + return
        var updated = await LoadItem(sqlConnStr, inventoryId, cancellationToken);
        return new PrefillOutcome(PrefillStatus.Ok, updated, null);
    }

    // ---------- Image selection ----------

    private static List<InventoryImageDto> SelectImages(
        InventoryItemDto item,
        int[]? imageIds,
        int maxImages)
    {
        if (imageIds is { Length: > 0 })
        {
            var byId = item.Images.ToDictionary(i => i.ImageId, i => i);
            var picked = new List<InventoryImageDto>(capacity: Math.Min(maxImages, imageIds.Length));
            var seen = new HashSet<int>();

            foreach (var id in imageIds)
            {
                if (picked.Count >= maxImages) break;
                if (!seen.Add(id)) continue;
                if (byId.TryGetValue(id, out var img))
                    picked.Add(img);
            }

            if (picked.Count > 0)
                return picked;
        }

        return item.Images
            .OrderBy(i => i.SortOrder)
            .Take(maxImages)
            .ToList();
    }

    // ---------- SQL ----------

    private static async Task<Guid?> LoadPublicId(string sqlConnStr, int id, CancellationToken ct)
    {
        const string sql = @"
SELECT i.PublicId
FROM inv.Inventory i
WHERE i.InventoryId = @Id;
";
        using var conn = new SqlConnection(sqlConnStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", id);

        var val = await cmd.ExecuteScalarAsync(ct);
        if (val is null || val is DBNull) return null;
        return (Guid)val;
    }

    private static async Task<InventoryItemDto?> LoadItem(string sqlConnStr, int id, CancellationToken ct)
    {
        const string sql = @"
SELECT
    i.InventoryId,
    i.Sku,
    i.Name,
    i.Description,
    i.QuantityOnHand,
    i.UnitPriceCents,
    img.ImageId,
    img.ImagePath,
    img.IsPrimary,
    img.SortOrder
FROM inv.Inventory i
LEFT JOIN inv.InventoryImage img ON img.InventoryId = i.InventoryId
WHERE i.InventoryId = @Id
ORDER BY i.InventoryId, img.SortOrder;
";
        using var conn = new SqlConnection(sqlConnStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);

        InventoryItemDto? item = null;

        const int O_InventoryId = 0;
        const int O_Sku = 1;
        const int O_Name = 2;
        const int O_Description = 3;
        const int O_QuantityOnHand = 4;
        const int O_UnitPriceCents = 5;
        const int O_ImageId = 6;
        const int O_ImagePath = 7;
        const int O_IsPrimary = 8;
        const int O_SortOrder = 9;

        while (await reader.ReadAsync(ct))
        {
            if (item is null)
            {
                item = new InventoryItemDto
                {
                    InventoryId = reader.GetInt32(O_InventoryId),
                    Sku = reader.GetString(O_Sku),
                    Name = reader.GetString(O_Name),
                    Description = reader.IsDBNull(O_Description) ? null : reader.GetString(O_Description),
                    QuantityOnHand = reader.GetInt32(O_QuantityOnHand),
                    UnitPriceCents = reader.GetInt32(O_UnitPriceCents),
                };
            }

            if (!reader.IsDBNull(O_ImageId))
            {
                item.Images.Add(new InventoryImageDto
                {
                    ImageId = reader.GetInt32(O_ImageId),
                    ImagePath = reader.GetString(O_ImagePath),
                    IsPrimary = reader.GetBoolean(O_IsPrimary),
                    SortOrder = reader.GetInt32(O_SortOrder),
                });
            }
        }

        return item;
    }

    private static async Task UpdateItemPartial(
        string sqlConnStr,
        int id,
        PrefillMode mode,
        string name,
        string? description,
        int unitPriceCents,
        CancellationToken ct)
    {
        string sql = mode switch
        {
            PrefillMode.Title => "UPDATE inv.Inventory SET Name = @Name WHERE InventoryId = @Id;",
            PrefillMode.Description => "UPDATE inv.Inventory SET Description = @Description WHERE InventoryId = @Id;",
            PrefillMode.Price => "UPDATE inv.Inventory SET UnitPriceCents = @UnitPriceCents WHERE InventoryId = @Id;",
            _ => @"
UPDATE inv.Inventory
SET Name = @Name,
    Description = @Description,
    UnitPriceCents = @UnitPriceCents
WHERE InventoryId = @Id;"
        };

        using var conn = new SqlConnection(sqlConnStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.AddWithValue("@Id", id);

        if (mode is PrefillMode.All or PrefillMode.Title)
            cmd.Parameters.AddWithValue("@Name", name);

        if (mode is PrefillMode.All or PrefillMode.Description)
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);

        if (mode is PrefillMode.All or PrefillMode.Price)
            cmd.Parameters.AddWithValue("@UnitPriceCents", unitPriceCents);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- Blob + SAS ----------

    private static Uri MakeReadSas(BlobContainerClient container, string blobName, TimeSpan ttl)
    {
        var blob = container.GetBlobClient(blobName);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl),
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sas);
    }

    private static string? ToBlobName(string imagePath, string containerName, Guid publicId)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;

        string candidate;

        if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
        {
            candidate = uri.AbsolutePath.Trim('/');
            var parts = candidate.Split('/', 2);
            if (parts.Length == 2 && parts[0].Equals(containerName, StringComparison.OrdinalIgnoreCase))
                candidate = Uri.UnescapeDataString(parts[1]);
            else
                candidate = Uri.UnescapeDataString(candidate);
        }
        else
        {
            candidate = imagePath.TrimStart('/');
        }

        var q = candidate.IndexOf('?');
        if (q >= 0) candidate = candidate[..q];
        candidate = candidate.Trim();

        var requiredPrefix = $"images/{publicId:N}/";
        if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return candidate;
    }

    // ---------- Value merging ----------

    private static int? SanitizePrice(int? cents)
    {
        if (cents is null) return null;
        var v = cents.Value;
        if (v < 0) v = 0;
        if (v > 500_000) v = 500_000;
        return v;
    }

    private static string PickString(bool overwrite, string current, string? proposed, Func<string, bool> isPlaceholder)
    {
        if (overwrite)
            return !string.IsNullOrWhiteSpace(proposed) ? proposed.Trim() : current;

        if (isPlaceholder(current) && !string.IsNullOrWhiteSpace(proposed))
            return proposed.Trim();

        return current;
    }

    private static string? PickNullableString(bool overwrite, string? current, string? proposed, Func<string?, bool> isPlaceholder)
    {
        if (overwrite)
            return !string.IsNullOrWhiteSpace(proposed) ? proposed.Trim() : current;

        if (isPlaceholder(current) && !string.IsNullOrWhiteSpace(proposed))
            return proposed.Trim();

        return current;
    }

    private static int PickInt(bool overwrite, int current, int? proposed, Func<int, bool> isPlaceholder)
    {
        if (overwrite)
            return proposed ?? current;

        if (isPlaceholder(current) && proposed.HasValue)
            return proposed.Value;

        return current;
    }

    // ---------- Azure OpenAI call ----------

    private async Task<AiPrefillResult> CallAzureOpenAi(
        IReadOnlyList<Uri> imageUrls,
        string? titleHint,
        string? notes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_openAi.Endpoint)
            || string.IsNullOrWhiteSpace(_openAi.ApiKey)
            || string.IsNullOrWhiteSpace(_openAi.Deployment))
        {
            throw new InvalidOperationException("Missing Azure OpenAI options (Endpoint/ApiKey/Deployment).");
        }

        var endpoint = _openAi.Endpoint.TrimEnd('/');
        var url = $"{endpoint}/openai/deployments/{_openAi.Deployment}/chat/completions?api-version={_openAi.ApiVersion}";

        var sb = new StringBuilder();
        sb.AppendLine("Analyze the item photos and return ONLY valid JSON (no markdown).");
        sb.AppendLine("Schema:");
        sb.AppendLine("{");
        sb.AppendLine(@"  ""name"": string,");
        sb.AppendLine(@"  ""description"": string,");
        sb.AppendLine(@"  ""unitPriceCents"": number");
        sb.AppendLine("}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- name: short, product-style (no quotes)");
        sb.AppendLine("- description: 1-2 sentences, factual, avoid hype");
        sb.AppendLine("- unitPriceCents: integer cents (USD), reasonable resale price");

        if (!string.IsNullOrWhiteSpace(titleHint))
        {
            sb.AppendLine();
            sb.AppendLine("Title hint (optional; may be wrong):");
            sb.AppendLine(titleHint.Trim());
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.AppendLine();
            sb.AppendLine("Notes (optional; may be partial):");
            sb.AppendLine(notes.Trim());
        }

        var content = new List<object>
        {
            new { type = "text", text = sb.ToString() }
        };

        foreach (var u in imageUrls)
            content.Add(new { type = "image_url", image_url = new { url = u.ToString() } });

        var payload = new
        {
            messages = new object[] { new { role = "user", content } },
            temperature = 0.2,
            max_tokens = 400
        };

        var json = JsonSerializer.Serialize(payload);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
        httpReq.Headers.Add("api-key", _openAi.ApiKey);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(httpReq, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Azure OpenAI error {Status}: {Body}", (int)resp.StatusCode, respBody);
            throw new InvalidOperationException($"Azure OpenAI error {(int)resp.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(respBody);
        var contentText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        var clean = ExtractFirstJsonObject(contentText);

        return JsonSerializer.Deserialize<AiPrefillResult>(clean, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AiPrefillResult();
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start)
            return s.Substring(start, end - start + 1);
        return "{}";
    }
}
