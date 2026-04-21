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