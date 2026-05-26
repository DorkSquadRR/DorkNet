using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Single-row Postgres-backed store for the dorm community board state.
///
/// Wire shape mirrors what the watch's
/// <c>CommunityBoard.CommunityBoardDTO.Deserialize</c> reads
/// (Cpp2IL_ISIL/.../CommunityBoard_NestedType_CommunityBoardDTO.txt:460-480):
/// <c>FeaturedPlayer</c> / <c>FeaturedRoomGroup</c> /
/// <c>CurrentAnnouncement</c> / <c>InstagramImages</c> / <c>Videos</c>.
///
/// Pre-PR-3 wrote the JSON to <c>data/community_board.json</c> on disk +
/// kept an in-memory cache. With horizontal scale, an admin edit on
/// replica A wouldn't invalidate replica B's cache — admins would see
/// stale state until the next restart. The single-row table sidesteps
/// the cache-invalidation problem entirely: every read is one cheap
/// SELECT, every write is one UPDATE/INSERT, both visible to all
/// replicas immediately.
///
/// Service is now <b>scoped</b> rather than singleton because it holds
/// a DbContext reference.
/// </summary>
public class CommunityBoardService(DorkNetDbContext db)
{
    /// <summary>Fixed PK for the single board row. We pin to id=1 rather
    /// than using a singleton-table pattern because EF Core needs a
    /// trackable key.</summary>
    private const int RowId = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<CommunityBoardState> GetAsync()
    {
        var row = await db.CommunityBoardRows.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == RowId);
        if (row is null) return DefaultState();
        try
        {
            return JsonSerializer.Deserialize<CommunityBoardState>(row.Json, JsonOpts)
                ?? DefaultState();
        }
        catch (JsonException)
        {
            // Corrupt row — return default rather than 500. Next admin
            // edit will overwrite the bad payload.
            return DefaultState();
        }
    }

    public async Task<CommunityBoardState> UpdateAsync(CommunityBoardState next)
    {
        var json = JsonSerializer.Serialize(next, JsonOpts);
        var existing = await db.CommunityBoardRows.FirstOrDefaultAsync(c => c.Id == RowId);
        if (existing is null)
        {
            db.CommunityBoardRows.Add(new CommunityBoardEntity
            {
                Id = RowId,
                Json = json,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Json = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return next;
    }

    private static CommunityBoardState DefaultState() => new()
    {
        CurrentAnnouncement = new AnnouncementState
        {
            Message = "Welcome to the private server.",
            MoreInfoUrl = string.Empty,
        },
    };
}

/// <summary>Storage shape for the community board. Field names match
/// the watch's CommunityBoardDTO keys (verified in
/// CommunityBoard_NestedType_CommunityBoardDTO.txt) so the same record
/// can be returned verbatim from <c>GET /api/communityboard/v1/current</c>.</summary>
public class CommunityBoardState
{
    public FeaturedPlayerState? FeaturedPlayer { get; set; }
    public FeaturedRoomGroupState? FeaturedRoomGroup { get; set; }
    public AnnouncementState? CurrentAnnouncement { get; set; }
    public List<InstagramImageState> InstagramImages { get; set; } = new();
    public List<VideoState> Videos { get; set; } = new();
}

/// <summary>CommunityBoardFeaturedPlayerData: Id / TitleOverride / UrlOverride
/// (CommunityBoard_NestedType_CommunityBoardFeaturedPlayerData.txt:410-420).</summary>
public class FeaturedPlayerState
{
    public int Id { get; set; }
    public string TitleOverride { get; set; } = string.Empty;
    public string UrlOverride { get; set; } = string.Empty;
}

/// <summary>FeaturedRoomGroupDTO: Name / FeaturedRooms list
/// (Rooms_NestedType_FeaturedRoomGroupDTO.txt:85-90). FeaturedRooms is
/// a list of room ids — the watch fetches each via /api/rooms/v2/{id}.</summary>
public class FeaturedRoomGroupState
{
    public string Name { get; set; } = string.Empty;
    public List<long> FeaturedRooms { get; set; } = new();
}

/// <summary>CommunityBoardAnnouncementDTO: Message / MoreInfoUrl
/// (CommunityBoard_NestedType_CommunityBoardAnnouncementDTO.txt:85-90).</summary>
public class AnnouncementState
{
    public string Message { get; set; } = string.Empty;
    public string MoreInfoUrl { get; set; } = string.Empty;
}

/// <summary>InstagramImageDTO: ImageName / ImageUrl
/// (CommunityBoard_NestedType_InstagramImageDTO.txt:85-90).</summary>
public class InstagramImageState
{
    public string ImageName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

/// <summary>CommunityBoardVideoData: BlobName / Title / Description /
/// ThumbnailBlobName / SourceUrl
/// (CommunityBoard_NestedType_CommunityBoardVideoData.txt:460-480).</summary>
public class VideoState
{
    public string BlobName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ThumbnailBlobName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
}
