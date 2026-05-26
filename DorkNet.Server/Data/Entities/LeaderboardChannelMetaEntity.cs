namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Admin-curated metadata for a leaderboard <c>StatChannel</c> id. The
/// 2020 watch reports raw integer channel ids via
/// <c>POST api/Leaderboard/v1/SetStats</c>; this table lets admins map
/// each channel to a specific room (so the room detail page can list
/// "its" leaderboards) and a human-readable name + ordering hint. Rows
/// here override the hardcoded <c>AdminController.KnownStatChannels</c>
/// fallback. Channels with no row stay listed as "Channel N".
///
/// One channel id, one row. Repeated <c>SetStats</c> calls for the same
/// channel from any room write to that channel's value rows; the meta
/// row only changes when an admin renames or re-maps it.
/// </summary>
public class LeaderboardChannelMetaEntity
{
    /// <summary>Same int the watch reports for this channel. Primary
    /// key — there's only ever one meta row per channel.</summary>
    public int Channel { get; set; }

    /// <summary>Room this channel "belongs to" for the per-room detail
    /// view. 0 = global / unscoped (e.g. cross-room player progression
    /// counters). The same room can own many channels (e.g. Stunt
    /// Runner has one channel per course).</summary>
    public long RoomId { get; set; }

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
