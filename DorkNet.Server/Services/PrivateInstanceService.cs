using DorkNet.Server.Controllers.Match;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Cross-replica registry of invite-only matches created via
/// <c>POST /goto/room/{name}?CreatePrivateInstance=True</c>.
///
/// Why this exists: the watch sets <c>CreatePrivateInstance=True</c> in
/// the matchmaking form when the player picks "Private Instance". Our
/// /goto handler responds with a unique <c>photonRoomId</c> so randos
/// can't matchmake into it. Invitees later POST
/// <c>/goto/instance/{roomInstanceId}</c> carrying the inviter's
/// instance id; that handler looks up the registered photon room
/// here so they end up in the same Photon match instead of being
/// routed to a fresh public lobby.
///
/// **Multi-replica**: state lives in two Postgres tables —
/// <see cref="PrivateInstanceEntity"/> for the instance metadata and
/// <see cref="PrivateInstanceInviteeEntity"/> for the per-invite
/// access list. Pre-PR-3 used <c>ConcurrentDictionary</c> singletons
/// which split across replicas: an invite issued on replica A wasn't
/// visible to a goto/instance hitting replica B.
///
/// Service is now <b>scoped</b> rather than singleton because it
/// holds a DbContext reference. The previous singleton lifetime would
/// have leaked the DbContext for the lifetime of the app.
/// </summary>
public class PrivateInstanceService(DorkNetDbContext db)
{
    /// <summary>Generate a fresh <c>roomInstanceId</c> + <c>photonRoomId</c>
    /// pair for a private match and persist the row. Caller is responsible
    /// for plumbing the result onto the outgoing <see cref="RoomInstanceDto"/>.</summary>
    public async Task<PrivateInstance> RegisterAsync(
        long roomId, long subRoomId, long ownerPlayerId,
        string baseName, string location, string dataBlob, string photonRegion, int maxCapacity)
    {
        // Random component so the same player can have multiple
        // concurrent private instances of the same room (e.g. one in
        // the lobby, one in a sub-room) — without it the photon room
        // ids would collide and the second instance would silently
        // matchmake into the first.
        var nonce = Random.Shared.Next(0x10000, 0xFFFFFF);
        // Pack roomId/subId/ownerId into the high bits of the instance
        // id so it's unique-by-construction across rooms+players. The
        // low 24 bits carry the nonce so multiple concurrent instances
        // by the same player still get distinct ids. Stays inside int63
        // for safe LitJson marshalling on the watch.
        var instanceId = (long)(((ulong)(uint)nonce & 0xFFFFFFul)
            ^ ((ulong)roomId << 24)
            ^ ((ulong)subRoomId << 40)
            ^ ((ulong)(uint)ownerPlayerId << 48));
        if (instanceId < 0) instanceId = -instanceId;
        var photonRoomId = $"^{baseName}_{roomId}_sub{subRoomId}_priv{ownerPlayerId}_{nonce:x6}";

        var entity = new PrivateInstanceEntity
        {
            Id = instanceId,
            RoomId = roomId,
            SubRoomId = subRoomId,
            OwnerPlayerId = ownerPlayerId,
            PhotonRoomId = photonRoomId,
            Location = location,
            DataBlob = dataBlob,
            PhotonRegion = photonRegion,
            MaxCapacity = maxCapacity,
            Name = baseName,
            CreatedAt = DateTime.UtcNow,
        };
        db.PrivateInstances.Add(entity);
        await db.SaveChangesAsync();
        return ToRecord(entity);
    }

