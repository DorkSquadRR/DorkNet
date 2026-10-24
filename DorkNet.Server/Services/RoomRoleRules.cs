using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// The 2023 room-role ladder and who may change whose role.
///
/// <para><b>Wire enum.</b> The client's room role is <c>OMMBGJMJJPN : byte</c>
/// (recnet-runtime-decomp/OMMBGJMJJPN.cs): None 0, Banned 1, Host 10,
/// Moderator 20, CoOwner 30, TemporaryCoOwner 31, Creator 255. It is
/// gap-skipping, like the club MembershipType enum, so never treat it as an
/// index. <see cref="RoomRoleEntity.Role"/> stores a compact kind instead
/// (0 = CoOwner, 1 = Moderator, 2 = Host); <see cref="ToWire"/> and
/// <see cref="TryFromWire"/> convert.</para>
///
/// <para><b>The creator is 255, not 30.</b> The client reads the local
/// player's role straight out of the room details <c>Roles</c> list
/// (<c>FGCPNAACHIK.ONECLLALJEO</c>: first row whose AccountId matches), and
/// the room-permissions runtime only lets a player edit a role strictly
/// below their own, with Creator (255) the one unconditional pass
/// (<c>OMJKHJLFOCO.DIIMLMKEJLC</c>,
/// IsilDump/RecRoom.RoomPermissions.Runtime/OMJKHJLFOCO.txt:2367:
/// <c>if role == 255 return true; if target &gt;= role return false</c>).
/// Advertising the creator as CoOwner (30) therefore locked the room's own
/// creator out of the Co-Owner and Creator permission rows.</para>
///
/// <para><b>Server rule</b> (<see cref="CheckChange"/>): the creator may set
/// any role on anyone but themselves. Otherwise the actor must hold an
/// accepted CoOwner role, and both the target's current role (accepted or
/// still invited) and the requested role must be strictly below the
/// actor's. A player may always drop their own role or decline their own
/// invite (role 0), and may accept exactly the role they were invited to.</para>
/// </summary>
public static class RoomRoleRules
{
    public const int WireNone = 0;
    public const int WireBanned = 1;
    public const int WireHost = 10;
    public const int WireModerator = 20;
    public const int WireCoOwner = 30;
    public const int WireTemporaryCoOwner = 31;
    public const int WireCreator = 255;

    public const int StoredCoOwner = 0;
    public const int StoredModerator = 1;
    public const int StoredHost = 2;

    /// <summary>Stored kind → wire role.</summary>
    public static int ToWire(int storedRole) => storedRole switch
    {
        StoredCoOwner => WireCoOwner,
        StoredModerator => WireModerator,
        StoredHost => WireHost,
        _ => WireNone,
    };

    /// <summary>Wire role → stored kind. Only the three persistable roles
    /// map; None, Banned, TemporaryCoOwner (an in-instance grant) and Creator
    /// (ownership, changed through <c>rooms/{id}/creator</c>) do not.</summary>
    public static bool TryFromWire(int wireRole, out int storedRole)
    {
        storedRole = wireRole switch
        {
            WireCoOwner => StoredCoOwner,
            WireModerator => StoredModerator,
            WireHost => StoredHost,
            _ => -1,
        };
        return storedRole >= 0;
    }

    /// <summary>The player's accepted wire role in the room: 255 for the
    /// creator, the highest accepted grant otherwise, 0 for none.</summary>
    public static int AcceptedRank(RoomEntity room, IEnumerable<RoomRoleEntity> rows, long playerId)
    {
        if (room.CreatorPlayerId == playerId) return WireCreator;
        return rows
            .Where(r => r.RoomId == room.Id && r.PlayerId == playerId && r.Accepted)
            .Select(r => ToWire(r.Role))
            .DefaultIfEmpty(WireNone)
            .Max();
    }

    /// <summary>The highest role the player has been invited to and not yet
    /// accepted, 0 for none.</summary>
    public static int InvitedRank(RoomEntity room, IEnumerable<RoomRoleEntity> rows, long playerId)
        => rows
            .Where(r => r.RoomId == room.Id && r.PlayerId == playerId && !r.Accepted)
            .Select(r => ToWire(r.Role))
            .DefaultIfEmpty(WireNone)
            .Max();

    /// <summary>True when the player may change room-level settings and the
    /// sub-room settings that hang off them: the creator or an accepted
    /// co-owner. This is the same gate as the bare room mutations
    /// (RoomsModerationController.RequireOwnedRoomAsync) and room saves.</summary>
    public static bool CanEditRoom(RoomEntity room, IEnumerable<RoomRoleEntity> rows, long playerId)
        => AcceptedRank(room, rows, playerId) >= WireCoOwner;

    /// <summary>What a role write will do once <see cref="CheckChange"/> allows it.</summary>
    public enum ChangeKind
    {
        /// <summary>Grant or replace the target's role (accepted at once).</summary>
        Grant,
        /// <summary>Record a pending invite the target must accept.</summary>
        Invite,
        /// <summary>The target accepts their own pending invite.</summary>
        AcceptInvite,
        /// <summary>Remove the target's role and any pending invite.</summary>
        Clear,
    }

