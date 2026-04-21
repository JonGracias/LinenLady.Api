// /Application/Items/CreateItemsHandler.cs
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using LinenLady.API.Keywords;
using LinenLady.API.Contracts;
using NanoidDotNet;

namespace LinenLady.API.Inventory.Items.Handler;

static class Sku
{
    // URL-safe alphabet (64 chars)
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

    public static string NewCode(int length = 7)
        => Nanoid.Generate(Alphabet, length);

    public static bool IsUniqueViolation(SqlException ex)
        => ex.Number is 2627 or 2601;
}

/// <summary>
/// Creates a draft item (inv.Inventory) and returns SAS upload targets for images.
/// After creation, triggers keyword + SEO generation directly via GenerateKeywordsHandler.
/// </summary>
public sealed class CreateItemsHandler
{
    private readonly ILogger<CreateItemsHandler> _logger;
    private readonly GenerateKeywordsHandler _keywordsHandler;

    public CreateItemsHandler(
        ILogger<CreateItemsHandler> logger,
        GenerateKeywordsHandler keywordsHandler)
    {
        _logger          = logger;
        _keywordsHandler = keywordsHandler;
    }

    public async Task<CreateItemsResult> HandleAsync(CreateItemsRequest body, CancellationToken ct)
    {
        // -----------------------------
        // 1) Read environment config
        // -----------------------------
        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException("Server misconfigured: missing SQL_CONNECTION_STRING.");

        var storageConn =
            Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING") ??
            Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        if (string.IsNullOrWhiteSpace(storageConn))
            throw new InvalidOperationException("Server misconfigured: missing storage connection string.");

        var containerName =
            Environment.GetEnvironmentVariable("IMAGE_CONTAINER_NAME") ??
            "inventory-images";

        // -----------------------------
        // 2) Validate request + clamp
        // -----------------------------
        var files = body.Files;
        int requestedCount = (files is { Count: > 0 }) ? files.Count : (body.Count ?? 0);
        if (requestedCount <= 0)
            throw new ArgumentException("Provide files[] or count > 0.");

        int count = Math.Clamp(requestedCount, 1, 4);

        // -----------------------------
        // 3) Generate identifiers
        // -----------------------------
        var publicId  = Guid.NewGuid();
        var publicIdN = publicId.ToString("N");
       

        var name = string.IsNullOrWhiteSpace(body.TitleHint)
            ? "Draft"
            : body.TitleHint!.Trim();

        var description = string.IsNullOrWhiteSpace(body.Notes)
            ? null
            : body.Notes!.Trim();

        // -----------------------------
        // 4) Insert draft into SQL
        // -----------------------------
        const string insertSql = @"
        INSERT INTO inv.Inventory (PublicId, Sku, Name, Description)
        OUTPUT INSERTED.InventoryId
        VALUES (@PublicId, @Sku, @Name, @Description);";

        int inventoryId = 0;
        string sku = "";
        bool inserted = false;

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            const int maxAttempts = 12;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                sku = Sku.NewCode(7);            

                try
                {
                    using var cmd = new SqlCommand(insertSql, conn) { CommandTimeout = 30 };

                    cmd.Parameters.Add("@PublicId", System.Data.SqlDbType.UniqueIdentifier).Value = publicId;
                    cmd.Parameters.Add("@Sku", System.Data.SqlDbType.NVarChar, 64).Value = sku;
                    cmd.Parameters.Add("@Name", System.Data.SqlDbType.NVarChar, 255).Value = name;
                    cmd.Parameters.Add("@Description", System.Data.SqlDbType.NVarChar, -1)
                        .Value = (object?)description ?? DBNull.Value;

                    inventoryId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
                    inserted = true;
                    break; // ✅ success
                }
                catch (SqlException ex) when (Sku.IsUniqueViolation(ex) && attempt < maxAttempts)
                {
                    // collision -> retry
                }
            }

            if (!inserted)
                throw new InvalidOperationException("Failed to allocate SKU after retries.");
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error creating draft item.");
            throw new InvalidOperationException("Database error.");
        }

        // -----------------------------
        // 5) Generate SAS upload URLs
        // -----------------------------
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(15);

        CreateItemsResult result;
        try
        {
            var service   = new BlobServiceClient(storageConn);
            var container = service.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);

            var targets = new List<UploadTarget>(count);

            for (int i = 0; i < count; i++)
            {
                FileSpec? spec = null;
                if (files is { Count: > 0 } && i < files.Count)
                    spec = files[i];

                var ext         = NormalizeExtension(spec?.FileName) ?? ".jpg";
                var contentType = NormalizeContentType(spec?.ContentType, ext);
                var blobName    = $"images/{publicIdN}/{i + 1:00}-{Guid.NewGuid():N}{ext}";
                var blobClient  = container.GetBlobClient(blobName);

                var sas = new BlobSasBuilder
                {
                    BlobContainerName = containerName,
                    BlobName          = blobName,
                    Resource          = "b",
                    ExpiresOn         = expiresOn
                };
                sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

                targets.Add(new UploadTarget(
                    Index:  i + 1,
                    BlobName:    blobName,
                    UploadUrl:   blobClient.GenerateSasUri(sas).ToString(),
                    Method:      "PUT",
                    RequiredHeaders: new Dictionary<string, string>
                    {
                        ["x-ms-blob-type"] = "BlockBlob",
                        ["Content-Type"]   = contentType
                    },
                    ContentType: contentType
                ));
            }

            result = new CreateItemsResult(
                InventoryId:  inventoryId,
                PublicId:     publicIdN,
                Sku:          sku,
                Container:    containerName,
                ExpiresOnUtc: expiresOn.UtcDateTime,
                Uploads:      targets
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "SAS generation failed.");
            throw new InvalidOperationException("SAS generation failed. Ensure storage connection string includes AccountKey.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error creating draft uploads.");
            throw new InvalidOperationException("Server error.");
        }

        // -----------------------------
        // 6) Kick off keywords + SEO
        //    Non-fatal — runs after the result is ready to return.
        //    Note: at this point only TitleHint/Notes are in the DB.
        //    AI vision hasn't run yet so name/description may be minimal.
        //    Keywords will be sparse now but will be regenerated automatically
        //    after the frontend calls ai-vision and the user saves the item.
        // -----------------------------
        _ = Task.Run(async () =>
        {
            try
            {
                await _keywordsHandler.HandleAsync(inventoryId, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Keywords/SEO generation failed for new item {Id} (non-fatal).", inventoryId);
            }
        });

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? NormalizeExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var ext = Path.GetExtension(fileName.Trim()).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" => ext,
            _ => ".jpg"
        };
    }

    private static string NormalizeContentType(string? contentType, string ext)
    {
        if (!string.IsNullOrWhiteSpace(contentType)) return contentType.Trim();
        return ext switch
        {
            ".png"  => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            _       => "image/jpeg"
        };
    }
}