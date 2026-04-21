namespace LinenLady.API.AI.Blob.Options;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ImageContainerName { get; set; } = "inventory-images";
}
