using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Durable record of a private (invite-only) Photon match created via
/// <c>POST /goto/room/{name}?CreatePrivateInstance=True</c>. Replaces
/// the in-process <c>ConcurrentDictionary</c> in
/// <see cref="DorkNet.Server.Services.PrivateInstanceService"/> so
/// invitations land on any replica.
///
/// Without this row in shared storage, an invite issued by Player A
/// (replica 1) wouldn't be visible to Player B's <c>POST /goto/instance/{id}</c>
/// when their request lands on replica 2 — they'd be routed to a fresh
/// public lobby instead of the inviter's match.
///
/// The associated invite list lives in <see cref="PrivateInstanceInviteeEntity"/>;
/// we keep it as a join table rather than a JSON column so the per-row
/// "is X invited to Y?" check stays a fast indexed lookup.
/// </summary>
public class PrivateInstanceEntity
{
    /// <summary>The instance id used as the wire <c>roomInstanceId</c>
    /// in matchmaking responses. Generated server-side at creation
    /// time; stable for the lifetime of the match. NOT autoincrement —
    /// the value is computed from owner/room/sub plus a nonce so the
    /// id is itself unforgeable without the nonce.</summary>
    public long Id { get; set; }

    public long RoomId { get; set; }
    public long SubRoomId { get; set; }
    public long OwnerPlayerId { get; set; }

    [MaxLength(256)] public string PhotonRoomId { get; set; } = string.Empty;
    [MaxLength(64)]  public string Location { get; set; } = string.Empty;
    [MaxLength(256)] public string DataBlob { get; set; } = string.Empty;
    [MaxLength(16)]  public string PhotonRegion { get; set; } = "us";
    public int MaxCapacity { get; set; } = 8;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;

    /// <summary>Host-chosen room code, set via PUT
    /// <c>/roominstance/{id}/roomcode</c>. Empty means "no custom code", in
    /// which case the deterministic <c>RoomCodeService.Generate</c> value is
    /// served instead.</summary>
    [MaxLength(16)] public string RoomCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
