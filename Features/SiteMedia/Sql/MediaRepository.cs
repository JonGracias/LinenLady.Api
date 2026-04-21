using Microsoft.Data.SqlClient;
using LinenLady.API.Contracts;

namespace LinenLady.API.Site.Media.Sql;

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

public sealed class SiteRepository : ISiteRepository
{
    private readonly string _connStr;

    public SiteRepository(string connStr) => _connStr = connStr;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private SqlConnection Open() => new(_connStr);

    private static SiteMediaDto ReadMedia(SqlDataReader r, string? readUrl = null) => new(
        MediaId:       r.GetInt32(r.GetOrdinal("MediaId")),
        Name:          r.GetString(r.GetOrdinal("Name")),
        BlobPath:      r.GetString(r.GetOrdinal("BlobPath")),
        ContentType:   r.GetString(r.GetOrdinal("ContentType")),
        FileSizeBytes: r.IsDBNull(r.GetOrdinal("FileSizeBytes")) ? null : r.GetInt64(r.GetOrdinal("FileSizeBytes")),
        ReadUrl:       readUrl,
        UploadedAt:    r.GetDateTime(r.GetOrdinal("UploadedAt"))
    );

    // ── Media ─────────────────────────────────────────────────────────────────

    public async Task<List<SiteMediaDto>> ListMediaAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT MediaId, Name, BlobPath, ContentType, FileSizeBytes, UploadedAt
            FROM site.SiteMedia
            WHERE IsDeleted = 0
            ORDER BY UploadedAt DESC;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        using var r   = await cmd.ExecuteReaderAsync(ct);

