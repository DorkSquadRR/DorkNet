using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Match;

/// <summary>
/// match.rec.net/player — bulk player-presence query used by
/// <c>RecNet.Matchmaking.RefreshCachedPlayerPresences</c>.
///
/// URL pattern verified against
/// Cpp2IL_ISIL/IsilDump/Assembly-CSharp/RecNet/Matchmaking.txt
/// lines 3565-3568:
///   <c>String.Join("&amp;", ids)</c> → <c>String.Concat("player?", joined)</c>.
/// So the request URL is literally <c>player?{id1}&amp;{id2}&amp;{id3}...</c>
/// — every query-string KEY is one account id, with no value. The previous
/// <c>[FromQuery(Name="id")] int[] ids</c> binding only matched
/// <c>?id=X&amp;id=Y</c> and never picked up any of the watch's bare-id
/// requests, which is why every other player rendered as offline: with no
/// ids parsed, the response was <c>[]</c> and IsOnline (which checks
/// <c>roomInstance != null</c>) defaulted to false.
///
/// Response shape: <c>List&lt;PlayerPresence&gt;</c> — keys verified by
/// disassembling <c>RecNet.PlayerPresence.Deserialize</c>:
///     <c>"playerId"</c>          (int, required)
///     <c>"statusVisibility"</c>  (int, required)
///     <c>"deviceClass"</c>       (int, required)
///     <c>"vrMovementMode"</c>    (only read when deviceClass == 1)
///     <c>"roomInstance"</c>      (object, optional)
///
/// <c>PlayerPresence.IsOnline</c> returns true iff
/// <c>roomInstance</c> is non-null — so we surface the live RoomInstance
/// from <see cref="PlayerPresenceService"/> rather than always sending
/// null. Players who haven't /goto'd anywhere still report offline.
/// </summary>
[ApiController]
public class PlayerPresenceController(
    PlayerPresenceService presence,
    PresenceBroadcastService presenceFanout,
    JoinTimeoutService joinTimeouts,
    DorkNetDbContext db,
    ServerSettingsService settings) : ControllerBase
{
    /// <summary>PlayerSettings key written by <c>PUT /player/statusvisibility</c>
    /// (MatchPlayerController.PrefStatusVisibility — same literal; the
    /// round-trip is pinned by a regression test).</summary>
    public const string PrefStatusVisibility = PresenceBroadcastService.StatusVisibilityPrefKey;

    [HttpGet("/player")]
    public async Task<ActionResult<List<PlayerPresenceDto>>> GetPresences()
    {
        // The watch sends two URL flavours for this endpoint:
        //   1. Bulk: <c>/player?{id1}&amp;{id2}&amp;{id3}</c> — bare keys,
        //      from RefreshCachedPlayerPresences (Matchmaking.txt:3565).
        //   2. Single: <c>/player?id={N}</c> — observed in the live trace
        //      from a different code path (probably the per-player presence
        //      probe). Both must work; an empty response makes
        //      PUNNetworkManager skip Photon region setup, which then
        //      crashes <c>PingPhotonRegionsInternal</c> with
        //      "An item with the same key has already been added. Key: none"
        //      and leaves the player on a black screen post-dorm-load.
        //
        // We accept ANY query parameter whose value parses as a long, plus
        // any bare key that does. Same dedup → same DB lookup → same
        // response shape regardless.
        var ids = new HashSet<long>();
        foreach (var (key, values) in Request.Query)
        {
            if (long.TryParse(key, out var fromKey) && fromKey > 0)
                ids.Add(fromKey);
            foreach (var v in values)
            {
                if (long.TryParse(v, out var fromValue) && fromValue > 0)
                    ids.Add(fromValue);
            }
        }

        // Caller's own id gets the FULL RoomInstance (photonRoomId +
        // dataBlob) so the watch's cached PlayerPresence for itself
        // matches what /player/heartbeat returns. The watch calls
        // /player/?id=<self>&id=<friend>&... at boot via
        // RefreshCachedPlayerPresences and then caches each entry; if
        // self's entry is redacted (photonRoomId="" / dataBlob="") but
        // heartbeat later returns the full values, RoomInstance.
        // RoomInstancesAreDifferent fires → "presence heartbeat
        // response indicates local presence is out-of-sync!" →
        // OnPresenceHeartbeatResponse tears the player out of the
        // current room and re-joins, killing any in-flight save flow
        // mid-coroutine. Only redact for OTHER players where the
        // privacy concern actually applies.
        var callerId = this.CurrentPlayerId();
        var callerRoom = callerId is long cid ? SelfRoom(cid) : null;
        var visibility = await LoadVisibilityAsync(ids);
        var hiddenFromCaller = await ResolveHiddenAsync(callerId, ids, visibility);
        var result = ids.Select(id =>
        {
            var isSelf = callerId == id;
            // The caller's own row must say exactly what their heartbeat
            // says (pending-join target, or null in the offline room): the
            // client compares its cached self presence field by field
            // (HCHDEHIGEBE.DCIJHIPJHOB) and treats any difference as
            // out-of-sync.
            var room = isSelf ? callerRoom : presence.GetRoom(id);
            // Online = a live signal (recent heartbeat, or a SignalR
            // connection), NOT "has a room". Presence keys live for minutes
            // to survive long room loads, so a room alone would leave friends
            // stuck online after the game is gone; and a player in the offline
            // room after matchmake/none has no room but is very much online
            // (the 2023 DTO carries isOnline on its own; see
            // NotificationService.BuildPresenceUpdatePayload).
            // A player who chose "appear offline" (or friends/favorites
            // only, when the caller doesn't qualify) is reported exactly
            // like a genuinely offline player: no room, isOnline=false.
            var isOnline = (isSelf || presenceFanout.IsOnline(id)) && !hiddenFromCaller.Contains(id);
            if (!isOnline) room = null;
            return new PlayerPresenceDto
            {
                PlayerId         = (int)id,
                // The player's stored online-status visibility. Self MUST
                // see its own stored value: the profile dialog's current
                // selection is read from the cached self presence, and a
                // hard-coded 0 here snapped it back to Public after every
                // presence refresh.
                StatusVisibility = visibility.GetValueOrDefault(id, PresenceVisibility.Public),
                DeviceClass      = 0,           // Desktop / unknown — not VR
                VrMovementMode   = 0,
                // Full instance details are safe, and necessary, when the
                // queried player is already co-present with the caller.
                // PUN asks /player?{remotePlayerId} after a Photon actor
                // joins; returning a redacted same-room instance there
                // leaves the 2020 client with an incomplete roomInstance
                // and can null-ref while reconciling player presence.
                RoomInstance     = isOnline && ShouldExposeFullRoom(callerId, callerRoom, id, room)
                    ? room
                    : RoomInstanceDto.Redact(isOnline ? room : null),
                IsOnline         = isOnline,
            };
        }).ToList();

        return Ok(result);
    }

    /// <summary>The caller's own room as their heartbeat reports it.</summary>
    private RoomInstanceDto? SelfRoom(long playerId)
    {
        if (joinTimeouts.GetPending(playerId) is { DeferPresenceCommit: true } pending)
            return pending.TargetRoom;
        if (presence.IsInNoRoom(playerId)) return null;
        return presence.GetRoom(playerId);
    }

    private Task<Dictionary<long, int>> LoadVisibilityAsync(HashSet<long> ids) =>
        PresenceBroadcastService.GetStatusVisibilitiesAsync(db, ids);

    /// <summary>Ids whose online status the caller may not see. The caller
    /// always sees itself; anonymous callers only see Public players.</summary>
    private async Task<HashSet<long>> ResolveHiddenAsync(
        long? callerId, HashSet<long> ids, Dictionary<long, int> visibility)
    {
        var hidden = new HashSet<long>();
        var restricted = visibility
            .Where(kv => kv.Value != PresenceVisibility.Public && kv.Key != callerId)
            .ToList();
        if (restricted.Count == 0) return hidden;

        if (callerId is not long me)
        {
            foreach (var kv in restricted) hidden.Add(kv.Key);
            return hidden;
        }

        HashSet<long>? friends = null;
        HashSet<long>? favoritedMe = null;
        foreach (var (target, vis) in restricted)
        {
            if (vis == PresenceVisibility.Offline) { hidden.Add(target); continue; }

            friends ??= (await RelationshipQueries.EffectiveFriendIdsAsync(db, settings, me)).ToHashSet();
            if (!friends.Contains(target)) { hidden.Add(target); continue; }

            if (vis == PresenceVisibility.FavoriteFriendsOnly)
            {
                // Favorited lives on the favoriter's own row
                // (RequesterId = favoriter), as RelationshipsController.BuildMerged reads it.
                favoritedMe ??= (await db.Relationships
                    .Where(r => r.TargetId == me && r.Favorited && ids.Contains(r.RequesterId))
                    .Select(r => r.RequesterId)
                    .ToListAsync()).ToHashSet();
                if (!favoritedMe.Contains(target)) hidden.Add(target);
            }
        }
        return hidden;
    }

    private static bool ShouldExposeFullRoom(
        long? callerId,
        RoomInstanceDto? callerRoom,
        long targetId,
        RoomInstanceDto? targetRoom)
    {
        if (targetRoom is null) return false;
        if (targetId == callerId) return true;
        if (callerRoom is null) return false;

        if (callerRoom.RoomInstanceId != 0
            && callerRoom.RoomInstanceId == targetRoom.RoomInstanceId)
        {
            return true;
        }

        return !string.IsNullOrEmpty(callerRoom.PhotonRoomId)
            && string.Equals(callerRoom.PhotonRoomId, targetRoom.PhotonRoomId,
                StringComparison.Ordinal);
    }
}

