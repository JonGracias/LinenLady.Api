// Application/Site/SiteMediaService.cs
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using LinenLady.API.Contracts;

namespace LinenLady.Inventory.Application.Site;

/// <summary>
/// Shared service for SAS URL generation — injected into all site handlers
/// that need to attach read URLs to media DTOs.
/// </summary>
public sealed class SiteMediaService
{
    private readonly string _connStr;
    private readonly string _container;

    public SiteMediaService()
    {
        _connStr   = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING") ?? "";
        _container = Environment.GetEnvironmentVariable("SITE_MEDIA_CONTAINER_NAME") ?? "site-media";
    }

    public string GenerateReadSas(string blobPath, int minutesTtl = 60)
    {
        try
        {
            var client     = new BlobClient(_connStr, _container, blobPath);
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _container,
                BlobName          = blobPath,
                Resource          = "b",
                ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(minutesTtl),
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return client.GenerateSasUri(sasBuilder).ToString();
        }
        catch { return ""; }
    }

    public string GenerateUploadSas(string blobPath, string contentType)
    {
        try
        {
            var client     = new BlobClient(_connStr, _container, blobPath);
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _container,
                BlobName          = blobPath,
                Resource          = "b",
                ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(30),
                ContentType       = contentType,
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);
            return client.GenerateSasUri(sasBuilder).ToString();
        }
        catch { return ""; }
    }

    public SiteMediaDto WithReadUrl(SiteMediaDto m) =>
        m with { ReadUrl = GenerateReadSas(m.BlobPath) };

    public SiteConfigDto WithReadUrl(SiteConfigDto c) =>
        c with { Media = c.Media is null ? null : WithReadUrl(c.Media) };

    public HeroSlideDto WithReadUrl(HeroSlideDto s) =>
        s with { Media = s.Media is null ? null : WithReadUrl(s.Media) };
}