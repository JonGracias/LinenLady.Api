namespace LinenLady.API.Site.Blob;

using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using LinenLady.API.Blob.Options;
using LinenLady.API.Contracts;
using Microsoft.Extensions.Options;

/// <summary>
/// SAS URL generation for site media blobs. Used by site handlers to attach
/// read URLs to media DTOs and to issue upload URLs to the client.
/// </summary>
public sealed class SiteMediaSasService
{
    private readonly BlobStorageOptions _opts;
    private readonly ILogger<SiteMediaSasService> _logger;

    public SiteMediaSasService(IOptions<BlobStorageOptions> opts, ILogger<SiteMediaSasService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public string GenerateReadSas(string blobPath, int minutesTtl = 60)
    {
        if (!EnsureConfigured()) return "";

        try
        {
            var client = new BlobClient(_opts.ConnectionString, _opts.SiteMediaContainerName, blobPath);
            var sas = new BlobSasBuilder
            {
                BlobContainerName = _opts.SiteMediaContainerName,
                BlobName          = blobPath,
                Resource          = "b",
                ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(minutesTtl),
            };
            sas.SetPermissions(BlobSasPermissions.Read);
            return client.GenerateSasUri(sas).ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate read SAS for {BlobPath}.", blobPath);
            return "";
        }
    }

    public string GenerateUploadSas(string blobPath, string contentType)
    {
        if (!EnsureConfigured())
            throw new InvalidOperationException("Blob storage is not configured (BlobStorage:ConnectionString).");

        var client = new BlobClient(_opts.ConnectionString, _opts.SiteMediaContainerName, blobPath);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = _opts.SiteMediaContainerName,
            BlobName          = blobPath,
            Resource          = "b",
            ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(30),
            ContentType       = contentType,
        };
        sas.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);
        return client.GenerateSasUri(sas).ToString();
    }

    public SiteMediaDto WithReadUrl(SiteMediaDto m) =>
        m with { ReadUrl = GenerateReadSas(m.BlobPath) };

    public SiteConfigDto WithReadUrl(SiteConfigDto c) =>
        c with { Media = c.Media is null ? null : WithReadUrl(c.Media) };

    public HeroSlideDto WithReadUrl(HeroSlideDto s) =>
        s with { Media = s.Media is null ? null : WithReadUrl(s.Media) };

    private bool EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_opts.ConnectionString)
            && !string.IsNullOrWhiteSpace(_opts.SiteMediaContainerName))
        {
            return true;
        }

        _logger.LogError(
            "Blob storage misconfigured: ConnectionString or SiteMediaContainerName is empty.");
        return false;
    }
}
