namespace LinenLady.API.Contracts;

// ── Media ────────────────────────────────────────────────────────────────────

public sealed record SiteMediaDto(
    int     MediaId,
    string  Name,
    string  BlobPath,
    string  ContentType,
    long?   FileSizeBytes,
    string? ReadUrl,         // SAS URL, generated on read
    DateTime UploadedAt
);

public sealed record CreateMediaRequest(
    string Name,
    string FileName,
    string ContentType,
    long?  FileSizeBytes
);

public sealed record CreateMediaResponse(
    int    MediaId,
    string BlobPath,
    string UploadUrl,    // SAS PUT URL for the client to upload directly
    string Method        // always "PUT"
);

// ── SiteConfig ───────────────────────────────────────────────────────────────

public sealed record SiteConfigDto(
    string         ConfigKey,
    int?           MediaId,
    SiteMediaDto?  Media,       // resolved media with ReadUrl
    DateTime       UpdatedAt
);

public sealed record SetConfigRequest(
    int? MediaId    // null = clear the assignment
);

// ── HeroSlide ────────────────────────────────────────────────────────────────

public sealed record HeroSlideDto(
    int            SlideId,
    int?           MediaId,
    SiteMediaDto?  Media,
    string?        Heading,
    string?        Subtext,
    string?        LinkUrl,
    string?        LinkLabel,
    int            SortOrder,
    bool           IsActive,
    DateTime       UpdatedAt
);

public sealed record UpsertHeroSlideRequest(
    int?    MediaId,
    string? Heading,
    string? Subtext,
    string? LinkUrl,
    string? LinkLabel,
    int     SortOrder,
    bool    IsActive
);

public sealed record ReorderHeroSlidesRequest(
    List<SlideOrder> Slides
);

public sealed record SlideOrder(
    int SlideId,
    int SortOrder
);

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