public class PlayerPresenceDto
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    /// <summary>Online-status visibility enum (int, 2023 client):
    /// 0 = Public, 1 = FriendsOnly, 2 = FavoriteFriendsOnly, 3 = Offline.</summary>
    [JsonPropertyName("statusVisibility")]
    public int StatusVisibility { get; set; }

    /// <summary>RecNet.DeviceClass enum (int).</summary>
    [JsonPropertyName("deviceClass")]
    public int DeviceClass { get; set; }

    /// <summary>VRMovementMode enum (int) — only consulted when
    /// deviceClass == 1.</summary>
    [JsonPropertyName("vrMovementMode")]
    public int VrMovementMode { get; set; }

    /// <summary>Read by Util.GetObjectKey&lt;RoomInstance&gt; — must
    /// be a nested object (or null), NOT a string. Empty string ""
    /// triggers
    /// <c>InvalidCastException: Unable to cast object of type 'String'
    /// to type 'Dictionary'2'</c>. <c>null</c> means "player not in
    /// any room" → IsOnline = false.</summary>
    [JsonPropertyName("roomInstance")]
    public object? RoomInstance { get; set; } = null;

    // 2020.12 PlayerPresence.Deserialize (EFAJBHGDIDD.PPGFHEDFBEA) reads
    // these as required keys after roomInstance. Missing → KeyNotFoundException
    // → "RecNet presence heartbeat failed: Malformed Response" spam.
    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; } = true;

    [JsonPropertyName("appVersion")]
    // 2023.03.21 client build version ("20230317"); must match the joining
    // client's own version or it reports a version mismatch. Stale 2020 value
    // (20201210) carried over from the December branch.
    public string AppVersion { get; set; } = "20230317";
}
