using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
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
    PlayerPresenceService presence) : ControllerBase
{
    [HttpGet("/player")]
    public ActionResult<List<PlayerPresenceDto>> GetPresences()
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
        var callerRoom = callerId is long cid ? presence.GetRoom(cid) : null;
        var result = ids.Select(id =>
        {
            var room = presence.GetRoom(id);
            // Other players need both a room presence and an active
            // live signal. Use the recent-heartbeat marker as the
            // truth source: SignalR can drop while the player is still
            // heartbeating, or linger after the game is gone. Presence
            // keys intentionally live for minutes to survive long room
            // loads, so using them alone leaves friends stuck online
            // after a graceful disconnect.
            var isSelf = callerId == id;
            var active = isSelf || presence.IsRecentlyActive(id);
            var isOnline = room is not null && active;
            return new PlayerPresenceDto
            {
                PlayerId         = (int)id,
                // StatusVisibility is the player's *preference* (would
                // they like to appear online if they were online?). For
                // offline players we still report Visible so when they
                // come back the friend's cached preference is correct,
                // and IsOnline=false is what gates the "Online" badge.
                StatusVisibility = 1,           // Visible
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

    /// <summary>PlayerStatusVisibility enum (int):
    /// 0 = Hidden / Offline-appearing, 1 = Visible, 2 = FriendsOnly.</summary>
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
    public int AppVersion { get; set; } = 20201210;
}
