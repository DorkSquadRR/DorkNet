namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Admin-curated metadata for a room-scoped leaderboard
/// <c>StatChannel</c> id. The
/// 2020 watch reports raw integer channel ids via
/// <c>POST api/Leaderboard/v1/SetStats</c>; this table lets admins map
/// each channel to a specific room (so the room detail page can list
/// "its" leaderboards) and a human-readable name + ordering hint. Rows
/// here override the hardcoded <c>AdminController.KnownStatChannels</c>
/// fallback. Channels with no row stay listed as "Channel N".
///
/// One row per (room, channel). Repeated <c>SetStats</c> calls for the
/// same channel in different rooms do not share metadata or values.
/// </summary>
public class LeaderboardChannelMetaEntity
{
    /// <summary>Room this channel "belongs to" for the per-room detail
    /// view. 0 = global / unscoped legacy bucket.</summary>
    public long RoomId { get; set; }

    /// <summary>Same int the watch reports for this channel. Only unique
    /// together with <see cref="RoomId"/>.</summary>
    public int Channel { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>When true, a lower stored value is a better score (race
    /// times, lap times, etc.). The list endpoint sorts ascending.</summary>
    public bool LowerIsBetter { get; set; }

    /// <summary>UI hint: "count" (default), "time-ms" for elapsed-time
    /// channels, "score" for points. The SPA formats accordingly.</summary>
    public string ValueFormat { get; set; } = "count";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
