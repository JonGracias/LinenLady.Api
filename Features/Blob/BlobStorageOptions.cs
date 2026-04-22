namespace LinenLady.API.Blob.Options;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ImageContainerName { get; set; } = "inventory-images";
    public string SiteMediaContainerName { get; set; } = "site-media";
}
