// Application/Site/SiteHandlers.cs
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Sql;

namespace LinenLady.Inventory.Application.Site;

// ── Media ─────────────────────────────────────────────────────────────────────

public sealed class ListSiteMediaHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public ListSiteMediaHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<List<SiteMediaDto>> HandleAsync(CancellationToken ct)
    {
        var list = await _repo.ListMediaAsync(ct);
        return list.Select(_svc.WithReadUrl).ToList();
    }
}

public sealed class CreateSiteMediaHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public CreateSiteMediaHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<CreateMediaResponse> HandleAsync(CreateMediaRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.FileName))
            throw new ArgumentException("Name and FileName are required.");

        var ext      = Path.GetExtension(req.FileName).TrimStart('.').ToLowerInvariant();
        var blobPath = $"site-media/{Guid.NewGuid():N}.{ext}";

        var media     = await _repo.CreateMediaAsync(req.Name, blobPath, req.ContentType, req.FileSizeBytes, ct);
        var uploadUrl = _svc.GenerateUploadSas(blobPath, req.ContentType);

        return new CreateMediaResponse(media.MediaId, blobPath, uploadUrl, "PUT");
    }
}

public sealed class DeleteSiteMediaHandler
{
    private readonly ISiteRepository _repo;

    public DeleteSiteMediaHandler(ISiteRepository repo) => _repo = repo;

    public async Task<bool> HandleAsync(int mediaId, CancellationToken ct)
        => await _repo.DeleteMediaAsync(mediaId, ct);
}

// ── SiteConfig ────────────────────────────────────────────────────────────────

public sealed class ListSiteConfigHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public ListSiteConfigHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<List<SiteConfigDto>> HandleAsync(CancellationToken ct)
    {
        var list = await _repo.ListConfigAsync(ct);
        return list.Select(_svc.WithReadUrl).ToList();
    }
}

public sealed class GetSiteConfigHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public GetSiteConfigHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<SiteConfigDto?> HandleAsync(string key, CancellationToken ct)
    {
        var config = await _repo.GetConfigAsync(key, ct);
        return config is null ? null : _svc.WithReadUrl(config);
    }
}

public sealed class SetSiteConfigHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public SetSiteConfigHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<SiteConfigDto> HandleAsync(string key, int? mediaId, CancellationToken ct)
    {
        await _repo.SetConfigAsync(key, mediaId, ct);
        var config = await _repo.GetConfigAsync(key, ct);
        return _svc.WithReadUrl(config!);
    }
}

// ── HeroSlides ────────────────────────────────────────────────────────────────

public sealed class ListHeroSlidesHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public ListHeroSlidesHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<List<HeroSlideDto>> HandleAsync(bool activeOnly, CancellationToken ct)
    {
        var slides = await _repo.ListHeroSlidesAsync(activeOnly, ct);
        return slides.Select(_svc.WithReadUrl).ToList();
    }
}

public sealed class CreateHeroSlideHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public CreateHeroSlideHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<HeroSlideDto> HandleAsync(UpsertHeroSlideRequest req, CancellationToken ct)
    {
        var slide = await _repo.CreateHeroSlideAsync(req, ct);
        return _svc.WithReadUrl(slide);
    }
}

public sealed class UpdateHeroSlideHandler
{
    private readonly ISiteRepository _repo;
    private readonly SiteMediaService _svc;

    public UpdateHeroSlideHandler(ISiteRepository repo, SiteMediaService svc)
    {
        _repo = repo;
        _svc  = svc;
    }

    public async Task<HeroSlideDto?> HandleAsync(int slideId, UpsertHeroSlideRequest req, CancellationToken ct)
    {
        var slide = await _repo.UpdateHeroSlideAsync(slideId, req, ct);
        return slide is null ? null : _svc.WithReadUrl(slide);
    }
}

public sealed class DeleteHeroSlideHandler
{
    private readonly ISiteRepository _repo;

    public DeleteHeroSlideHandler(ISiteRepository repo) => _repo = repo;

    public async Task<bool> HandleAsync(int slideId, CancellationToken ct)
        => await _repo.DeleteHeroSlideAsync(slideId, ct);
}

public sealed class ReorderHeroSlidesHandler
{
    private readonly ISiteRepository _repo;

    public ReorderHeroSlidesHandler(ISiteRepository repo) => _repo = repo;

    public async Task HandleAsync(List<SlideOrder> slides, CancellationToken ct)
        => await _repo.ReorderHeroSlidesAsync(slides, ct);
}