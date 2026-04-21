namespace LinenLady.API.Contracts;

public sealed record FileSpec(
    string? FileName,
    string? ContentType);

public sealed record CreateItemsRequest(
    string? TitleHint,
    string? Notes,
    int? Count,
    List<FileSpec>? Files);

public sealed record UploadTarget(
    int Index,
    string BlobName,
    string UploadUrl,
    string Method,
    Dictionary<string, string> RequiredHeaders,
    string ContentType);

public sealed record CreateItemsResult(
    int InventoryId,
    string PublicId,
    string Sku,
    string Container,
    DateTime ExpiresOnUtc,
    List<UploadTarget> Uploads);