    /// <summary>Decide whether <paramref name="actorId"/> may set
    /// <paramref name="targetId"/>'s role to <paramref name="requestedWireRole"/>.
    /// Returns null when allowed, otherwise an error code for the response.
    /// <paramref name="kind"/> is what the caller should then do.</summary>
    public static string? CheckChange(
        RoomEntity room,
        IReadOnlyCollection<RoomRoleEntity> rows,
        long actorId,
        long targetId,
        int requestedWireRole,
        bool isInvite,
        out ChangeKind kind)
    {
        kind = requestedWireRole == WireNone
            ? ChangeKind.Clear
            : isInvite ? ChangeKind.Invite : ChangeKind.Grant;

        if (targetId == room.CreatorPlayerId)
            return "cannot_change_creator_role";

        if (requestedWireRole != WireNone && !TryFromWire(requestedWireRole, out _))
            return "invalid_role";

        var actorRank = AcceptedRank(room, rows, actorId);

        // Self-service: anyone may give up their own role or decline their
        // own invite, and accept the exact role they were invited to.
        if (actorId == targetId && actorRank != WireCreator)
        {
            if (requestedWireRole == WireNone) return null;
            if (!isInvite && requestedWireRole == InvitedRank(room, rows, targetId))
            {
                kind = ChangeKind.AcceptInvite;
                return null;
            }
            return "forbidden";
        }

        if (actorRank == WireCreator) return null;
        if (actorRank < WireCoOwner) return "forbidden";

        var targetRank = Math.Max(
            AcceptedRank(room, rows, targetId),
            InvitedRank(room, rows, targetId));
        if (targetRank >= actorRank) return "forbidden";
        if (requestedWireRole >= actorRank) return "forbidden";
        return null;
    }

    /// <summary>True when <paramref name="playerId"/> may write the
    /// permission row for <paramref name="wireRole"/>: the creator may edit
    /// every role (including CoOwner and Creator); a co-owner only the roles
    /// below their own. Mirrors the client's OMJKHJLFOCO.DIIMLMKEJLC.</summary>
    public static bool CanEditRolePermissions(
        RoomEntity room, IEnumerable<RoomRoleEntity> rows, long playerId, int wireRole)
    {
        var rank = AcceptedRank(room, rows, playerId);
        if (rank == WireCreator) return true;
        if (rank < WireCoOwner) return false;
        // TemporaryCoOwner collapses to CoOwner on the client (ONECLLALJEO).
        var target = wireRole == WireTemporaryCoOwner ? WireCoOwner : wireRole;
        return target < rank;
    }

    /// <summary>The room details / roles-list rows: one per account, with
    /// the creator first as 255 and every other account's accepted role in
    /// <c>Role</c> and pending invite in <c>InvitedRole</c>. Rows the
    /// creator happens to own in RoomRoles are ignored so the creator is
    /// never listed twice (the client takes the first match).</summary>
    public static List<RoomRoleWire> BuildWires(RoomEntity room, IEnumerable<RoomRoleEntity> rows)
    {
        var list = new List<RoomRoleWire>
        {
            new(ToClientAccountId(room.CreatorPlayerId), WireCreator, WireNone, null),
        };
        foreach (var group in rows
                     .Where(r => r.RoomId == room.Id && r.PlayerId != room.CreatorPlayerId)
                     .GroupBy(r => r.PlayerId)
                     .OrderBy(g => g.Key))
        {
            var accepted = group.Where(r => r.Accepted).ToList();
            var invited = group.Where(r => !r.Accepted).ToList();
            var role = accepted.Select(r => ToWire(r.Role)).DefaultIfEmpty(WireNone).Max();
            var invitedRole = invited.Select(r => ToWire(r.Role)).DefaultIfEmpty(WireNone).Max();
            var lastChangedBy = group
                .OrderByDescending(r => r.GrantedAt)
                .Select(r => r.GrantedByPlayerId)
                .FirstOrDefault();
            list.Add(new RoomRoleWire(
                ToClientAccountId(group.Key),
                role,
                invitedRole,
                lastChangedBy is long by ? ToClientAccountId(by) : null));
        }
        return list;
    }

    /// <summary>One <c>EFHPLDPNGIM</c> row: <c>{AccountId: Int32, Role,
    /// InvitedRole, LastChangedByAccountId: Int32?}</c>.</summary>
    public sealed record RoomRoleWire(int AccountId, int Role, int InvitedRole, int? LastChangedByAccountId);

    /// <summary>Account ids are Int32 in the 2023 role DTO. Clamp rather than
    /// wrap so an out-of-range id is obviously wrong.</summary>
    public static int ToClientAccountId(long accountId)
        => accountId > int.MaxValue
            ? int.MaxValue
            : accountId < int.MinValue
                ? int.MinValue
                : (int)accountId;
}
