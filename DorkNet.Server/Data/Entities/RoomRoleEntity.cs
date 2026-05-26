namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-room player role grant. Drives RoomDetails.CoOwners /
/// Moderators / Hosts arrays (and their Invited* counterparts when
/// <see cref="Accepted"/> is false). Each (RoomId, PlayerId, Role)
/// tuple is unique — granting the same role twice is a no-op.
/// </summary>
public class RoomRoleEntity
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public long PlayerId { get; set; }

    /// <summary>RoleKind: 0 = CoOwner, 1 = Moderator, 2 = Host.</summary>
    public int Role { get; set; }

    /// <summary>False while the invite is still pending. Accepted
    /// rows show up in CoOwners/Moderators/Hosts; pending rows show
    /// up in InvitedCoOwners/InvitedModerators/InvitedHosts.</summary>
    public bool Accepted { get; set; } = true;

    public long? GrantedByPlayerId { get; set; }
    public System.DateTime GrantedAt { get; set; } = System.DateTime.UtcNow;
}
