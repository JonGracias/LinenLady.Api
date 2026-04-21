namespace LinenLady.API.Site.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Site.Blob;
using LinenLady.API.Site.Sql;

public sealed class SiteConfigHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaSasService _sas;

    public SiteConfigHandler(ISiteRepository repo, SiteMediaSasService sas)
    {
        _repo = repo;
        _sas = sas;
    }

    public async Task<List<SiteConfigDto>> ListAsync(CancellationToken ct)
    {
        var list = await _repo.ListConfigAsync(ct);
        return list.Select(_sas.WithReadUrl).ToList();
    }

    public async Task<SiteConfigDto?> GetAsync(string key, CancellationToken ct)
    {
        var config = await _repo.GetConfigAsync(key, ct);
        return config is null ? null : _sas.WithReadUrl(config);
    }

    public async Task<SiteConfigDto> SetAsync(string key, int? mediaId, CancellationToken ct)
    {
        await _repo.SetConfigAsync(key, mediaId, ct);
        var config = await _repo.GetConfigAsync(key, ct);
        return _sas.WithReadUrl(config!);
    }
}
