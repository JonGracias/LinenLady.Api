namespace LinenLady.API.Site.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Site.Blob;
using LinenLady.API.Site.Sql;

public sealed class SiteHeroHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaSasService _sas;

    public SiteHeroHandler(ISiteRepository repo, SiteMediaSasService sas)
    {
        _repo = repo;
        _sas = sas;
    }

    public async Task<List<HeroSlideDto>> ListAsync(bool activeOnly, CancellationToken ct)
    {
        var slides = await _repo.ListHeroSlidesAsync(activeOnly, ct);
        return slides.Select(_sas.WithReadUrl).ToList();
    }

    public async Task<HeroSlideDto> CreateAsync(UpsertHeroSlideRequest req, CancellationToken ct)
    {
        var slide = await _repo.CreateHeroSlideAsync(req, ct);
        return _sas.WithReadUrl(slide);
    }

    public async Task<HeroSlideDto?> UpdateAsync(int slideId, UpsertHeroSlideRequest req, CancellationToken ct)
    {
        var slide = await _repo.UpdateHeroSlideAsync(slideId, req, ct);
        return slide is null ? null : _sas.WithReadUrl(slide);
    }

    public Task<bool> DeleteAsync(int slideId, CancellationToken ct)
        => _repo.DeleteHeroSlideAsync(slideId, ct);

    public Task ReorderAsync(List<SlideOrder> slides, CancellationToken ct)
        => _repo.ReorderHeroSlidesAsync(slides, ct);
}
