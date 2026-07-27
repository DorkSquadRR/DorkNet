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
    private const int ChatSuccess = 0;
    private const int ChatInvalidArguments = 1;
    private const int ChatMembershipNotFound = 3;
    private const int ChatPlayerAlreadyOnThread = 4;

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

        var keys = sentOrReceived
            .Concat(memberKeys)
            .Distinct()
            .Where(k => !k.StartsWith("group:") || memberKeys.Contains(k))
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
                    latestMessage: c);
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
        return Ok(ToWireThread(
            meta,
            member,
            playerIds ?? new List<int>(),
            messages: rows.OrderBy(c => c.SentAt).ToList()));
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
        // explicit membership table.
        var recipients = new List<long>();
        if (key.StartsWith("dm:") && TryParseDmKey(key, out var a, out var b))
        {
            var other = a == Me ? b : a;
            recipients.Add(other);
        }
        else if (key.StartsWith("group:"))
        {
            recipients = await db.ChatThreadMembers
                .Where(m => m.ThreadKey == key && m.PlayerId != Me)
                .Select(m => m.PlayerId)
                .ToListAsync();
        }
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
        if (chatThreadId.StartsWith("dm:"))
            return Ok(ChatInvalidArguments);
        var removed = await db.ChatThreadMembers
            .Where(m => m.ThreadKey == chatThreadId && m.PlayerId == Me)
            .ExecuteDeleteAsync();
        return Ok(removed > 0 ? ChatSuccess : ChatMembershipNotFound);
    }

    public sealed record RenameRequest(string Name);

    /// <summary>Set the thread's display name. Permission: any member
    /// of the thread can rename. For DMs the watch typically nudges
    /// users to pick a nickname after first send; group-chat creators
    /// usually set the name at creation time.</summary>
    [HttpPut("/thread/{chatThreadId}/rename")]
    public async Task<IActionResult> Rename(string chatThreadId, [FromBody] RenameRequest body)
    {
        chatThreadId = await ResolveThreadKeyAsync(chatThreadId);
        var name = (body.Name ?? string.Empty).Trim();
        if (name.Length > 128) name = name[..128];

        var iAmMember = chatThreadId.StartsWith("dm:")
            ? IsDmParticipant(chatThreadId, Me)
            : await db.ChatThreadMembers
                .AnyAsync(m => m.ThreadKey == chatThreadId && m.PlayerId == Me);
        if (!iAmMember) return Ok(ChatMembershipNotFound);

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
        return Ok(ToWireThread(meta, null, participants, messages: rows.OrderBy(c => c.SentAt).ToList()));
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
        var recipients = new List<long>();
        if (key.StartsWith("dm:") && TryParseDmKey(key, out var a, out var b))
        {
            var other = a == Me ? b : a;
            if (other != Me) recipients.Add(other);
        }
        else if (key.StartsWith("group:"))
        {
            recipients = await db.ChatThreadMembers
                .Where(m => m.ThreadKey == key && m.PlayerId != Me)
                .Select(m => m.PlayerId)
                .ToListAsync();
        }
        recipients.Add(Me);
        foreach (var rid in recipients)
        {
            await notifications.NotifyAsync(rid,
                PushNotificationId.ChatMessageReceived,
                ToWireMessage(entry, thread.Id));
        }
        var participants = await LoadParticipantsAsync(new[] { key });
        participants.TryGetValue(key, out var playerIds);
        return Ok(new Dictionary<string, object?>
        {
            ["chatThread"] = ToWireThread(thread, null, playerIds ?? new List<int>(), latestMessage: entry),
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

        var recipients = new List<long>();
        if (key.StartsWith("dm:") && TryParseDmKey(key, out var a, out var b))
        {
            var other = a == Me ? b : a;
            if (other != Me) recipients.Add(other);
        }
        else if (key.StartsWith("group:"))
        {
            recipients = await db.ChatThreadMembers
                .Where(m => m.ThreadKey == key && m.PlayerId != Me)
                .Select(m => m.PlayerId)
                .ToListAsync();
        }
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

        return result;
    }

    private static Dictionary<string, object?> ToWireThread(
        ChatThreadEntity thread,
        ChatThreadMemberEntity? member,
        List<int> playerIds,
        ChatMessageEntity? latestMessage = null,
        List<ChatMessageEntity>? messages = null)
    {
        var dto = new Dictionary<string, object?>
        {
            ["chatThreadId"] = thread.Id,
            ["chatThreadName"] = thread.Name ?? string.Empty,
            ["lastReadMessageId"] = member?.LastReadMessageId ?? 0,
            ["snoozedUntil"] = member?.SnoozeUntil,
            ["playerIds"] = playerIds,
        };

        if (messages is not null)
        {
            dto["messages"] = messages.Select(m => ToWireMessage(m, thread.Id)).ToList();
        }
        else if (latestMessage is not null)
        {
            dto["latestMessage"] = ToWireMessage(latestMessage, thread.Id);
        }

        return dto;
    }

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
