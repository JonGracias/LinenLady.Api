namespace LinenLady.API.Site.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Site.Blob;
using LinenLady.API.Site.Sql;

public sealed class SiteMediaHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaSasService _sas;

    public SiteMediaHandler(ISiteRepository repo, SiteMediaSasService sas)
    {
        _repo = repo;
        _sas = sas;
    }

    public async Task<List<SiteMediaDto>> ListAsync(CancellationToken ct)
    {
        var list = await _repo.ListMediaAsync(ct);
        return list.Select(_sas.WithReadUrl).ToList();
    }

    public async Task<CreateMediaResponse> CreateAsync(CreateMediaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.FileName))
            throw new ArgumentException("Name and FileName are required.");

        var ext = Path.GetExtension(req.FileName).TrimStart('.').ToLowerInvariant();
        var blobPath = $"site-media/{Guid.NewGuid():N}.{ext}";

        var media = await _repo.CreateMediaAsync(req.Name, blobPath, req.ContentType, req.FileSizeBytes, ct);
        var uploadUrl = _sas.GenerateUploadSas(blobPath, req.ContentType);

        return new CreateMediaResponse(media.MediaId, blobPath, uploadUrl, "PUT");
    }

    public Task<bool> DeleteAsync(int mediaId, CancellationToken ct)
        => _repo.DeleteMediaAsync(mediaId, ct);
}
