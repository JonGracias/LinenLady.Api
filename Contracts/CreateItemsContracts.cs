namespace LinenLady.API.Contracts;

/// <summary>
/// Request sent by frontend to prepare upload targets (SAS URLs) for a new draft item.
/// Mirrors your existing CreateDraftRequest + FileSpec. :contentReference[oaicite:3]{index=3}
/// </summary>
public static class CreateItemsContracts
{
    public sealed record FileSpec(
        string? FileName,
        string? ContentType
    );

    public sealed record CreateItemsRequest(
        string? TitleHint,
        string? Notes,
        int? Count,
        List<FileSpec>? Files
    );

    public sealed record UploadTarget(
        int Index,
        string BlobName,
        string UploadUrl,
        string Method,
        Dictionary<string, string> RequiredHeaders,
        string ContentType
    );

    public sealed record CreateItemsResult(
        int InventoryId,
        string PublicId,
        string Sku,
        string Container,
        DateTime ExpiresOnUtc,
        List<UploadTarget> Uploads
    );
}
