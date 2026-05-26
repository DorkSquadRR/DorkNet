using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Messages.V2;

/// <summary>
/// api.rec.net/api/messages/v2/* — direct-message inbox API. Backed
/// by <see cref="MessageEntity"/>. Sends fan out a SignalR push
/// (<c>MessageReceived</c>) so the recipient's watch refreshes
/// immediately.
/// </summary>
[ApiController]
[Route("api/[controller]/v2")]
[Authorize]
public class MessagesController(
    DorkNetDbContext db,
    NotificationService notifications,
    PrivateInstanceService privateInstances,
    OnlinePresenceService onlinePresence,
    ILogger<MessagesController> logger) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>List the caller's inbox, newest-first. Response is a
    /// flat array of <see cref="MessageDto"/>; the watch groups by
    /// sender locally. v1/v3 paths share the same handler — the watch
    /// uses whichever URL its <c>RecNet.Messages</c> static helper
    /// built from <c>GameConfig.MessageServiceVersion</c>.</summary>
    [HttpGet("get")]
    [HttpGet("/api/messages/v1/get")]
    [HttpGet("/api/messages/v3/get")]
    [HttpGet("/api/messages/v1/onlineStatus")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await db.Messages
            .Where(m => m.RecipientPlayerId == Me || m.SenderPlayerId == Me)
            // Defensive filter for the empty-body ghost rows that
            // accumulated before SendFormMessage rejected them. The
            // watch's MessagesScreenItem renders TextMessage (type 30)
            // as "Message : {body}", so an empty body becomes a
            // useless "Message :" entry the user has to scroll past.
            // Control-channel types (10 RequestGameInvite, 20
            // FriendStatusOnline, etc.) have their own labels and
            // legitimately carry no body, so we keep them.
            .Where(m => m.Type != 30 || (m.Body != null && m.Body != ""))
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync();
        return Ok(await ToDtosAsync(rows));
    }

    /// <summary>GET <c>api/messages/v1/unread</c> — list of unread
    /// messages addressed to the caller. The watch uses the count for
    /// the inbox badge and the entries themselves for the toast
    /// preview.</summary>
    [HttpGet("/api/messages/v1/unread")]
    public async Task<ActionResult<List<MessageDto>>> Unread()
    {
        var rows = await db.Messages
            .Where(m => m.RecipientPlayerId == Me && m.ReadAt == null)
            // Same ghost-row filter as GetMessages above — keep the
            // unread badge count honest by hiding empty TextMessages.
            .Where(m => m.Type != 30 || (m.Body != null && m.Body != ""))
            .OrderByDescending(m => m.SentAt)
            .Take(100)
            .ToListAsync();
        return Ok(await ToDtosAsync(rows));
    }

    public sealed class DeleteRequest
    {
        public long? Id { get; set; }
        public long? MessageId { get; set; }
        public List<long>? MessageIds { get; set; }
    }

    /// <summary>POST <c>api/messages/v3/delete</c> — hard-delete one or
    /// more messages addressed to the caller. The 2020 watch's inbox
    /// "delete" action posts JSON <c>{"MessageIds":[8]}</c> (plural
    /// array, verified from the live trace where the body was
    /// <c>{"MessageIds":[8]}</c> against <c>/api/messages/v3/delete</c>);
    /// some older / SPA call sites still post <c>id</c> or
    /// <c>messageId</c> as a form/query. Accept all three shapes and
    /// scope every delete to <c>RecipientPlayerId == Me</c> so the
    /// sender can't unsend.</summary>
    [HttpPost("/api/messages/v3/delete")]
    [HttpPost("/api/messages/v1/delete")]
    [Consumes("application/json")]
    public async Task<IActionResult> DeleteJson([FromBody] DeleteRequest body)
    {
        var ids = new List<long>();
        if (body.Id is long a && a > 0) ids.Add(a);
        if (body.MessageId is long b && b > 0) ids.Add(b);
        if (body.MessageIds is { Count: > 0 })
            ids.AddRange(body.MessageIds.Where(v => v > 0));
        return await DeleteByIds(ids);
    }

    [HttpPost("/api/messages/v3/delete")]
    [HttpPost("/api/messages/v1/delete")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> DeleteForm(
        [FromForm(Name = "id")] long? formId,
        [FromForm(Name = "messageId")] long? formMessageId,
        [FromForm(Name = "messageIds")] string? messageIdsCsv)
    {
        var ids = new List<long>();
        if (formId is long a && a > 0) ids.Add(a);
        if (formMessageId is long b && b > 0) ids.Add(b);
        if (long.TryParse(Request.Query["id"], out var qi) && qi > 0) ids.Add(qi);
        if (!string.IsNullOrWhiteSpace(messageIdsCsv))
        {
            foreach (var s in messageIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (long.TryParse(s.Trim(), out var v) && v > 0) ids.Add(v);
        }
        return await DeleteByIds(ids);
    }

    private async Task<IActionResult> DeleteByIds(List<long> ids)
    {
        ids = ids.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "missing_id" });
        var deleted = await db.Messages
            .Where(m => ids.Contains(m.Id) && m.RecipientPlayerId == Me)
            .ExecuteDeleteAsync();
        return Ok(new { success = true, error = "", deleted });
    }

    /// <summary>POST <c>api/messages/v3/markRead</c> — bulk
    /// mark-as-read for every unread message addressed to the caller.
    /// Optional <c>ids</c> CSV scopes to specific message ids.</summary>
    [HttpPost("/api/messages/v3/markRead")]
    [HttpPost("/api/messages/v1/markRead")]
    public async Task<IActionResult> MarkReadBulk([FromForm(Name = "ids")] string? ids)
    {
        IQueryable<MessageEntity> q = db.Messages
            .Where(m => m.RecipientPlayerId == Me && m.ReadAt == null);
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var idSet = ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : 0L)
                .Where(v => v > 0)
                .ToHashSet();
            if (idSet.Count > 0) q = q.Where(m => idSet.Contains(m.Id));
        }
        var now = DateTime.UtcNow;
        await q.ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now));
        return Ok(new { success = true, error = "" });
    }

    public sealed record SendRequest(long RecipientId, string Body, long? RoomId = null);

    /// <summary>Send a direct message — JSON wire shape used by some
    /// SPA/admin callers. The 2020 watch posts to the same path with
    /// form-urlencoded fields (<see cref="SendFormMessage"/>); the
    /// <c>[Consumes]</c> attribute disambiguates which action MVC
    /// picks. Empty bodies are rejected so the inbox doesn't get
    /// padded with whitespace pings; messages over 2000 chars are
    /// truncated to fit the column.</summary>
    [HttpPost("send")]
    [HttpPost("/api/messages/v1/send")]
    [Consumes("application/json")]
    public async Task<ActionResult<MessageDto>> SendMessage([FromBody] SendRequest req)
    {
        if (req.RecipientId <= 0 || req.RecipientId == Me)
            return BadRequest(new { error = "invalid_recipient" });
        var trimmed = (req.Body ?? string.Empty).Trim();
        if (trimmed.Length == 0) return BadRequest(new { error = "empty_body" });
        if (trimmed.Length > 2000) trimmed = trimmed[..2000];

        var entry = new MessageEntity
        {
            SenderPlayerId = Me,
            RecipientPlayerId = req.RecipientId,
            Body = trimmed,
            Type = 30, // TextMessage
            RoomId = req.RoomId,
        };
        db.Messages.Add(entry);
        await db.SaveChangesAsync();

        await BroadcastMessageAsync(entry);
        return Ok(ToDto(entry));
    }

    /// <summary>Form-urlencoded variant — what the 2020 watch actually
    /// posts when the user taps "Send Join Request" or any other
    /// <c>RecNet.Messages.SendMessage(player, type, data)</c> caller.
    /// Wire shape (verified against client log <c>contentType=
    /// application/x-www-form-urlencoded</c>, body <c>ToPlayerId=…&amp;
    /// Type=10&amp;Data=</c>): the <c>Type</c> integer is a
    /// <c>RecNet.Message.MessageType</c> enum value
    /// (<c>Cpp2IL_CS/.../RecNet/Message.cs</c>) — 10 =
    /// RequestGameInvite, 30 = TextMessage, etc. Empty <c>Data</c> is
    /// allowed for control-channel types (join request, declines)
    /// where the message itself is the signal.</summary>
    [HttpPost("send")]
    [HttpPost("/api/messages/v1/send")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<MessageDto>> SendFormMessage(
        [FromForm(Name = "ToPlayerId")] long toPlayerId,
        [FromForm(Name = "Type")] int type,
        [FromForm(Name = "Data")] string? data,
        [FromForm(Name = "RoomId")] long? roomId)
    {
        if (toPlayerId <= 0 || toPlayerId == Me)
            return BadRequest(new { error = "invalid_recipient" });
        var trimmed = (data ?? string.Empty);
        if (trimmed.Length > 2000) trimmed = trimmed[..2000];

        // For TextMessage (type 30) the body IS the message — empty
        // is meaningless and renders as a ghost "Message :" row in
        // the watch's inbox (MessagesScreenItem format string at
        // Cpp2IL_ISIL/.../MessagesScreenItem.txt:2145 is
        // "Message : {0}"). Control-channel types (RequestGameInvite,
        // declines, FriendStatusOnline, etc.) intentionally carry
        // empty Data — they get rendered by their own label string
        // in the same screen item.
        if (type == 30 && trimmed.Trim().Length == 0)
            return BadRequest(new { error = "empty_text_message" });

        var entry = new MessageEntity
        {
            SenderPlayerId = Me,
            RecipientPlayerId = toPlayerId,
            Body = trimmed,
            Type = type,
            RoomId = roomId,
        };
        db.Messages.Add(entry);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[messages] send from={From} to={To} type={Type} dataLen={Len}",
            Me, toPlayerId, type, trimmed.Length);

        await BroadcastMessageAsync(entry);
        return Ok(ToDto(entry));
    }

    /// <summary>POST <c>invite</c> — handles the watch's
    /// <c>RecNet.Messages.SendGameInvite(playerId, roomInstanceId)</c>
    /// (verified at <c>Cpp2IL_ISIL/.../Messages.txt:613-773</c>): form
    /// fields <c>playerId</c> (recipient) + <c>roomInstanceId</c>
    /// (the inviter's private match id). Two side-effects:
    ///   1. Add the recipient to the invite list for that instance so
    ///      <c>POST /goto/instance/{id}</c> accepts them.
    ///   2. Drop a Type-6 (GameInviteV2) message in their inbox carrying the
    ///      RoomInstanceId so the message UI's "Join" button knows
    ///      where to send them.
    /// Registered at multiple paths because the exact URL the watch
    /// builds for "invite" depends on the messages-namespace base —
    /// rather than guess wrong and silently fall through to the
    /// wildcard, we cover all plausible mounts and log the one that
    /// actually fires.</summary>
    [HttpPost("/invite")]
    [HttpPost("/api/invite")]
    [HttpPost("/api/messages/v1/invite")]
    [HttpPost("/api/messages/v2/invite")]
    [HttpPost("/api/messages/v3/invite")]
    public async Task<ActionResult> SendInvite(
        [FromForm(Name = "playerId")] long playerId,
        [FromForm(Name = "roomInstanceId")] long roomInstanceId)
    {
        logger.LogInformation(
            "[invite] from={From} → to={To} instance={Instance} (path={Path})",
            Me, playerId, roomInstanceId, Request.Path);

        if (playerId <= 0 || playerId == Me)
            return BadRequest(new { error = "invalid_recipient" });

        // Only the OWNER of the private instance can invite into it.
        // Otherwise an attacker who somehow learned a roomInstanceId
        // could invite themselves (or anyone) freely.
        var inst = await privateInstances.GetAsync(roomInstanceId);
        if (inst is null)
            return BadRequest(new { error = "instance_not_found", roomInstanceId });
        if (inst.OwnerPlayerId != Me)
            return Forbid();

        // Persist a real RecNet.Message.MessageType.GameInviteV2 (=6)
        // so the watch's GameInviteController calls
        // Matchmaking.AcceptRoomInvite(inviteId, roomId), which posts
        // goto/invite/{inviteId}. Type 0 follows the sender's current
        // presence instead and can land in the wrong instance.
        //
        // Two saves: the first allocates the message Id (which becomes
        // the `inviteId` the watch sends back in /goto/invite/{N}); the
        // second backfills the JSON Body once we know it. We can't
        // pre-allocate the Id because EF Core's identity columns only
        // populate on SaveChanges.
        var roomName = await db.Rooms
            .Where(r => r.Id == inst.RoomId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync() ?? "Private match";

        var entry = new MessageEntity
        {
            SenderPlayerId = Me,
            RecipientPlayerId = playerId,
            // Placeholder so the row passes the NOT NULL constraint
            // until we overwrite it post-save with the real JSON.
            Body = string.Empty,
            Type = 6,
            RoomId = inst.RoomId,
        };
        db.Messages.Add(entry);
        await db.SaveChangesAsync();

        // InviteAsync AFTER the message save so we can pin the freshly-
        // allocated Message.Id onto the invitee row's
        // LatestInviteMessageId. This is what /goto/invite/{N} falls
        // back to when the watch's accept flow has already raced
        // through DELETE /api/messages/v3/delete — the message row is
        // gone but the (instance, player) → messageId mapping survives.
        await privateInstances.InviteAsync(roomInstanceId, playerId, messageId: entry.Id);

        // Body shape MUST be the JSON the watch's Message.Deserialize
        // builds RoomInviteDetails from (verified at
        // Cpp2IL_ISIL/.../RecNet/Message.txt:786-805 — for Type 6/7
        // the parser hits `Json.Deserialize` and reads "inviteId" +
        // "name"; for other types it `Int32.Parse`s the body).
        //
        //   inviteId       → the message Id; watch echoes it back in
        //                    /goto/invite/{inviteId}, server looks the
        //                    row up to resolve roomInstanceId.
        //   name           → display string in the invite toast / row.
        //   roomInstanceId → extra field the watch ignores; server uses
        //                    it (via TryParseRoomInstanceId's JSON
        //                    fallback) so /goto/invite/{N} doesn't have
        //                    to re-query the private-instance service.
        //
        // The roomInstanceId MUST be a JSON STRING, not a number. LitJson
        // chokes on the dict-cast when a value is a 64-bit integer larger
        // than int32 — confirmed via SaveDebugMod trace: the watch logs
        //   `data={"inviteId":31,"name":"Dorm_1811750","roomInstanceId":6546545006375796736}`
        //   `RoomInviteDetails=<null>`
        //   `System.NullReferenceException at RecNet.Util.GetKey from
        //    RecNet.Message.Deserialize`
        // Json.Deserialize returns a null cast-result on the long value,
        // GetKey("inviteId") then NREs and the whole parse aborts before
        // RoomInviteDetails is built. Stringifying the value sidesteps
        // LitJson's long-int handling entirely; the watch only reads
        // inviteId + name so the string form is invisible to it.
        entry.Body = System.Text.Json.JsonSerializer.Serialize(new
        {
            inviteId = entry.Id,
            name = roomName,
            roomInstanceId = roomInstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync();

        await BroadcastMessageAsync(entry);
        return Ok(new { success = true, messageId = entry.Id });
    }

    /// <summary>Mark a single message as read. No-op if not
    /// addressed to the caller — prevents the sender from "reading"
    /// the recipient's copy.</summary>
    [HttpPost("markread/{id:long}")]
    public async Task<ActionResult> MarkRead(long id)
    {
        var msg = await db.Messages.FirstOrDefaultAsync(m =>
            m.Id == id && m.RecipientPlayerId == Me);
        if (msg is null) return NotFound();
        if (msg.ReadAt is null)
        {
            msg.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return Ok();
    }

    public sealed class SendMultipleBody
    {
        public List<long>? ToPlayerIds { get; set; }
        public int Type { get; set; }
        public string? Data { get; set; }
        public long? RoomId { get; set; }
    }

    /// <summary>POST <c>api/messages/v1/sendMultiple</c> — fan-out a
    /// message to many recipients. Persists one MessageEntity per
    /// recipient. Wire shape: <c>{ToPlayerIds (int[]), Type (int), Data (string)}</c>.</summary>
    [HttpPost("/api/messages/v1/sendMultiple")]
    [Consumes("application/json")]
    public async Task<IActionResult> SendMultiple([FromBody] SendMultipleBody body)
    {
        if (body.ToPlayerIds is null || body.ToPlayerIds.Count == 0)
            return Ok(Array.Empty<object>());
        var entries = new List<MessageEntity>();
        foreach (var to in body.ToPlayerIds.Distinct().Where(id => id > 0 && id != Me))
        {
            var entry = new MessageEntity
            {
                SenderPlayerId    = Me,
                RecipientPlayerId = to,
                Body              = body.Data ?? string.Empty,
                Type              = body.Type,
                RoomId            = body.RoomId,
            };
            entries.Add(entry);
            db.Messages.Add(entry);
        }
        await db.SaveChangesAsync();
        foreach (var entry in entries)
        {
            await BroadcastMessageAsync(entry);
        }
        return Ok(Array.Empty<object>());
    }

    /// <summary>POST <c>api/offlineinvite/v1/send</c> — send an invite
    /// message to a player who's offline; surfaces in their inbox on
    /// next login. Wire shape: form-urlencoded <c>RecipientId</c> +
    /// <c>Data</c>. Idempotent per (sender, recipient, body).</summary>
    [HttpPost("/api/offlineinvite/v1/send")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> OfflineInviteSend(
        [FromForm(Name = "RecipientId")] long? recipientId,
        [FromForm(Name = "Data")] string? data)
    {
        if (recipientId is not long rid || rid <= 0 || rid == Me)
            return Ok(new { success = false, error = "invalid_recipient" });
        var entry = new MessageEntity
        {
            SenderPlayerId = Me,
            RecipientPlayerId = rid,
            Body = data ?? string.Empty,
            Type = 0,
        };
        db.Messages.Add(entry);
        await db.SaveChangesAsync();
        await BroadcastMessageAsync(entry);
        return Ok(new { success = true, messageId = entry.Id });
    }

    /// <summary>GET <c>api/offlineinvite/v1/all</c> — invites
    /// received while the player was offline. Pulls unread
    /// MessageEntity rows whose body looks like an invite.</summary>
    [HttpGet("/api/offlineinvite/v1/all")]
    public async Task<IActionResult> OfflineInviteAll()
    {
        var rows = await db.Messages
            .Where(m => m.RecipientPlayerId == Me && m.ReadAt == null
                && (m.Body.Contains("invite") || m.Body.Contains("event")))
            .OrderByDescending(m => m.SentAt)
            .Take(50)
            .Select(m => new
            {
                InviteId = m.Id,
                FromPlayerId = (int)m.SenderPlayerId,
                m.Body,
                SentAt = m.SentAt,
            })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>The boot sequence asks for online friends. We
    /// derive that from the SignalR hub's connected-player set —
    /// the response shape is <c>[ {AccountId, Online}, ... ]</c>.</summary>
    [HttpGet("/api/messages/v1/favoriteFriendOnlineStatus")]
    public async Task<ActionResult> FavoriteFriendOnlineStatus()
    {
        var friendIds = await db.Relationships
            .Where(r => r.Status == RelationshipStatus.Friend &&
                        (r.RequesterId == Me || r.TargetId == Me))
            .Select(r => r.RequesterId == Me ? r.TargetId : r.RequesterId)
            .ToListAsync();
        var online = onlinePresence.OnlinePlayerIds().ToHashSet();
        return Ok(friendIds.Select(id => new
        {
            AccountId = id,
            Online = online.Contains(id),
        }));
    }

    /// <summary>Wire shape verified against
    /// <c>Cpp2IL_ISIL/.../RecNet/Message.txt:591-787</c>.
    /// <c>RecNet.Message.Deserialize</c> calls <c>Util.GetKey</c>
    /// (throws on miss) for: <c>Id</c> (long), <c>FromPlayerId</c> (int),
    /// <c>SentTime</c> (DateTime), <c>Type</c> (int — MessageType enum),
    /// and reads <c>Data</c> (string) via <c>Util.GetKeyOrDefault</c>.
    /// Optional nullable wrappers: <c>RoomId</c>, <c>PlayerEventId</c>.
    /// Previous response used <c>{Id, Sender, Recipient, Body, SentAt, ReadAt}</c>
    /// which crashed at <c>Util.GetKey("FromPlayerId")</c>.</summary>
    public sealed record MessageDto(
        long Id,
        long FromPlayerId,
        DateTime SentTime,
        int Type,
        string Data,
        long? RoomId,
        long? PlayerEventId);

    private async Task<List<MessageDto>> ToDtosAsync(List<MessageEntity> rows)
    {
        var instanceIds = rows
            .Where(m => IsInstanceInviteType(m.Type)
                && m.RoomId is null
                && long.TryParse(m.Body, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(m => long.Parse(m.Body, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

        var roomByInstance = instanceIds.Count == 0
            ? new Dictionary<long, long>()
            : await db.PrivateInstances
                .Where(i => instanceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.RoomId })
                .ToDictionaryAsync(i => i.Id, i => i.RoomId);

        return rows.Select(m =>
        {
            long? roomId = m.RoomId;
            if (roomId is null
                && IsInstanceInviteType(m.Type)
                && long.TryParse(m.Body, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var instanceId)
                && roomByInstance.TryGetValue(instanceId, out var rid))
            {
                roomId = rid;
            }
            return ToDto(m, roomId);
        }).ToList();
    }

    private static bool IsInstanceInviteType(int type) => type is 6 or 7;

    /// <summary>Push a <c>MessageReceived</c> SignalR notification to BOTH
    /// the recipient AND the sender. The recipient half is the obvious one
    /// — that's what fires the watch's <c>OnMessageAdded</c> event and
    /// gives them the toast / inbox-row update. The sender-half is less
    /// obvious but equally needed: the watch's <c>Messages.SendMessage</c>
    /// (Messages.txt:1790-1900) just does <c>ExpectHttpStatusSuccess</c>
    /// on the response and never touches the local <c>MessageList</c>,
    /// so without a self-targeted push the sender's chat thread doesn't
    /// know about its own outgoing message until the next
    /// <c>RefreshList</c> (which only fires on hub-reconnect or first
    /// inbox open). Pushing to both sides keeps both watches in sync
    /// in real time. Dedup happens client-side in
    /// <c>OnMessageReceived</c> via the "already-in-MessageList by Id"
    /// check (Messages.txt:2790-2793), so a player who somehow ends up
    /// as both sender and recipient (admin self-messaging) only sees
    /// the message once.</summary>
    private async Task BroadcastMessageAsync(MessageEntity entry)
    {
        var dto = ToDto(entry);
        await notifications.NotifyAsync(entry.RecipientPlayerId,
            PushNotificationId.MessageReceived, dto);
        if (entry.SenderPlayerId != entry.RecipientPlayerId)
        {
            await notifications.NotifyAsync(entry.SenderPlayerId,
                PushNotificationId.MessageReceived, dto);
        }
    }

    private static MessageDto ToDto(MessageEntity m, long? roomId = null) =>
        new(
            Id:            m.Id,
            FromPlayerId:  m.SenderPlayerId,
            SentTime:      m.SentAt,
            // Round-trip the stored Type verbatim. Earlier we coerced
            // Type==0 → 30 for "pre-Type-column rows", but the
            // Messages.Type Postgres patch (Program.cs:612-614) added
            // the column with DEFAULT 30 and backfilled every legacy
            // row to 30, so any Type==0 row in the DB now is a real
            // GameInvite (e.g. SendInvite above). Coercing it to 30
            // routes it through TextMessageController and renders
            // "Message : <instanceId>" with no Accept button.
            Type:          m.Type,
            Data:          m.Body,
            RoomId:        roomId ?? m.RoomId,
            PlayerEventId: null);
}