    public async Task<PrivateInstance?> GetAsync(long instanceId)
    {
        var e = await db.PrivateInstances.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == instanceId);
        return e is null ? null : ToRecord(e);
    }

    /// <summary>
    /// Idempotent registration of a player's dorm as a private instance.
    /// Dorms are always owner-only by design — anyone reaching the
    /// dorm's PhotonRoom name uninvited would otherwise be able to
    /// drop in. We mark every dorm <c>IsPrivate=true</c> and back it
    /// with a deterministic-id PrivateInstance row so
    /// <see cref="CanJoinAsync"/> works the same way it does for
    /// player-created private matches: owner always allowed, everyone
    /// else needs an explicit invite.
    ///
    /// The id is derived from <c>(ownerPlayerId, roomId, subRoomId)</c>
    /// with a fixed high-bit marker so it never collides with the
    /// random-nonce ids produced by <see cref="RegisterAsync"/>. Reusing
    /// the same id across repeat <c>/goto/room/DormRoom</c> calls keeps
    /// the registry from accumulating one row per dorm join.
    /// </summary>
    public async Task<PrivateInstance> EnsureForDormAsync(
        long ownerPlayerId, long roomId, long subRoomId,
        string baseName, string location, string dataBlob,
        string photonRegion, int maxCapacity, string photonRoomId)
    {
        // Deterministic-and-distinct from RegisterAsync's nonce layout.
        // Marker 0xDD0000 in the low 24 bits separates dorms from
        // player-created private matches so the id spaces never alias.
        var instanceId = (long)(
            0xDD0000ul
            ^ ((ulong)(uint)roomId << 24)
            ^ ((ulong)(uint)subRoomId << 40)
            ^ ((ulong)(uint)ownerPlayerId << 48));
        if (instanceId < 0) instanceId = -instanceId;

        var existing = await db.PrivateInstances
            .FirstOrDefaultAsync(p => p.Id == instanceId);
        if (existing is not null)
        {
            // Keep PhotonRoomId / DataBlob / PhotonRegion current. The
            // OWNER's most recent /goto/room/DormRoom is the source of
            // truth for which Photon region their dorm lives in — if
            // we serve invitees a stale region (because Photon:CloudRegion
            // config changed since the row was first written), the
            // invitee's watch connects to a different Photon master and
            // joining "^dormroom_p{owner}" there creates a SEPARATE room
            // from where the owner is. Both watches report Photon
            // joinresult=Success but never see each other in-game —
            // exact symptom 1362428/1811750 hit: same server-side
            // instance id, two parallel Photon rooms.
            var changed = false;
            if (existing.PhotonRoomId != photonRoomId) { existing.PhotonRoomId = photonRoomId; changed = true; }
            if (existing.DataBlob != dataBlob)         { existing.DataBlob = dataBlob;         changed = true; }
            if (existing.Location != location)         { existing.Location = location;         changed = true; }
            if (existing.PhotonRegion != photonRegion) { existing.PhotonRegion = photonRegion; changed = true; }
            if (changed) await db.SaveChangesAsync();
            return ToRecord(existing);
        }

        var entity = new PrivateInstanceEntity
        {
            Id = instanceId,
            RoomId = roomId,
            SubRoomId = subRoomId,
            OwnerPlayerId = ownerPlayerId,
            PhotonRoomId = photonRoomId,
            Location = location,
            DataBlob = dataBlob,
            PhotonRegion = photonRegion,
            MaxCapacity = maxCapacity,
            Name = baseName,
            CreatedAt = DateTime.UtcNow,
        };
        db.PrivateInstances.Add(entity);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent insert from another replica raced us to the
            // same deterministic id — re-fetch the winning row.
            db.Entry(entity).State = EntityState.Detached;
            var raced = await db.PrivateInstances.AsNoTracking()
                .FirstAsync(p => p.Id == instanceId);
            return ToRecord(raced);
        }
        return ToRecord(entity);
    }

    public async Task RemoveAsync(long instanceId)
    {
        // Cascade-delete invitees first so we don't leave orphan rows
        // referencing a non-existent instance.
        await db.PrivateInstanceInvitees
            .Where(i => i.PrivateInstanceId == instanceId)
            .ExecuteDeleteAsync();
        await db.PrivateInstances
            .Where(p => p.Id == instanceId)
            .ExecuteDeleteAsync();
    }

    /// <summary>Add a player to the invite list for a private instance.
    /// Idempotent on (instance, player) — repeat invites refresh
    /// <c>LatestInviteMessageId</c> so the most-recent invite's
    /// message id is always recoverable for /goto/invite resolution.
    /// Returns false if the instance isn't registered (probably
    /// expired / wrong id).</summary>
    public async Task<bool> InviteAsync(long instanceId, long inviteePlayerId, long? messageId = null)
    {
        if (!await db.PrivateInstances.AnyAsync(p => p.Id == instanceId)) return false;
        var existing = await db.PrivateInstanceInvitees
            .FirstOrDefaultAsync(i => i.PrivateInstanceId == instanceId && i.PlayerId == inviteePlayerId);
        if (existing is not null)
        {
            // Refresh the latest-invite pointer if this call carries
            // a new messageId — the watch may send several invites in
            // a row, and /goto/invite needs the freshest one.
            if (messageId is long m && existing.LatestInviteMessageId != m)
            {
                existing.LatestInviteMessageId = m;
                await db.SaveChangesAsync();
            }
            return true;
        }
        db.PrivateInstanceInvitees.Add(new PrivateInstanceInviteeEntity
        {
            PrivateInstanceId = instanceId,
            PlayerId = inviteePlayerId,
            InvitedAt = DateTime.UtcNow,
            LatestInviteMessageId = messageId,
        });
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Drop an invitee from a private instance's allow-list.
    /// Used when the invitee declines via
    /// <c>DELETE match.*/invite/{messageId}</c> — without this, an
    /// invitee who clicks "decline" then changes their mind could
    /// still join the instance without a fresh invite. No-op if the
    /// invite never existed.</summary>
    public async Task RevokeInviteAsync(long instanceId, long inviteePlayerId)
    {
        await db.PrivateInstanceInvitees
            .Where(i => i.PrivateInstanceId == instanceId && i.PlayerId == inviteePlayerId)
            .ExecuteDeleteAsync();
    }

    /// <summary>True when <paramref name="playerId"/> is the owner of
    /// the instance OR has been explicitly invited. The owner is
    /// implicitly allowed even without an Invite() call so they can
    /// rejoin their own private match after disconnects.</summary>
    public async Task<bool> CanJoinAsync(long instanceId, long playerId)
    {
        var ownerId = await db.PrivateInstances.AsNoTracking()
            .Where(p => p.Id == instanceId)
            .Select(p => (long?)p.OwnerPlayerId)
            .FirstOrDefaultAsync();
        if (ownerId is null) return false;
        if (ownerId == playerId) return true;
        return await db.PrivateInstanceInvitees.AsNoTracking()
            .AnyAsync(i => i.PrivateInstanceId == instanceId && i.PlayerId == playerId);
    }

    private static PrivateInstance ToRecord(PrivateInstanceEntity e) => new(
        InstanceId:    e.Id,
        RoomId:        e.RoomId,
        SubRoomId:     e.SubRoomId,
        OwnerPlayerId: e.OwnerPlayerId,
        PhotonRoomId:  e.PhotonRoomId,
        Location:      e.Location,
        DataBlob:      e.DataBlob,
        PhotonRegion:  e.PhotonRegion,
        MaxCapacity:   e.MaxCapacity,
        Name:          e.Name,
        CreatedAt:     DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc));

    public sealed record PrivateInstance(
        long InstanceId,
        long RoomId,
        long SubRoomId,
        long OwnerPlayerId,
        string PhotonRoomId,
        string Location,
        string DataBlob,
        string PhotonRegion,
        int MaxCapacity,
        string Name,
        DateTime CreatedAt);
}
