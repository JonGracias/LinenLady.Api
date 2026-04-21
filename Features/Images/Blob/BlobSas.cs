// /Infrastructure/Blob/BlobSas.cs
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace LinenLady.API.Blob;

public static class BlobSas
{
    public static string BuildReadUrl(
        string connectionString,
        string containerName,
        string blobName,
        TimeSpan ttl)
    {
        var container = new BlobContainerClient(connectionString, containerName);
        var blobClient = container.GetBlobClient(blobName);

        var (accountName, accountKey) = ParseCredentials(connectionString);
        var cred = new StorageSharedKeyCredential(accountName, accountKey);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl),
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        var qs = sas.ToSasQueryParameters(cred).ToString();
        return $"{blobClient.Uri}?{qs}";
    }

    public static (string UploadUrl, Dictionary<string, string> RequiredHeaders) BuildUploadUrl(
        string connectionString,
        string containerName,
        string blobName,
        string contentType,
        TimeSpan ttl)
    {
        var container = new BlobContainerClient(connectionString, containerName);
        var blobClient = container.GetBlobClient(blobName);

        var (accountName, accountKey) = ParseCredentials(connectionString);
        var cred = new StorageSharedKeyCredential(accountName, accountKey);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl),
        };
        sas.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var qs = sas.ToSasQueryParameters(cred).ToString();
        var uploadUrl = $"{blobClient.Uri}?{qs}";

        var headers = new Dictionary<string, string>
        {
            ["x-ms-blob-type"] = "BlockBlob",
            ["Content-Type"]   = contentType,
        };

        return (uploadUrl, headers);
    }

    private static (string AccountName, string AccountKey) ParseCredentials(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var accountName = parts
            .First(p => p.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
            .Split('=', 2)[1];
        var accountKey = parts
            .First(p => p.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
            .Split('=', 2)[1];
        return (accountName, accountKey);
    }
}