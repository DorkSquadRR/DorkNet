using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Active game session backing the matchmaking <c>JoinOrCreate</c> flow
/// in <see cref="DorkNet.Server.Services.GameSessionService"/>. Replaces
/// the in-process <c>ConcurrentDictionary</c> + atomic counter so:
/// <list type="bullet">
/// <item>Session ids are allocated by Postgres bigserial — no
///     replica-local counter that collides with another replica's
///     after restart.</item>
/// <item>A "find a non-full session in this room+region" query
///     coordinates across all replicas; without it, two players
///     joining via different replicas could each create their own
///     fresh session for the same room and end up isolated.</item>
/// </list>
///
/// Sessions are deleted when their PlayerCount hits 0 (handled in the
/// service, no FK cascade needed). Brief mid-rejoin flicker is fine —
/// the next JoinOrCreate just creates a new session with a new id.
/// </summary>
public class GameSessionEntity
{
    /// <summary>bigserial — allocated by Postgres on insert. Crosses
    /// replicas safely because the sequence is shared.</summary>
    public long Id { get; set; }

    [MaxLength(64)] public string RoomId { get; set; } = string.Empty;
    [MaxLength(64)] public string ActivityLevelId { get; set; } = string.Empty;
    [MaxLength(16)] public string Region { get; set; } = "us";
    [MaxLength(128)] public string PhotonRoomName { get; set; } = string.Empty;
    public int MaxCapacity { get; set; } = 8;
    public int PlayerCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
