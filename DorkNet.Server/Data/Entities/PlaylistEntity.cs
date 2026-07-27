using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A curated or user-created list of rooms surfaced by the watch's
/// /roomserver/roomsandplaylists/* endpoints. The 2020.12 watch
/// deserializes union responses where each entry is either a Room
/// (KLCOGEIGEBJ) or a Playlist (KMKPEOGJDFK) — the discriminator is
/// the presence of the lowercase "roomId" key (factory at
/// MKAMHOIHOJK.txt:621).
///
/// Required wire fields for the playlist subclass (KMKPEOGJDFK.txt:68
/// reads PlaylistId; base keys at MKAMHOIHOJK.txt:516-612 cover
/// Name/Description/ImageName/Stats/etc.).
/// </summary>
public class PlaylistEntity
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>Player who built / curates the list. Curated/system
    /// playlists are owned by the seed account (id=1).</summary>
    public long CreatorPlayerId { get; set; } = 1;

    /// <summary>Surfaces in /api/curatedroomplaylists. Curated lists
    /// are sorted by <see cref="OrderIndex"/> and always come from the
    /// seed account.</summary>
    public bool IsCurated { get; set; } = false;

    /// <summary>Sort key for curated lists — lower comes first.
    /// Ignored for user-created lists (which sort by HotScore proxy).</summary>
    public int OrderIndex { get; set; } = 0;

    /// <summary>Comma-separated tags. Matches the same convention used
    /// by <see cref="RoomEntity.TagsCsv"/> — the hot endpoint applies
    /// a substring filter against this column.</summary>
    [MaxLength(1024)]
    public string TagsCsv { get; set; } = string.Empty;

    public int CheerCount { get; set; } = 0;
    public int FavoriteCount { get; set; } = 0;

    /// <summary>Unique visitors across the rooms this playlist points
    /// at — bumps when a player visits any member room.</summary>
    public int VisitorCount { get; set; } = 0;

    /// <summary>Total joins across all member rooms.</summary>
    public int VisitCount { get; set; } = 0;

    /// <summary>Playlist accessibility enum as the client sends it
    /// (0 = private, 1 = public). Previously the accessibility/restrictions/
    /// level-voting/warning mutations were acknowledged and thrown away, so a
    /// creator could never actually publish a playlist or change its settings —
    /// the wire always reported the hardcoded defaults back.</summary>
    public int Accessibility { get; set; } = 0;

    public bool SupportsLevelVoting { get; set; } = false;
    public bool SupportsJuniors { get; set; } = true;
    public bool SupportsScreens { get; set; } = true;
    public bool SupportsTeleportVR { get; set; } = true;
    public bool SupportsWalkVR { get; set; } = true;

    /// <summary>Content-warning bitmask, plus the creator's free-text warning.</summary>
    public int WarningMask { get; set; } = 0;

    [MaxLength(512)]
    public string CustomWarning { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
