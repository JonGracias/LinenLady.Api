namespace LinenLady.API.Site.Sql;

using LinenLady.API.Contracts;

public interface ISiteRepository
{
    Task<List<SiteMediaDto>>  ListMediaAsync(CancellationToken ct);
    Task<SiteMediaDto?>       GetMediaAsync(int mediaId, CancellationToken ct);
    Task<SiteMediaDto>        CreateMediaAsync(string name, string blobPath, string contentType, long? fileSizeBytes, CancellationToken ct);
    Task<bool>                DeleteMediaAsync(int mediaId, CancellationToken ct);

    Task<SiteConfigDto?>      GetConfigAsync(string key, CancellationToken ct);
    Task<List<SiteConfigDto>> ListConfigAsync(CancellationToken ct);
    Task                      SetConfigAsync(string key, int? mediaId, CancellationToken ct);

    Task<List<HeroSlideDto>>  ListHeroSlidesAsync(bool activeOnly, CancellationToken ct);
    Task<HeroSlideDto?>       GetHeroSlideAsync(int slideId, CancellationToken ct);
    Task<HeroSlideDto>        CreateHeroSlideAsync(UpsertHeroSlideRequest req, CancellationToken ct);
    Task<HeroSlideDto?>       UpdateHeroSlideAsync(int slideId, UpsertHeroSlideRequest req, CancellationToken ct);
    Task<bool>                DeleteHeroSlideAsync(int slideId, CancellationToken ct);
    Task                      ReorderHeroSlidesAsync(List<SlideOrder> slides, CancellationToken ct);
}
