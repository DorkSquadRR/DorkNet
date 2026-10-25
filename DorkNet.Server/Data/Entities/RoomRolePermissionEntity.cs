using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One cell of a sub-room's role-permission matrix, as written by
/// <c>PUT rooms/{roomId}/subrooms/{subRoomId}/permissions</c> and served back
/// in the <c>Permissions</c> list of <c>GET photon_access_token</c>.
///
/// The 2023-03-21 client models a cell as <c>BNLPBMJJOMM</c>
/// (formatter <c>RecNet.Runtime/HDOJMEBCAGP.txt</c>, keys
/// <c>Permission</c> / <c>Role</c> / <c>Override</c> / <c>Type</c> /
/// <c>Value</c>): Permission and Value are strings, Role is the room-role
/// enum <c>OMMBGJMJJPN</c> (None 0, Host 10, Moderator 20, CoOwner 30,
/// TemporaryCoOwner 31, Creator 255), Type is <c>FEOJFGECKGH</c>
/// (Bool 0, Int 1, StringList 2) and Override is a bool.
///
/// The editor sends only the cells it changed (the room-permissions runtime
/// builds a one-element <c>BNLPBMJJOMM[]</c> per edit,
/// <c>OMJKHJLFOCO.AFHJLBBFAGP.&lt;LocalRequestModifyRolePermission&gt;g__ChangeAndRefresh</c>),
/// so the write is an upsert per (room, sub-room, role, permission) and never
/// a replace of the whole matrix.
/// </summary>
public class RoomRolePermissionEntity
{
    public long Id { get; set; }

    public long RoomId { get; set; }

    /// <summary>The wire sub-room id from the route (the same value the room
    /// details advertise as <c>SubRooms[].SubRoomId</c>).</summary>
    public long SubRoomId { get; set; }

    /// <summary>Wire room-role value (OMMBGJMJJPN), e.g. 30 = CoOwner, 255 = Creator.</summary>
    public int Role { get; set; }

    /// <summary>The permission id exactly as the client sent it.</summary>
    [MaxLength(128)]
    public string Permission { get; set; } = string.Empty;

    /// <summary>FEOJFGECKGH: 0 = Bool, 1 = Int, 2 = StringList.</summary>
    public int Type { get; set; }

    public bool Override { get; set; }

    /// <summary>The value exactly as the client sent it (a string on the wire).</summary>
    [MaxLength(4096)]
    public string Value { get; set; } = string.Empty;

    public long UpdatedByPlayerId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