        var list = new List<SiteMediaDto>();
        while (await r.ReadAsync(ct))
            list.Add(ReadMedia(r));
        return list;
    }

    public async Task<SiteMediaDto?> GetMediaAsync(int mediaId, CancellationToken ct)
    {
        const string sql = """
            SELECT MediaId, Name, BlobPath, ContentType, FileSizeBytes, UploadedAt
            FROM site.SiteMedia
            WHERE MediaId = @MediaId AND IsDeleted = 0;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MediaId", mediaId);
        using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMedia(r) : null;
    }

    public async Task<SiteMediaDto> CreateMediaAsync(
        string name, string blobPath, string contentType, long? fileSizeBytes, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO site.SiteMedia (Name, BlobPath, ContentType, FileSizeBytes)
            OUTPUT INSERTED.MediaId, INSERTED.Name, INSERTED.BlobPath,
                   INSERTED.ContentType, INSERTED.FileSizeBytes, INSERTED.UploadedAt
            VALUES (@Name, @BlobPath, @ContentType, @FileSizeBytes);
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Name",          name);
        cmd.Parameters.AddWithValue("@BlobPath",      blobPath);
        cmd.Parameters.AddWithValue("@ContentType",   contentType);
        cmd.Parameters.AddWithValue("@FileSizeBytes", (object?)fileSizeBytes ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return ReadMedia(r);
    }

    public async Task<bool> DeleteMediaAsync(int mediaId, CancellationToken ct)
    {
        const string sql = """
            UPDATE site.SiteMedia SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME()
            WHERE MediaId = @MediaId AND IsDeleted = 0;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MediaId", mediaId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ── SiteConfig ────────────────────────────────────────────────────────────

    public async Task<SiteConfigDto?> GetConfigAsync(string key, CancellationToken ct)
    {
        const string sql = """
            SELECT c.ConfigKey, c.MediaId, c.UpdatedAt,
                   m.Name, m.BlobPath, m.ContentType, m.FileSizeBytes, m.UploadedAt
            FROM site.SiteConfig c
            LEFT JOIN site.SiteMedia m ON m.MediaId = c.MediaId AND m.IsDeleted = 0
            WHERE c.ConfigKey = @Key;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Key", key);
        using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadConfig(r);
    }

    public async Task<List<SiteConfigDto>> ListConfigAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT c.ConfigKey, c.MediaId, c.UpdatedAt,
                   m.Name, m.BlobPath, m.ContentType, m.FileSizeBytes, m.UploadedAt
            FROM site.SiteConfig c
            LEFT JOIN site.SiteMedia m ON m.MediaId = c.MediaId AND m.IsDeleted = 0
            ORDER BY c.ConfigKey;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        using var r   = await cmd.ExecuteReaderAsync(ct);
        var list = new List<SiteConfigDto>();
        while (await r.ReadAsync(ct)) list.Add(ReadConfig(r));
        return list;
    }

    public async Task SetConfigAsync(string key, int? mediaId, CancellationToken ct)
    {
        const string sql = """
            MERGE site.SiteConfig AS t
            USING (SELECT @Key AS ConfigKey) AS s ON t.ConfigKey = s.ConfigKey
            WHEN MATCHED THEN
                UPDATE SET MediaId = @MediaId, UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (ConfigKey, MediaId) VALUES (@Key, @MediaId);
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Key",     key);
        cmd.Parameters.AddWithValue("@MediaId", (object?)mediaId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SiteConfigDto ReadConfig(SqlDataReader r)
    {
        var mediaId = r.IsDBNull(r.GetOrdinal("MediaId")) ? (int?)null : r.GetInt32(r.GetOrdinal("MediaId"));
        SiteMediaDto? media = null;
        if (mediaId.HasValue && !r.IsDBNull(r.GetOrdinal("Name")))
            media = ReadMedia(r);

        return new SiteConfigDto(
            ConfigKey: r.GetString(r.GetOrdinal("ConfigKey")),
            MediaId:   mediaId,
            Media:     media,
            UpdatedAt: r.GetDateTime(r.GetOrdinal("UpdatedAt"))
        );
    }

    // ── HeroSlides ────────────────────────────────────────────────────────────

    public async Task<List<HeroSlideDto>> ListHeroSlidesAsync(bool activeOnly, CancellationToken ct)
    {
        var sql = $"""
            SELECT s.SlideId, s.MediaId, s.Heading, s.Subtext, s.LinkUrl, s.LinkLabel,
                   s.SortOrder, s.IsActive, s.UpdatedAt,
                   m.Name, m.BlobPath, m.ContentType, m.FileSizeBytes, m.UploadedAt
            FROM site.HeroSlide s
            LEFT JOIN site.SiteMedia m ON m.MediaId = s.MediaId AND m.IsDeleted = 0
            {(activeOnly ? "WHERE s.IsActive = 1" : "")}
            ORDER BY s.SortOrder;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        using var r   = await cmd.ExecuteReaderAsync(ct);
        var list = new List<HeroSlideDto>();
        while (await r.ReadAsync(ct)) list.Add(ReadSlide(r));
        return list;
    }

    public async Task<HeroSlideDto?> GetHeroSlideAsync(int slideId, CancellationToken ct)
    {
        const string sql = """
            SELECT s.SlideId, s.MediaId, s.Heading, s.Subtext, s.LinkUrl, s.LinkLabel,
                   s.SortOrder, s.IsActive, s.UpdatedAt,
                   m.Name, m.BlobPath, m.ContentType, m.FileSizeBytes, m.UploadedAt
            FROM site.HeroSlide s
            LEFT JOIN site.SiteMedia m ON m.MediaId = s.MediaId AND m.IsDeleted = 0
            WHERE s.SlideId = @SlideId;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SlideId", slideId);
        using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadSlide(r) : null;
    }

    public async Task<HeroSlideDto> CreateHeroSlideAsync(UpsertHeroSlideRequest req, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO site.HeroSlide (MediaId, Heading, Subtext, LinkUrl, LinkLabel, SortOrder, IsActive)
            OUTPUT INSERTED.SlideId
            VALUES (@MediaId, @Heading, @Subtext, @LinkUrl, @LinkLabel, @SortOrder, @IsActive);
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        BindSlide(cmd, req);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        return (await GetHeroSlideAsync(id, ct))!;
    }

    public async Task<HeroSlideDto?> UpdateHeroSlideAsync(int slideId, UpsertHeroSlideRequest req, CancellationToken ct)
    {
        const string sql = """
            UPDATE site.HeroSlide
            SET MediaId   = @MediaId,
                Heading   = @Heading,
                Subtext   = @Subtext,
                LinkUrl   = @LinkUrl,
                LinkLabel = @LinkLabel,
                SortOrder = @SortOrder,
                IsActive  = @IsActive,
                UpdatedAt = SYSUTCDATETIME()
            WHERE SlideId = @SlideId;
            """;

        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        BindSlide(cmd, req);
        cmd.Parameters.AddWithValue("@SlideId", slideId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 0 ? null : await GetHeroSlideAsync(slideId, ct);
    }

    public async Task<bool> DeleteHeroSlideAsync(int slideId, CancellationToken ct)
    {
        const string sql = "DELETE FROM site.HeroSlide WHERE SlideId = @SlideId;";
        using var conn = Open();
        await conn.OpenAsync(ct);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SlideId", slideId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task ReorderHeroSlidesAsync(List<SlideOrder> slides, CancellationToken ct)
    {
        using var conn = Open();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        foreach (var s in slides)
        {
            using var cmd = new SqlCommand(
                "UPDATE site.HeroSlide SET SortOrder = @SortOrder WHERE SlideId = @SlideId;",
                conn, tx);
            cmd.Parameters.AddWithValue("@SortOrder", s.SortOrder);
            cmd.Parameters.AddWithValue("@SlideId",   s.SlideId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static void BindSlide(SqlCommand cmd, UpsertHeroSlideRequest req)
    {
        cmd.Parameters.AddWithValue("@MediaId",   (object?)req.MediaId   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Heading",   (object?)req.Heading   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Subtext",   (object?)req.Subtext   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LinkUrl",   (object?)req.LinkUrl   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LinkLabel", (object?)req.LinkLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
        cmd.Parameters.AddWithValue("@IsActive",  req.IsActive);
    }

    private static HeroSlideDto ReadSlide(SqlDataReader r)
    {
        var mediaId = r.IsDBNull(r.GetOrdinal("MediaId")) ? (int?)null : r.GetInt32(r.GetOrdinal("MediaId"));
        SiteMediaDto? media = null;
        if (mediaId.HasValue && !r.IsDBNull(r.GetOrdinal("Name")))
            media = ReadMedia(r);

        return new HeroSlideDto(
            SlideId:   r.GetInt32(r.GetOrdinal("SlideId")),
            MediaId:   mediaId,
            Media:     media,
            Heading:   r.IsDBNull(r.GetOrdinal("Heading"))   ? null : r.GetString(r.GetOrdinal("Heading")),
            Subtext:   r.IsDBNull(r.GetOrdinal("Subtext"))   ? null : r.GetString(r.GetOrdinal("Subtext")),
            LinkUrl:   r.IsDBNull(r.GetOrdinal("LinkUrl"))   ? null : r.GetString(r.GetOrdinal("LinkUrl")),
            LinkLabel: r.IsDBNull(r.GetOrdinal("LinkLabel")) ? null : r.GetString(r.GetOrdinal("LinkLabel")),
            SortOrder: r.GetInt32(r.GetOrdinal("SortOrder")),
            IsActive:  r.GetBoolean(r.GetOrdinal("IsActive")),
            UpdatedAt: r.GetDateTime(r.GetOrdinal("UpdatedAt"))
        );
    }
}