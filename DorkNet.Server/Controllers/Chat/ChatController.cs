using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using System.Text.Json;

namespace DorkNet.Server.Controllers.Chat;

/// <summary>
/// chat.rec.net — DM / group-chat endpoints. Three entities back this:
/// <list type="bullet">
/// <item><see cref="ChatMessageEntity"/> — append-only message log
///       keyed by thread.</item>
/// <item><see cref="ChatThreadEntity"/> — optional per-thread metadata
///       (custom name, creator) for group chats and renamed DMs.</item>
/// <item><see cref="ChatThreadMemberEntity"/> — per-(thread, player)
///       row carrying snooze timestamp and last-read message pointer.
///       For unmodified 2-person DMs we create rows lazily on first
///       mutation; the message table is the source of truth for
///       participants.</item>
/// </list>
/// </summary>
[ApiController]
[Authorize]
public class ChatController(
    DorkNetDbContext db,
    NotificationService notifications) : ControllerBase
{
    // MGGNHLHPHOF on the 2023 wire: Success=0, InvalidArguments=1,
    // ThreadNotFound=2, MembershipNotFound=3, PlayerAlreadyOnThread=4,
    // CannotMessagePlayer=5, InvalidCharacters=6, RecentlyLeftThread=7,
    // ThreadTooLarge=8.
    private const int ChatSuccess = 0;
    private const int ChatInvalidArguments = 1;
    private const int ChatThreadNotFound = 2;
    private const int ChatMembershipNotFound = 3;
    private const int ChatPlayerAlreadyOnThread = 4;

    /// <summary>Bit markers on <c>ClubMembershipEntity.Permissions</c>:
    /// 128 = pending invite/request, 256 = banned. Both mean "not on the
    /// roster" — same test <c>ClubService.MembershipTypeFromPerms</c>
    /// applies.</summary>
    private const int ClubPendingFlag = 128;
    private const int ClubBannedFlag = 256;

    private long Me => this.RequireCurrentPlayerId();

    /// <summary>List the caller's chat threads, most-recently-active
    /// first. Each thread carries the most recent message preview so
    /// the watch can render the inbox without per-thread fetches.</summary>
    [HttpGet("/thread")]
    public async Task<IActionResult> GetThreads([FromQuery] int? maxCount)
    {
        var take = Math.Clamp(maxCount ?? 50, 1, 100);

        // Two sources of "threads I see in my inbox":
        //   1. Explicit membership rows (group chats — leaving deletes
        //      the row, so its presence means "still in").
        //   2. DM threads where I sent or received at least one message.
        // We union both, then filter out group keys I'm NOT a member
        // of (in case I sent a message to a group then left).
        var memberKeys = (await db.ChatThreadMembers
            .Where(m => m.PlayerId == Me)
            .Select(m => m.ThreadKey)
            .ToListAsync()).ToHashSet();

        var sentOrReceived = await db.ChatMessages
            .Where(c => c.SenderPlayerId == Me ||
                        c.ThreadKey.StartsWith($"dm:{Me}:") ||
                        c.ThreadKey.EndsWith($":{Me}"))
            .Select(c => c.ThreadKey)
            .Distinct()
            .ToListAsync();

        //   3. Club chats for every club I'm actually on the roster of —
        //      club threads carry no ChatThreadMembers rows (the roster
        //      IS the membership), so without this the "Club chats"
        //      filter on the thread list is always empty. Only clubs
        //      whose channel has been used are listed, so browsing the
        //      inbox never materialises empty threads.
        var myClubKeys = (await db.ClubMemberships
            .Where(m => m.PlayerId == Me &&
                        (m.Permissions & ClubPendingFlag) == 0 &&
                        (m.Permissions & ClubBannedFlag) == 0)
            .Select(m => m.ClubId)
            .ToListAsync())
            .Select(ClubKey)
            .ToList();
        var activeClubKeys = myClubKeys.Count == 0
            ? new List<string>()
            : await db.ChatMessages
                .Where(c => myClubKeys.Contains(c.ThreadKey))
                .Select(c => c.ThreadKey)
                .Distinct()
                .ToListAsync();

        var keys = sentOrReceived
            .Concat(memberKeys)
            .Concat(activeClubKeys)
            .Distinct()
            .Where(k => !k.StartsWith("group:") || memberKeys.Contains(k))
            .Where(k => ClubIdFromKey(k) == 0 || activeClubKeys.Contains(k))
            .ToList();
        if (keys.Count == 0) return Ok(Array.Empty<object>());

        var latest = await db.ChatMessages
            .Where(c => keys.Contains(c.ThreadKey))
            .GroupBy(c => c.ThreadKey)
            .Select(g => g.OrderByDescending(c => c.SentAt).First())
            .Take(take)
            .ToListAsync();

        var threadMeta = await EnsureThreadRowsAsync(keys);
        var myMembership = await db.ChatThreadMembers
            .Where(m => m.PlayerId == Me && keys.Contains(m.ThreadKey))
            .ToDictionaryAsync(m => m.ThreadKey, m => m);
        var participants = await LoadParticipantsAsync(keys);
        var favorites = await LoadFavoritesAsync(keys);

        return Ok(latest
            .OrderByDescending(c => c.SentAt)
            .Select(c =>
            {
                var meta = threadMeta[c.ThreadKey];
                myMembership.TryGetValue(c.ThreadKey, out var member);
                participants.TryGetValue(c.ThreadKey, out var playerIds);
                return ToWireThread(
                    meta,
                    member,
                    playerIds ?? new List<int>(),
                    latestMessage: c,
                    isFavorited: favorites.Contains(c.ThreadKey));
            })
            .ToList());
    }

    /// <summary>Return the single thread by its key, with the most
    /// recent N messages.</summary>
    [HttpGet("/thread/{chatThreadId}")]
    public async Task<IActionResult> GetThreadById(string chatThreadId,
        [FromQuery] int maxCount = 50)
    {
        var take = Math.Clamp(maxCount, 1, 200);
        var key = await ResolveThreadKeyAsync(chatThreadId);
        var rows = await db.ChatMessages
            .Where(c => c.ThreadKey == key)
            .OrderByDescending(c => c.SentAt)
            .Take(take)
            .ToListAsync();
        if (rows.Count == 0) return NotFound();
        var meta = (await EnsureThreadRowsAsync(new[] { key }))[key];
        var member = await db.ChatThreadMembers
            .FirstOrDefaultAsync(m => m.ThreadKey == key && m.PlayerId == Me);
        var participants = await LoadParticipantsAsync(new[] { key });
        participants.TryGetValue(key, out var playerIds);
        var favorites = await LoadFavoritesAsync(new[] { key });
        return Ok(ToWireThread(
            meta,
            member,
            playerIds ?? new List<int>(),
            messages: rows.OrderBy(c => c.SentAt).ToList(),
            isFavorited: favorites.Contains(key)));
    }

    /// <summary>GET <c>thread/club/{clubId}</c> — the 2023-03-21 club
    /// chat tab's only entry point. Binary evidence: RecNet.Runtime
    /// <c>DLDKCILCKNA.txt</c> method <c>BOOOAPKCCJI(Int64)</c>, instr
    /// 045 <c>Move rcx, "thread/club/{0}"</c>, verb constant
    /// <c>Move rdx, 0</c> at instr 054 = HTTPMethods.Get, query fields
    /// <c>maxCount</c> (instr 067, default 10) and <c>mode</c> (instr
    /// 079, default 0). The declared return type is
    /// <c>FGLDKEJLAKB&lt;List&lt;JNMKLHDFPOJ&gt;&gt;</c>, so the body
    /// must be a JSON ARRAY of chat threads even though a club has
    /// exactly one channel — hence the one-element array.
    ///
    /// The three-segment path matched no template before, so this 404'd
    /// and club chat could not open at all.
    ///
    /// <paramref name="mode"/> is accepted and ignored: the client
    /// always sends 0 and its semantics are not resolvable from the
    /// ISIL (no other value is ever passed).</summary>
    [HttpGet("/thread/club/{clubId:long}")]
    public async Task<IActionResult> GetClubThreads(long clubId,
        [FromQuery] int maxCount = 10,
        [FromQuery] int mode = 0)
    {
        _ = mode;
        var take = Math.Clamp(maxCount, 1, 200);

        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId);
        if (club is null) return Ok(Array.Empty<object>());

        // Only roster members see the club channel. Non-members get an
        // empty array rather than a 403 — the client deserialises the
        // body as a list before it ever looks at the status code.
        var onRoster = await db.ClubMemberships.AnyAsync(m =>
            m.ClubId == clubId && m.PlayerId == Me &&
            (m.Permissions & ClubPendingFlag) == 0 &&
            (m.Permissions & ClubBannedFlag) == 0);
        if (!onRoster) return Ok(Array.Empty<object>());

        var key = ClubKey(clubId);
        var meta = await EnsureThreadRowAsync(key);
        // Name the thread after the club the first time it materialises
        // so the thread-list card has a label without a second fetch.
        if (string.IsNullOrEmpty(meta.Name) && !string.IsNullOrEmpty(club.Name))
        {
            meta.Name = club.Name.Length > 128 ? club.Name[..128] : club.Name;
            meta.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var rows = await db.ChatMessages
            .Where(c => c.ThreadKey == key)
            .OrderByDescending(c => c.SentAt)
            .Take(take)
            .ToListAsync();
        var member = await db.ChatThreadMembers
            .FirstOrDefaultAsync(m => m.ThreadKey == key && m.PlayerId == Me);
        var participants = await LoadParticipantsAsync(new[] { key });
        participants.TryGetValue(key, out var playerIds);
        var favorites = await LoadFavoritesAsync(new[] { key });

        return Ok(new List<Dictionary<string, object?>>
        {
            ToWireThread(
                meta,
                member,
                playerIds ?? new List<int>(),
                messages: rows.OrderBy(c => c.SentAt).ToList(),
                isFavorited: favorites.Contains(key)),
        });
    }

    /// <summary>Recent messages for a thread, paginated by
    /// <paramref name="beforeMessageId"/>.</summary>
    [HttpGet("/thread/{chatThreadId}/message")]
    public async Task<IActionResult> GetMessages(string chatThreadId,
        [FromQuery] int maxCount = 50,
        [FromQuery] int? messageCount = null,
        [FromQuery] long? beforeMessageId = null,
        [FromQuery] long? referenceMessageId = null)
    {
        var take = Math.Clamp(messageCount ?? maxCount, 1, 200);
        var key = await ResolveThreadKeyAsync(chatThreadId);
        var thread = await db.ChatThreads.FirstOrDefaultAsync(t => t.ThreadKey == key);
        if (thread is null) return Ok(Array.Empty<object>());
        var q = db.ChatMessages.Where(c => c.ThreadKey == key);
        var before = beforeMessageId ?? referenceMessageId;
        if (before is long b)
            q = q.Where(c => c.Id < b);
        var rows = await q
            .OrderByDescending(c => c.SentAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.OrderBy(c => c.SentAt).Select(c => ToWireMessage(c, thread.Id)).ToList());
    }

    /// <summary>Unread-thread count from per-thread last-read pointers.
    /// A thread counts as unread when there's at least one message
    /// after the caller's <c>LastReadMessageId</c> (or any message at
    /// all when the caller has never read).</summary>
    [HttpGet("/thread/unreadcount")]
    public async Task<IActionResult> UnreadCount()
    {
        // Threads the caller participates in via membership row.
        var memberKeys = await db.ChatThreadMembers
            .Where(m => m.PlayerId == Me)
            .ToListAsync();
        var memberKeyToLastRead = memberKeys.ToDictionary(m => m.ThreadKey, m => m.LastReadMessageId);

        // DM threads also count even without an explicit member row.
        var dmKeys = await db.ChatMessages
            .Where(c => c.ThreadKey.StartsWith($"dm:{Me}:") || c.ThreadKey.EndsWith($":{Me}"))
            .Select(c => c.ThreadKey)
            .Distinct()
            .ToListAsync();

        var keys = memberKeyToLastRead.Keys.Concat(dmKeys).Distinct().ToList();
        if (keys.Count == 0) return Ok(0);

        var perThreadMax = await db.ChatMessages
            .Where(c => keys.Contains(c.ThreadKey) && c.SenderPlayerId != Me)
            .GroupBy(c => c.ThreadKey)
            .Select(g => new { Key = g.Key, MaxId = g.Max(c => c.Id) })
            .ToListAsync();

        var unread = perThreadMax.Count(t =>
        {
            memberKeyToLastRead.TryGetValue(t.Key, out var lastRead);
            return lastRead is null || t.MaxId > lastRead.Value;
        });
        return Ok(unread);
    }

    public sealed record SendChatRequest(string? ChatThreadId, long? RecipientId, string Body);

    /// <summary>Append a message to a thread. The thread is either
    /// referenced by id (existing thread) or by RecipientId (new DM
    /// to that player; we synthesise the canonical dm key). JSON shape
    /// used by the admin SPA / API callers; the 2020 watch posts the
    /// form-urlencoded variant in <see cref="SendForm"/> with multi-
    /// valued <c>ids</c> + a <c>messageContents</c> JSON blob.</summary>
    [HttpPost("/thread")]
    [HttpPost("/thread/{chatThreadId}/message")]
    [Consumes("application/json")]
    public async Task<IActionResult> Send(string? chatThreadId,
        [FromBody] SendChatRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Body)) return BadRequest("empty");
        var trimmed = body.Body.Trim();
        if (trimmed.Length > 2000) trimmed = trimmed[..2000];

        var key = chatThreadId is null ? null : await ResolveThreadKeyAsync(chatThreadId);
        key ??= body.ChatThreadId is null ? null : await ResolveThreadKeyAsync(body.ChatThreadId);
        key ??= body.RecipientId is long otherId
            ? ChatMessageEntity.DmKey(Me, otherId)
            : null;
        if (key is null) return BadRequest("missing_thread");
        var thread = await EnsureThreadRowAsync(key);

        var entry = new ChatMessageEntity
        {
            ThreadKey = key,
            SenderPlayerId = Me,
            Body = trimmed,
        };
        db.ChatMessages.Add(entry);
        await db.SaveChangesAsync();

        // Notify every participant — INCLUDING the sender. The 2020
        // watch's chat send path doesn't update its local thread cache
        // off the HTTP 200 response, so without a self-targeted push
        // the sender's chat UI doesn't show its own outgoing message
        // until the next thread refresh. DM keys encode the two
        // participants directly; group chat threads fan out across the
        // explicit membership table; club chats fan out over the roster.
        var recipients = await ThreadRecipientsAsync(key);
        recipients.Add(Me);
        foreach (var rid in recipients)
        {
            await notifications.NotifyAsync(rid,
                PushNotificationId.ChatMessageReceived,
                ToWireMessage(entry, thread.Id));
        }

        return Ok(ToWireSendMessageResponse(entry, thread.Id));
    }

    /// <summary>Set the caller's last-read pointer on the thread to
    /// the most recent message id. Drives the inbox's unread badge.</summary>
    [HttpPut("/thread/{chatThreadId}/read")]
    [HttpPost("/thread/{chatThreadId}/read")]
    public async Task<IActionResult> MarkRead(string chatThreadId) =>
        await UpdateLastRead(await ResolveThreadKeyAsync(chatThreadId), latestOnly: true, explicitMessageId: null);

    /// <summary>PUT <c>/thread/{id}/message/{messageId}/read</c> — set
    /// the caller's last-read pointer to a specific message (not just
    /// "latest"). The 2020 watch uses this when the inbox view scrolls
    /// to a particular message rather than the bottom. URL verified
    /// at stringliteral.json: <c>"thread/{0}/message/{1}/read"</c>.</summary>
    [HttpPut("/thread/{chatThreadId}/message/{messageId:long}/read")]
    [HttpPost("/thread/{chatThreadId}/message/{messageId:long}/read")]
    public async Task<IActionResult> MarkMessageRead(string chatThreadId, long messageId) =>
        await UpdateLastRead(await ResolveThreadKeyAsync(chatThreadId), latestOnly: false, explicitMessageId: messageId);

    private async Task<IActionResult> UpdateLastRead(string chatThreadId, bool latestOnly, long? explicitMessageId)
    {
        long? latestId = explicitMessageId;
        if (latestId is null)
        {
            latestId = await db.ChatMessages
                .Where(c => c.ThreadKey == chatThreadId)
                .OrderByDescending(c => c.Id)
                .Select(c => (long?)c.Id)
                .FirstOrDefaultAsync();
        }
        if (latestId is null) return Ok(ChatSuccess);
        _ = latestOnly;

        var row = await db.ChatThreadMembers
            .FirstOrDefaultAsync(m => m.ThreadKey == chatThreadId && m.PlayerId == Me);
        if (row is null)
        {
            row = new ChatThreadMemberEntity
            {
                ThreadKey = chatThreadId,
                PlayerId = Me,
            };
            db.ChatThreadMembers.Add(row);
        }
        row.LastReadMessageId = latestId;
        await db.SaveChangesAsync();
        return Ok(ChatSuccess);
    }

    public sealed record SnoozeRequest(DateTime? Until);

    /// <summary>Mute notifications for the thread until
    /// <c>Until</c> (UTC). Null/missing payload clears the snooze.</summary>
    [HttpPost("/thread/{chatThreadId}/snooze")]
    public async Task<IActionResult> Snooze(string chatThreadId, [FromBody] SnoozeRequest? body)
    {
        chatThreadId = await ResolveThreadKeyAsync(chatThreadId);
        var row = await db.ChatThreadMembers
            .FirstOrDefaultAsync(m => m.ThreadKey == chatThreadId && m.PlayerId == Me);
        if (row is null)
        {
            row = new ChatThreadMemberEntity
            {
                ThreadKey = chatThreadId,
                PlayerId = Me,
            };
            db.ChatThreadMembers.Add(row);
        }
        row.SnoozeUntil = body?.Until;
        await db.SaveChangesAsync();
        var thread = await EnsureThreadRowAsync(chatThreadId);
        return Ok(new Dictionary<string, object?>
        {
            ["chatResult"] = ChatSuccess,
            ["snoozedUntil"] = row.SnoozeUntil,
        });
    }

    /// <summary>Remove the caller from the thread's membership. They
    /// stop receiving new messages and the thread drops off their
    /// inbox. DMs can't be left — there are only two participants and
    /// removing one effectively ends the conversation; the watch
    /// surfaces a "delete chat" option for DMs instead.</summary>
    // The 2023-03-21 client POSTs this; DELETE-only returned 405 and leaving a
    // group chat was impossible on that build.
    [HttpPost("/thread/{chatThreadId}/leave")]
    [HttpDelete("/thread/{chatThreadId}/leave")]
    public async Task<IActionResult> Leave(string chatThreadId)
    {
        chatThreadId = await ResolveThreadKeyAsync(chatThreadId);
        // DMs can't be left, and neither can club chats — club channel
        // membership is the club roster, so "leaving" is leaving the
        // club. Deleting the row here would only discard the caller's
        // last-read pointer.
        if (chatThreadId.StartsWith("dm:") || ClubIdFromKey(chatThreadId) != 0)
            return Ok(ChatInvalidArguments);
        var removed = await db.ChatThreadMembers
            .Where(m => m.ThreadKey == chatThreadId && m.PlayerId == Me)
            .ExecuteDeleteAsync();
        return Ok(removed > 0 ? ChatSuccess : ChatMembershipNotFound);
    }

    /// <summary>Rename payload. A plain class with a parameterless ctor
    /// (not a positional record) because
    /// <see cref="Binding.FormOrJsonModelBinder"/> constructs it with
    /// <c>Activator.CreateInstance</c> and then sets properties.</summary>
    public sealed class RenameRequest
    {
        public string? Name { get; set; }
    }

    /// <summary>Set the thread's display name. Permission: any member
    /// of the thread can rename. For DMs the watch typically nudges
    /// users to pick a nickname after first send; group-chat creators
    /// usually set the name at creation time.
    ///
    /// The 2023-03-21 client sends this as a form POST, not a JSON PUT:
    /// RecNet.Runtime <c>DLDKCILCKNA.txt</c> method
    /// <c>BKNMPEEMNPB(Int64, String)</c>, instr 122
    /// <c>Move rcx, "thread/{0}/rename"</c> with verb constant
    /// <c>Move rdx, 2</c> at instr 131 (= HTTPMethods.Post) and a single
    /// form field <c>name</c> added at instr 145 via
    /// <c>BNDIAONDFFF.AFGEDDANEKP</c>. PUT-only + <c>[FromBody]</c> gave
    /// that client a 405 (and a 415 even if it had used PUT). The
    /// response is consumed by an <c>Action&lt;MGGNHLHPHOF&gt;</c>
    /// (instr 162) — a BARE integer body, not a wrapper object.</summary>
    [HttpPut("/thread/{chatThreadId}/rename")]
    [HttpPost("/thread/{chatThreadId}/rename")]
    public async Task<IActionResult> Rename(string chatThreadId,
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] RenameRequest body)
    {
        chatThreadId = await ResolveThreadKeyAsync(chatThreadId);
        var name = (body?.Name ?? string.Empty).Trim();
        if (name.Length > 128) name = name[..128];

        if (!await IsThreadMemberAsync(chatThreadId)) return Ok(ChatMembershipNotFound);

        var meta = await db.ChatThreads.FirstOrDefaultAsync(t => t.ThreadKey == chatThreadId);
        if (meta is null)
        {
            meta = new ChatThreadEntity
            {
                ThreadKey = chatThreadId,
                CreatorPlayerId = Me,
            };
            db.ChatThreads.Add(meta);
        }
        meta.Name = name;
        meta.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ChatSuccess);
    }

    /// <summary>Favourite payload — plain class for the same
    /// <see cref="Binding.FormOrJsonModelBinder"/> reason as
    /// <see cref="RenameRequest"/>.</summary>
    public sealed class FavoriteRequest
    {
        public bool Favorite { get; set; }
    }

    /// <summary>Star / unstar a chat thread for the caller.
    ///
    /// Binary evidence: RecNet.Runtime <c>DLDKCILCKNA.txt</c> method
    /// <c>DHKDPHCNLKD(Int64, Boolean)</c> — instr 106
    /// <c>Move rcx, "thread/{0}/favorite"</c>, verb constant
    /// <c>Move rdx, 3</c> at instr 115 (= HTTPMethods.Put), single form
    /// field <c>favorite</c> (Boolean, boxed at instr 122, added at
    /// instr 128). No route existed at all, so this 404'd.
    ///
    /// RESPONSE SHAPE: the continuation at instr 142 is
    /// <c>Func&lt;GLIKNMPPEHL, MGGNHLHPHOF&gt;</c> — a client-side
    /// PROJECTION, so the body is the wrapper object
    /// <c>GLIKNMPPEHL</c>, NOT the bare chat-result int the method's
    /// return type suggests. Its reader (<c>OGCMIDEFOEE.txt</c> instrs
    /// 042/069) accepts exactly two keys, each in the usual three
    /// casings: <c>ChatResult</c> and <c>IsFavorited</c>.
    ///
    /// The flag is per-(player, thread) and there is no column for it on
    /// <see cref="ChatThreadMemberEntity"/>, so it is persisted in the
    /// generic <c>PlayerSettings</c> table under
    /// <c>chat.favorite:&lt;threadKey&gt;</c> and read back into the
    /// thread DTO's <c>IsFavorited</c> by
    /// <see cref="LoadFavoritesAsync"/>.</summary>
    [HttpPut("/thread/{chatThreadId}/favorite")]
    [HttpPost("/thread/{chatThreadId}/favorite")]
    public async Task<IActionResult> Favorite(string chatThreadId,
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] FavoriteRequest body)
    {
        var key = await ResolveThreadKeyAsync(chatThreadId);
        if (!await db.ChatThreads.AnyAsync(t => t.ThreadKey == key) &&
            !await db.ChatMessages.AnyAsync(c => c.ThreadKey == key))
        {
            return Ok(new Dictionary<string, object?>
            {
                ["chatResult"] = ChatThreadNotFound,
                ["isFavorited"] = false,
            });
        }
        if (!await IsThreadMemberAsync(key))
        {
            return Ok(new Dictionary<string, object?>
            {
                ["chatResult"] = ChatMembershipNotFound,
                ["isFavorited"] = false,
            });
        }

        var favorite = body?.Favorite ?? false;
        var settingKey = FavoriteSettingKey(key);
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == Me && s.Key == settingKey);
        if (favorite)
        {
            if (row is null)
            {
                db.PlayerSettings.Add(new PlayerSettingEntity
                {
                    PlayerId = Me,
                    Key = settingKey,
                    Value = "1",
                });
            }
            else
            {
                row.Value = "1";
            }
        }
        else if (row is not null)
        {
            db.PlayerSettings.Remove(row);
        }
        await db.SaveChangesAsync();

        return Ok(new Dictionary<string, object?>
        {
            ["chatResult"] = ChatSuccess,
            ["isFavorited"] = favorite,
        });
    }

    /// <summary>Is the caller a participant of this thread? DM keys
    /// carry both ids, group chats use the membership table, club chats
    /// use the club roster.</summary>
    private async Task<bool> IsThreadMemberAsync(string key)
    {
        if (key.StartsWith("dm:")) return IsDmParticipant(key, Me);
        var clubId = ClubIdFromKey(key);
        if (clubId != 0)
        {
            return await db.ClubMemberships.AnyAsync(m =>
                m.ClubId == clubId && m.PlayerId == Me &&
                (m.Permissions & ClubPendingFlag) == 0 &&
                (m.Permissions & ClubBannedFlag) == 0);
        }
        return await db.ChatThreadMembers
            .AnyAsync(m => m.ThreadKey == key && m.PlayerId == Me);
    }

    public sealed record CreateGroupChatRequest(List<long> MemberIds, string? Name);

    /// <summary>Create a group chat. Generates a fresh
    /// <c>group:&lt;guid&gt;</c> key, inserts membership rows for the
    /// creator + each invited player, and returns the new thread id.
    /// Empty or duplicate member lists are tolerated (the caller is
    /// always added as a member regardless). JSON variant — the 2020
    /// watch hits the form-urlencoded form in <see cref="WithMembersForm"/>.
    /// </summary>
    [HttpPost("/thread/withmembers")]
    [Consumes("application/json")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupChatRequest body)
    {
        var key = $"group:{Guid.NewGuid():N}";
        var members = (body?.MemberIds ?? new())
            .Where(id => id > 0 && id != Me)
            .Distinct()
            .ToList();
        members.Add(Me);

        db.ChatThreads.Add(new ChatThreadEntity
        {
            ThreadKey = key,
            Name = (body?.Name ?? string.Empty).Trim(),
            CreatorPlayerId = Me,
        });
        foreach (var pid in members)
        {
            db.ChatThreadMembers.Add(new ChatThreadMemberEntity
            {
                ThreadKey = key,
                PlayerId = pid,
            });
        }
        await db.SaveChangesAsync();

        var thread = await db.ChatThreads.FirstAsync(t => t.ThreadKey == key);
        var playerIds = members.Select(x => (int)x).ToList();
        return Ok(new Dictionary<string, object?>
        {
            ["chatThread"] = ToWireThread(thread, null, playerIds),
            ["chatResult"] = ChatSuccess,
        });
    }

    /// <summary>Form-urlencoded variant of <see cref="CreateGroup"/>
    /// used by the 2020 watch. Wire shape verified from the live
    /// request: <c>ids=A&amp;ids=B&amp;messageCount=N</c>. The watch
    /// calls this to "open a chat with these people" — semantics are
    /// fetch-or-create-then-return-recent-messages, NOT just create.
    /// We resolve <c>ids</c> to a canonical thread key (DM if two
    /// participants total including the caller, otherwise group), pull
    /// the latest <c>messageCount</c> messages, and return a shape the
    /// watch's inbox can render directly.</summary>
    [HttpPost("/thread/withmembers")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> WithMembersForm()
    {
        var (ids, _) = ParseIdsAndContents(Request.Form);
        if (ids.Count == 0) return BadRequest(new { error = "missing_ids" });

        var count = Math.Clamp(
            int.TryParse(Request.Form["messageCount"], out var mc) ? mc : 50,
            1, 200);
        var key = await ResolveOrCreateThreadAsync(ids);
        var rows = await db.ChatMessages
            .Where(c => c.ThreadKey == key)
            .OrderByDescending(c => c.SentAt)
            .Take(count)
            .ToListAsync();
        var meta = await EnsureThreadRowAsync(key);
        var participants = ids.Concat(new[] { Me }).Distinct().Select(x => (int)x).ToList();
        var favorites = await LoadFavoritesAsync(new[] { key });
        return Ok(ToWireThread(meta, null, participants,
            messages: rows.OrderBy(c => c.SentAt).ToList(),
            isFavorited: favorites.Contains(key)));
    }

    /// <summary>Form-urlencoded variant of <see cref="Send"/> used by
    /// the 2020 watch. Wire shape: <c>ids=A&amp;ids=B&amp;
    /// messageContents={JSON}</c> where <c>messageContents</c> is the
    /// JSON-encoded <c>RecNet.ChatMessage.Contents</c> blob
    /// (<c>{Type,Version,Data}</c>) — Data is the actual text. The
    /// watch sends this to BOTH create-or-find-thread AND post-message
    /// in one call.</summary>
    [HttpPost("/thread")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> SendForm()
    {
        var (ids, contents) = ParseIdsAndContents(Request.Form);
        if (ids.Count == 0) return BadRequest(new { error = "missing_ids" });

        var text = ExtractChatDataField(contents);
        if (string.IsNullOrWhiteSpace(text)) return BadRequest(new { error = "empty_body" });
        if (text.Length > 2000) text = text[..2000];

        var key = await ResolveOrCreateThreadAsync(ids);
        var thread = await EnsureThreadRowAsync(key);
        var entry = new ChatMessageEntity
        {
            ThreadKey = key,
            SenderPlayerId = Me,
            Body = text,
        };
        db.ChatMessages.Add(entry);
        await db.SaveChangesAsync();

        // Notify every participant, sender INCLUDED — see SendMessageJson
        // above for the rationale; the 2020 watch's chat send path needs
        // the self-push to refresh its own UI.
        var recipients = await ThreadRecipientsAsync(key);
        recipients.Add(Me);
        foreach (var rid in recipients)
        {
            await notifications.NotifyAsync(rid,
                PushNotificationId.ChatMessageReceived,
                ToWireMessage(entry, thread.Id));
        }
        var participants = await LoadParticipantsAsync(new[] { key });
        participants.TryGetValue(key, out var playerIds);
        var favorites = await LoadFavoritesAsync(new[] { key });
        return Ok(new Dictionary<string, object?>
        {
            ["chatThread"] = ToWireThread(thread, null, playerIds ?? new List<int>(),
                latestMessage: entry, isFavorited: favorites.Contains(key)),
            ["chatResult"] = ChatSuccess,
        });
    }

    /// <summary>Form-urlencoded send used by the 2020 watch:
    /// <c>POST /thread/{chatId}</c> with <c>messageContents</c>.
    /// The response is a SendMessageResponse wrapper, not a raw message.</summary>
    [HttpPost("/thread/{chatThreadId}")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> SendMessageForm(string chatThreadId)
    {
        var key = await ResolveThreadKeyAsync(chatThreadId);
        var text = ExtractChatDataField(Request.Form["messageContents"].ToString());
        if (string.IsNullOrWhiteSpace(text)) return Ok(new Dictionary<string, object?>
        {
            ["chatResult"] = ChatInvalidArguments,
        });
        if (text.Length > 2000) text = text[..2000];

        var thread = await EnsureThreadRowAsync(key);
        var entry = new ChatMessageEntity
        {
            ThreadKey = key,
            SenderPlayerId = Me,
            Body = text,
        };
        db.ChatMessages.Add(entry);
        await db.SaveChangesAsync();

        var recipients = await ThreadRecipientsAsync(key);
        // Include the sender so the form-POST send path also refreshes
        // its own chat thread — same reason as the JSON send path above.
        recipients.Add(Me);
        foreach (var rid in recipients)
        {
            await notifications.NotifyAsync(rid,
                PushNotificationId.ChatMessageReceived,
                ToWireMessage(entry, thread.Id));
        }

        return Ok(ToWireSendMessageResponse(entry, thread.Id));
    }

    /// <summary>Pull <c>ids</c> (multi-valued) + <c>messageContents</c>
    /// out of the watch's form post. <c>ids</c> excludes the caller
    /// (the caller is implied by their JWT).</summary>
    private (List<long> ids, string? contents) ParseIdsAndContents(
        Microsoft.AspNetCore.Http.IFormCollection form)
    {
        var ids = new List<long>();
        foreach (var v in form["ids"])
        {
            if (long.TryParse(v, out var id) && id > 0 && id != Me)
                ids.Add(id);
        }
        ids = ids.Distinct().ToList();
        var contents = form["messageContents"].ToString();
        return (ids, string.IsNullOrEmpty(contents) ? null : contents);
    }

    /// <summary>Resolve <paramref name="otherIds"/> + caller to a
    /// canonical thread key. 1 other → DM key. 2+ others → existing
    /// group with the exact same membership, or a fresh
    /// <c>group:&lt;guid&gt;</c> if none. Membership rows are created
    /// lazily so the inbox listing path can find the thread.</summary>
    private async Task<string> ResolveOrCreateThreadAsync(List<long> otherIds)
    {
        if (otherIds.Count == 1)
        {
            var key = ChatMessageEntity.DmKey(Me, otherIds[0]);
            // Ensure a ChatThreadEntity row exists for DMs the watch
            // is mutating, so /thread inbox listings carry consistent
            // metadata. The membership rows aren't needed for DM
            // routing (the key carries the pair) but the inbox queries
            // are simpler when every active thread has a row.
            if (!await db.ChatThreads.AnyAsync(t => t.ThreadKey == key))
            {
                db.ChatThreads.Add(new ChatThreadEntity
                {
                    ThreadKey = key,
                    CreatorPlayerId = Me,
                });
                await db.SaveChangesAsync();
            }
            return key;
        }

        var full = otherIds.Concat(new[] { Me }).Distinct().OrderBy(x => x).ToList();
        // Look for an existing group whose membership matches exactly.
        // Counts the rows per thread that intersect `full`, then takes
        // the threads where total membership == |full| AND intersection
        // size == |full|.
        var candidateGroups = await db.ChatThreadMembers
            .Where(m => full.Contains(m.PlayerId) && m.ThreadKey.StartsWith("group:"))
            .GroupBy(m => m.ThreadKey)
            .Where(g => g.Count() == full.Count)
            .Select(g => g.Key)
            .ToListAsync();
        foreach (var k in candidateGroups)
        {
            var rows = await db.ChatThreadMembers
                .Where(m => m.ThreadKey == k)
                .Select(m => m.PlayerId)
                .ToListAsync();
            if (rows.OrderBy(x => x).SequenceEqual(full)) return k;
        }

        // No matching group → create one.
        var newKey = $"group:{Guid.NewGuid():N}";
        db.ChatThreads.Add(new ChatThreadEntity
        {
            ThreadKey = newKey,
            CreatorPlayerId = Me,
        });
        foreach (var pid in full)
        {
            db.ChatThreadMembers.Add(new ChatThreadMemberEntity
            {
                ThreadKey = newKey,
                PlayerId = pid,
            });
        }
        await db.SaveChangesAsync();
        return newKey;
    }

    /// <summary>Pull the <c>Data</c> field out of the watch's
    /// <c>messageContents</c> JSON: <c>{"Type":0,"Version":1,"Data":"hi"}</c>.
    /// Returns null/empty when the body isn't well-formed.</summary>
    private static string ExtractChatDataField(string? messageContents)
    {
        if (string.IsNullOrWhiteSpace(messageContents)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(messageContents);
            if (doc.RootElement.TryGetProperty("Data", out var data)
                && data.ValueKind == System.Text.Json.JsonValueKind.String)
                return data.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    /// <summary>Add a player to an existing group chat. Idempotent —
    /// re-adding an existing member is a no-op.</summary>
    [HttpPost("/thread/{chatThreadId}/member/{playerId:long}")]
    public async Task<IActionResult> AddMember(string chatThreadId, long playerId)
    {
        chatThreadId = await ResolveThreadKeyAsync(chatThreadId);
        if (!chatThreadId.StartsWith("group:"))
            return Ok(ChatInvalidArguments);
        var iAmMember = await db.ChatThreadMembers
            .AnyAsync(m => m.ThreadKey == chatThreadId && m.PlayerId == Me);
        if (!iAmMember) return Ok(ChatMembershipNotFound);
        var exists = await db.ChatThreadMembers
            .AnyAsync(m => m.ThreadKey == chatThreadId && m.PlayerId == playerId);
        if (exists) return Ok(ChatPlayerAlreadyOnThread);
        if (!exists)
        {
            db.ChatThreadMembers.Add(new ChatThreadMemberEntity
            {
                ThreadKey = chatThreadId,
                PlayerId = playerId,
            });
            await db.SaveChangesAsync();
        }
        return Ok(ChatSuccess);
    }

    private async Task<string> ResolveThreadKeyAsync(string idOrKey)
    {
        if (long.TryParse(idOrKey, out var id))
        {
            var key = await db.ChatThreads
                .Where(t => t.Id == id)
                .Select(t => t.ThreadKey)
                .FirstOrDefaultAsync();
            return key ?? idOrKey;
        }
        return idOrKey;
    }

    private async Task<Dictionary<string, ChatThreadEntity>> EnsureThreadRowsAsync(IEnumerable<string> keys)
    {
        var distinct = keys.Distinct().ToList();
        var rows = await db.ChatThreads
            .Where(t => distinct.Contains(t.ThreadKey))
            .ToDictionaryAsync(t => t.ThreadKey, t => t);

        foreach (var key in distinct.Where(k => !rows.ContainsKey(k)))
        {
            var row = new ChatThreadEntity
            {
                ThreadKey = key,
                CreatorPlayerId = Me,
            };
            db.ChatThreads.Add(row);
            rows[key] = row;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();

        return rows;
    }

    private async Task<ChatThreadEntity> EnsureThreadRowAsync(string key) =>
        (await EnsureThreadRowsAsync(new[] { key }))[key];

    private async Task<Dictionary<string, List<int>>> LoadParticipantsAsync(IEnumerable<string> keys)
    {
        var distinct = keys.Distinct().ToList();
        var result = distinct.ToDictionary(k => k, _ => new List<int>());

        foreach (var key in distinct)
        {
            if (TryParseDmKey(key, out var a, out var b))
            {
                result[key] = new List<int> { (int)a, (int)b };
            }
        }

        var groupKeys = distinct.Where(k => k.StartsWith("group:")).ToList();
        if (groupKeys.Count > 0)
        {
            var members = await db.ChatThreadMembers
                .Where(m => groupKeys.Contains(m.ThreadKey))
                .Select(m => new { m.ThreadKey, m.PlayerId })
                .ToListAsync();
            foreach (var group in members.GroupBy(m => m.ThreadKey))
                result[group.Key] = group.Select(m => (int)m.PlayerId).Distinct().ToList();
        }

        // Club threads have no ChatThreadMembers rows — membership IS
        // the club roster, so joining/leaving the club joins/leaves the
        // chat automatically.
        var clubIds = distinct
            .Select(k => new { Key = k, ClubId = ClubIdFromKey(k) })
            .Where(x => x.ClubId != 0)
            .ToList();
        if (clubIds.Count > 0)
        {
            var ids = clubIds.Select(x => x.ClubId).Distinct().ToList();
            var roster = await db.ClubMemberships
                .Where(m => ids.Contains(m.ClubId) &&
                            (m.Permissions & ClubPendingFlag) == 0 &&
                            (m.Permissions & ClubBannedFlag) == 0)
                .Select(m => new { m.ClubId, m.PlayerId })
                .ToListAsync();
            foreach (var x in clubIds)
            {
                result[x.Key] = roster
                    .Where(m => m.ClubId == x.ClubId)
                    .Select(m => (int)m.PlayerId)
                    .Distinct()
                    .ToList();
            }
        }

        return result;
    }

    /// <summary>PlayerSettings key holding the caller's per-thread
    /// favourite flag. The flag is per-(player, thread) and there is no
    /// column for it on <see cref="ChatThreadMemberEntity"/>, so it
    /// lives in the same generic per-player settings table the rest of
    /// the server uses for exactly this kind of flag (see
    /// RoomsController's playerdata and InventionsController).</summary>
    private const string FavoritePrefix = "chat.favorite:";

    private static string FavoriteSettingKey(string threadKey) => FavoritePrefix + threadKey;

    /// <summary>Thread keys the caller has starred, out of
    /// <paramref name="keys"/>.</summary>
    private async Task<HashSet<string>> LoadFavoritesAsync(IEnumerable<string> keys)
    {
        var distinct = keys.Distinct().ToList();
        if (distinct.Count == 0) return new HashSet<string>();
        var settingKeys = distinct.Select(FavoriteSettingKey).ToList();
        var rows = await db.PlayerSettings
            .AsNoTracking()
            .Where(s => s.PlayerId == Me && settingKeys.Contains(s.Key) && s.Value == "1")
            .Select(s => s.Key)
            .ToListAsync();
        return rows.Select(k => k[FavoritePrefix.Length..]).ToHashSet();
    }

    /// <summary>Players to push a new message to. DM keys carry both
    /// participants; group chats fan out over the membership table;
    /// club chats fan out over the club roster.</summary>
    private async Task<List<long>> ThreadRecipientsAsync(string key)
    {
        if (key.StartsWith("dm:") && TryParseDmKey(key, out var a, out var b))
        {
            var other = a == Me ? b : a;
            return other == Me ? new List<long>() : new List<long> { other };
        }
        if (key.StartsWith("group:"))
        {
            return await db.ChatThreadMembers
                .Where(m => m.ThreadKey == key && m.PlayerId != Me)
                .Select(m => m.PlayerId)
                .ToListAsync();
        }
        var clubId = ClubIdFromKey(key);
        if (clubId != 0)
        {
            return await db.ClubMemberships
                .Where(m => m.ClubId == clubId && m.PlayerId != Me &&
                            (m.Permissions & ClubPendingFlag) == 0 &&
                            (m.Permissions & ClubBannedFlag) == 0)
                .Select(m => m.PlayerId)
                .Distinct()
                .ToListAsync();
        }
        return new List<long>();
    }

    /// <summary>Project a thread row onto the client's chat-thread DTO.
    /// The 2023-03-21 reader (RecNet.Runtime EKEFFNBIOHJ.txt, instrs
    /// 082/109/133/149/173/197/221/245/269) matches exactly nine keys,
    /// each in PascalCase / camelCase / lowercase:
    /// <c>ChatThreadId, LastReadMessageId, Messages, LatestMessage,
    /// PlayerIds, ChatThreadName, SnoozedUntil, IsFavorited, ClubId</c>.
    /// <c>IsFavorited</c> and <c>ClubId</c> did not exist on the 2020
    /// watch's DTO, so they were missing here; both are emitted now.
    /// <c>LatestMessage</c> is emitted alongside <c>Messages</c> because
    /// the thread-list card binds the preview off it even when the full
    /// message array is present.</summary>
    private static Dictionary<string, object?> ToWireThread(
        ChatThreadEntity thread,
        ChatThreadMemberEntity? member,
        List<int> playerIds,
        ChatMessageEntity? latestMessage = null,
        List<ChatMessageEntity>? messages = null,
        bool isFavorited = false)
    {
        var dto = new Dictionary<string, object?>
        {
            ["chatThreadId"] = thread.Id,
            ["chatThreadName"] = thread.Name ?? string.Empty,
            ["lastReadMessageId"] = member?.LastReadMessageId ?? 0,
            ["snoozedUntil"] = member?.SnoozeUntil,
            ["playerIds"] = playerIds,
            ["isFavorited"] = isFavorited,
            ["clubId"] = ClubIdFromKey(thread.ThreadKey),
        };

        if (messages is not null)
        {
            dto["messages"] = messages.Select(m => ToWireMessage(m, thread.Id)).ToList();
            if (messages.Count > 0)
                dto["latestMessage"] = ToWireMessage(messages[^1], thread.Id);
        }
        else if (latestMessage is not null)
        {
            dto["latestMessage"] = ToWireMessage(latestMessage, thread.Id);
        }

        return dto;
    }

    /// <summary>Canonical thread key for a club's chat channel. One
    /// thread per club — the 2023 client's club chat tab is a single
    /// channel, and <c>GET thread/club/{id}</c> returns it wrapped in a
    /// one-element array.</summary>
    private static string ClubKey(long clubId) => $"club:{clubId}";

    /// <summary>Inverse of <see cref="ClubKey"/>; 0 for non-club
    /// threads, which is what the client's <c>ClubId</c> field carries
    /// for DMs and group chats.</summary>
    private static long ClubIdFromKey(string key) =>
        key.StartsWith("club:") && long.TryParse(key.AsSpan(5), out var id) ? id : 0;

    private static Dictionary<string, object?> ToWireMessage(ChatMessageEntity c, long chatThreadId) => new()
    {
        ["chatMessageId"] = c.Id,
        ["chatThreadId"] = chatThreadId,
        ["senderPlayerId"] = (int)c.SenderPlayerId,
        ["timeSent"] = c.SentAt,
        ["contents"] = JsonSerializer.Serialize(new
        {
            Type = 0,
            Version = 1,
            Data = c.Body,
        }),
    };

    private static Dictionary<string, object?> ToWireSendMessageResponse(ChatMessageEntity c, long chatThreadId) => new()
    {
        ["chatMessage"] = ToWireMessage(c, chatThreadId),
        ["chatResult"] = ChatSuccess,
    };

    private static bool TryParseDmKey(string key, out long a, out long b)
    {
        a = b = 0;
        var parts = key.Split(':');
        return parts.Length == 3 && parts[0] == "dm" &&
               long.TryParse(parts[1], out a) &&
               long.TryParse(parts[2], out b);
    }

    private static bool IsDmParticipant(string key, long playerId) =>
        TryParseDmKey(key, out var a, out var b) && (a == playerId || b == playerId);
}
