using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlayerEvents;

/// <summary>
/// api.rec.net/api/playerevents/v1/* — create, list, RSVP for
/// player-scheduled gatherings.
/// </summary>
[ApiController]
[Authorize]
public class PlayerEventsController(
    DorkNetDbContext db,
    NotificationService notifications) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    // ── Browse (v2) ──────────────────────────────────────────────────────

    /// <summary>GET <c>api/playerevents/v2</c> — upcoming events list.
    /// Returns the next 50 events ordered by start time. Wire shape
    /// is the standard PlayerEvent (Id, CreatorPlayerId, RoomId,
    /// Title, Description, StartsAt, EndsAt, Capacity).</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v2")]
    public async Task<ActionResult> ListUpcoming(
        [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        var rows = await db.PlayerEvents
            .Where(e => e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v2/{eventId:long}")]
    [HttpGet("api/playerevents/v1/{eventId:long}")]
    public async Task<ActionResult> GetOne(long eventId)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        return Ok(ToWire(ev));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/search")]
    [HttpGet("api/playerevents/v1/searchlive")]
    public async Task<ActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? query = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var needle = (q ?? query ?? string.Empty).Trim().ToLowerInvariant();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        var rows = db.PlayerEvents
            .Where(e => e.EndsAt > DateTime.UtcNow);
        if (needle.Length > 0)
        {
            rows = rows.Where(e =>
                e.Title.ToLower().Contains(needle) ||
                e.Description.ToLower().Contains(needle));
        }

        var result = await rows
            .OrderBy(e => e.StartsAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(result.Select(ToWire));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/room/{roomId:long}")]
    public async Task<ActionResult> ForRoom(long roomId)
    {
        var rows = await db.PlayerEvents
            .Where(e => e.RoomId == roomId && e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(100)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/club/{clubId:long}")]
    public async Task<ActionResult> ForClub(long clubId)
    {
        var memberIds = await db.ClubMemberships
            .Where(m => m.ClubId == clubId)
            .Select(m => m.PlayerId)
            .ToListAsync();
        if (memberIds.Count == 0) return Ok(Array.Empty<object>());

        var roomIds = await db.Rooms
            .Where(r => memberIds.Contains(r.CreatorPlayerId))
            .Select(r => r.Id)
            .ToListAsync();
        if (roomIds.Count == 0) return Ok(Array.Empty<object>());

        var rows = await db.PlayerEvents
            .Where(e => roomIds.Contains(e.RoomId) && e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(100)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/clubs")]
    public async Task<ActionResult> ClubEvents([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 200);
        var clubPlayerIds = await db.ClubMemberships
            .Select(m => m.PlayerId)
            .Distinct()
            .ToListAsync();
        if (clubPlayerIds.Count == 0) return Ok(Array.Empty<object>());
        var roomIds = await db.Rooms
            .Where(r => clubPlayerIds.Contains(r.CreatorPlayerId))
            .Select(r => r.Id)
            .ToListAsync();
        var rows = await db.PlayerEvents
            .Where(e => roomIds.Contains(e.RoomId) && e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    public sealed class EventBulkRequest
    {
        public List<long>? PlayerEventIds { get; set; }
        public List<long>? EventIds { get; set; }
        public List<long>? Ids { get; set; }
    }

    [AllowAnonymous]
    [HttpPost("api/playerevents/v1/bulk")]
    [HttpGet("api/playerevents/v1/bulk")]
    public async Task<ActionResult> Bulk([FromBody] EventBulkRequest? body)
    {
        var ids = await ReadEventIdsAsync(body);
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.PlayerEvents
            .Where(e => ids.Contains(e.Id))
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/tagfilters")]
    public IActionResult TagFilters() => Ok(new
    {
        PinnedFilters = new[] { "game", "social", "competition", "class", "club" },
        PopularFilters = new[] { "game", "social", "competition", "class", "club" },
    });

    /// <summary>GET <c>api/playerevents/v1/all</c> — the watch's
    /// <c>LocalPlayerEventInfo</c> fetch (caller's created + RSVP'd
    /// events). Response keys (both required, via
    /// <c>Util.GetObjectListKey</c>): <c>Created</c>, <c>Responses</c>.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/all")]
    public async Task<ActionResult> AllPlayerEvents()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me)
            return Ok(new { Created = Array.Empty<object>(), Responses = Array.Empty<object>() });

        var created = await db.PlayerEvents
            .Where(e => e.CreatorPlayerId == me)
            .OrderByDescending(e => e.StartsAt)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                e.RoomId,
                e.StartsAt,
                e.EndsAt,
                e.Capacity,
            })
            .ToListAsync();

        var responses = await db.PlayerEventResponses
            .Where(r => r.PlayerId == me)
            .Select(r => new
            {
                r.Id,
                r.EventId,
                r.Response,
                r.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { Created = created, Responses = responses });
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/all/{accountId:long}")]
    public async Task<ActionResult> AllPlayerEventsForAccount(long accountId)
    {
        var created = await db.PlayerEvents
            .Where(e => e.CreatorPlayerId == accountId)
            .OrderByDescending(e => e.StartsAt)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                e.RoomId,
                e.StartsAt,
                e.EndsAt,
                e.Capacity,
            })
            .ToListAsync();

        var responses = await db.PlayerEventResponses
            .Where(r => r.PlayerId == accountId)
            .Select(r => new
            {
                r.Id,
                r.EventId,
                r.Response,
                r.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { Created = created, Responses = responses });
    }

    /// <summary>POST <c>api/playerevents/v1/{eventId}/responses</c> —
    /// alternate RSVP route the watch uses for the response-list
    /// flow (older / sub-room view). Body is the same RsvpRequest
    /// as <c>v1/{id}/rsvp</c>; behaves identically.</summary>
    [HttpPost("api/playerevents/v1/{eventId:long}/responses")]
    public Task<ActionResult> RespondList(long eventId, [FromBody] RsvpRequest body)
        => Rsvp(eventId, body);

    /// <summary>Wire shape verified against
    /// <c>Cpp2IL_ISIL/.../RecNet/PlayerEvent.txt:769-815</c>:
    /// PascalCase keys <c>PlayerEventId, Name, Description,
    /// StartTime, EndTime, CreatorPlayerId, AttendeeCount, RoomId,
    /// ImageName, Accessibility</c>. Server-side we store these
    /// under different names (Title, StartsAt, EndsAt, Capacity) —
    /// remap explicitly here so the watch's
    /// <c>PlayerEvent.Deserialize</c> picks them up.</summary>
    private static object ToWire(PlayerEventEntity ev) => new
    {
        PlayerEventId = ev.Id,
        Name = ev.Title,
        ev.Description,
        StartTime = ev.StartsAt,
        EndTime = ev.EndsAt,
        CreatorPlayerId = (int)ev.CreatorPlayerId,
        AttendeeCount = 0, // computed at query time when needed
        ev.RoomId,
        ImageName = string.Empty,
        Accessibility = 1, // Public
    };

    public sealed record CreateEventRequest(
        string Title, string? Description, long RoomId,
        DateTime StartsAt, DateTime EndsAt, int Capacity);

    [HttpPost("api/playerevents/v1/create")]
    [HttpPost("api/playerevents/v1")]
    public async Task<ActionResult> Create([FromBody] CreateEventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("missing_title");
        var ev = new PlayerEventEntity
        {
            CreatorPlayerId = Me,
            RoomId = req.RoomId,
            Title = req.Title.Trim(),
            Description = (req.Description ?? string.Empty).Trim(),
            StartsAt = req.StartsAt,
            EndsAt = req.EndsAt > req.StartsAt ? req.EndsAt : req.StartsAt.AddHours(1),
            Capacity = Math.Max(0, req.Capacity),
        };
        db.PlayerEvents.Add(ev);
        await db.SaveChangesAsync();
        return Ok(new
        {
            ev.Id, ev.Title, ev.Description, ev.RoomId,
            ev.StartsAt, ev.EndsAt, ev.Capacity,
        });
    }

    public sealed record RsvpRequest(int Response);

    [HttpPost("api/playerevents/v1/{eventId:long}/rsvp")]
    public async Task<ActionResult> Rsvp(long eventId, [FromBody] RsvpRequest body)
    {
        if (body.Response < 0 || body.Response > 2)
            return BadRequest("invalid_response");
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();

        var row = await db.PlayerEventResponses
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.PlayerId == Me);
        if (row is null)
        {
            row = new PlayerEventResponseEntity { EventId = eventId, PlayerId = Me };
            db.PlayerEventResponses.Add(row);
        }
        row.Response = body.Response;
        await db.SaveChangesAsync();

        // Tell the creator someone RSVP'd. They can ignore but the
        // watch will refresh their event detail card.
        if (ev.CreatorPlayerId != Me)
            await notifications.NotifyAsync(ev.CreatorPlayerId,
                PushNotificationId.PlayerEventResponseChanged,
                new { ev.Id, From = Me, body.Response });
        return Ok(new { row.EventId, row.PlayerId, row.Response });
    }

    /// <summary>POST <c>api/playerevents/v1/respond</c> — set the
    /// caller's RSVP state for an event. <c>Response</c> is the
    /// <c>PlayerEventResponseType</c> enum (0=Going, 1=Maybe, 2=Pass).
    /// Idempotent — re-responding overwrites.
    ///
    /// Wire return is a BARE int (<c>DGOPHENCPOC</c> enum value:
    /// 0=Success, plus various error codes). Returning an object
    /// here throws InvalidCastException on the 2020.12 deserialiser
    /// — verified at <c>recroom-2020-client-response-contracts.md</c>
    /// under the playerevents/v1/respond section.</summary>
    [HttpPost("api/playerevents/v1/respond")]
    public async Task<IActionResult> RespondForm(
        [FromForm(Name = "EventId")] long? eventId,
        [FromForm(Name = "Response")] int response = 0)
    {
        if (eventId is not long evt || evt <= 0) return Ok(2 /*NoSuchEvent*/);
        var existing = await db.PlayerEventResponses
            .FirstOrDefaultAsync(r => r.PlayerId == Me && r.EventId == evt);
        if (existing is null)
            db.PlayerEventResponses.Add(new PlayerEventResponseEntity { PlayerId = Me, EventId = evt, Response = response });
        else
            existing.Response = response;
        await db.SaveChangesAsync();
        return Ok(0 /*Success*/);
    }

    /// <summary>POST <c>api/playerevents/v1/deleteResponse</c> — drop
    /// the caller's RSVP row. Same bare-int wire shape as
    /// <see cref="RespondForm"/>.</summary>
    [HttpPost("api/playerevents/v1/deleteResponse")]
    public async Task<IActionResult> DeleteResponseForm([FromForm(Name = "EventId")] long? eventId)
    {
        if (eventId is not long evt) return Ok(2 /*NoSuchEvent*/);
        await db.PlayerEventResponses
            .Where(r => r.PlayerId == Me && r.EventId == evt)
            .ExecuteDeleteAsync();
        return Ok(0 /*Success*/);
    }

    /// <summary>DELETE <c>api/playerevents/v2/delete/{id}</c> — hard
    /// delete an event the caller created.</summary>
    [HttpDelete("api/playerevents/v2/delete/{id:long}")]
    public async Task<IActionResult> DeleteByPath(long id)
    {
        var evt = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == id && e.CreatorPlayerId == Me);
        if (evt is null) return NotFound();
        db.PlayerEvents.Remove(evt);
        await db.PlayerEventResponses.Where(r => r.EventId == id).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/name")]
    [HttpPut("api/playerevents/v2/{eventId:long}/name")]
    public async Task<IActionResult> UpdateName(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("name", "Name", "value");
        if (string.IsNullOrWhiteSpace(value)) return BadRequest("missing_name");
        evt.Title = value.Trim()[..Math.Min(value.Trim().Length, 128)];
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/description")]
    [HttpPut("api/playerevents/v2/{eventId:long}/description")]
    public async Task<IActionResult> UpdateDescription(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("description", "Description", "value") ?? string.Empty;
        evt.Description = value.Trim()[..Math.Min(value.Trim().Length, 2000)];
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/room")]
    [HttpPut("api/playerevents/v2/{eventId:long}/room")]
    public async Task<IActionResult> UpdateRoom(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var roomId = await ReadLongFieldAsync("roomId", "RoomId", "value");
        if (roomId is not long id || id <= 0) return BadRequest("missing_room");
        if (!await db.Rooms.AnyAsync(r => r.Id == id)) return NotFound("room_not_found");
        evt.RoomId = id;
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/time")]
    [HttpPut("api/playerevents/v2/{eventId:long}/time")]
    public async Task<IActionResult> UpdateTime(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var start = await ReadDateFieldAsync("startTime", "StartTime", "startsAt", "StartsAt");
        var end = await ReadDateFieldAsync("endTime", "EndTime", "endsAt", "EndsAt");
        if (start is DateTime s) evt.StartsAt = s;
        if (end is DateTime e) evt.EndsAt = e > evt.StartsAt ? e : evt.StartsAt.AddHours(1);
        if (start is null && end is null) return BadRequest("missing_time");
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/image")]
    [HttpPut("api/playerevents/v2/{eventId:long}/image")]
    public async Task<IActionResult> UpdateImage(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("imageName", "ImageName", "value") ?? string.Empty;
        await SetEventSettingAsync(evt, "image", value.Trim());
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/accessibility")]
    [HttpPut("api/playerevents/v2/{eventId:long}/accessibility")]
    public async Task<IActionResult> UpdateAccessibility(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadIntFieldAsync("accessibility", "Accessibility", "value");
        if (value is not int accessibility) return BadRequest("missing_accessibility");
        await SetEventSettingAsync(evt, "accessibility", accessibility.ToString());
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/tags")]
    [HttpPut("api/playerevents/v2/{eventId:long}/tags")]
    public async Task<IActionResult> UpdateTags(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("tags", "Tags", "value") ?? string.Empty;
        await SetEventSettingAsync(evt, "tags", value.Trim());
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/multiinstance")]
    [HttpPut("api/playerevents/v2/{eventId:long}/multiinstance")]
    public async Task<IActionResult> UpdateMultiInstance(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadBoolFieldAsync("multiInstance", "MultiInstance", "value");
        await SetEventSettingAsync(evt, "multiinstance", (value ?? false) ? "true" : "false");
        return Ok(ToWire(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/club")]
    [HttpPut("api/playerevents/v2/{eventId:long}/club")]
    public async Task<IActionResult> UpdateClub(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var clubId = await ReadLongFieldAsync("clubId", "ClubId", "value");
        if (clubId is not long id || id <= 0) return BadRequest("missing_club");
        if (!await db.ClubMemberships.AnyAsync(m => m.ClubId == id && m.PlayerId == Me))
            return Forbid();
        await SetEventSettingAsync(evt, "club", id.ToString());
        return Ok(ToWire(evt));
    }

    [HttpDelete("api/playerevents/v1/{eventId:long}")]
    public async Task<ActionResult> Delete(long eventId)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        if (ev.CreatorPlayerId != Me) return Forbid();
        db.PlayerEvents.Remove(ev);
        // Cascade-clean the responses; SQLite has no FK cascade
        // configured here so do it manually.
        var responses = db.PlayerEventResponses.Where(r => r.EventId == eventId);
        db.PlayerEventResponses.RemoveRange(responses);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ── Bulk-invite (Phase 8) ────────────────────────────────────────────

    public sealed class BulkInviteRequest
    {
        public long PlayerEventId { get; set; }
        public List<int>? InvitedPlayerIds { get; set; }
    }

    /// <summary>POST <c>/api/playerevents/v1/bulkInvite</c> — wire
    /// type per agent ISIL extraction: <c>BulkInviteRequest{PlayerEventId,
    /// InvitedPlayerIds:List&lt;int&gt;}</c>; response
    /// <c>BulkInviteResponse{FailedInvites,Result(CreateModifyPlayerEventStatus)}</c>.
    /// We insert one MessageEntity per recipient so the watch's inbox
    /// shows the invite, and push a per-recipient notification.</summary>
    [HttpPost("api/playerevents/v1/bulkInvite")]
    public async Task<IActionResult> BulkInvite([FromBody] BulkInviteRequest req)
    {
        if (req?.InvitedPlayerIds is null || req.InvitedPlayerIds.Count == 0)
            return Ok(new { FailedInvites = Array.Empty<object>(), Result = 0 });

        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        if (ev is null) return NotFound();
        if (ev.CreatorPlayerId != Me) return Forbid();

        var failed = new List<object>();
        var sender = Me;
        var inviteText = $"You're invited to '{ev.Title}' starting {ev.StartsAt:u}.";
        foreach (var rid in req.InvitedPlayerIds.Distinct())
        {
            var exists = await db.Players.AnyAsync(p => p.Id == rid);
            if (!exists)
            {
                failed.Add(new { PlayerId = rid, Error = "unknown player" });
                continue;
            }
            db.Messages.Add(new MessageEntity
            {
                SenderPlayerId = sender,
                RecipientPlayerId = rid,
                Body = inviteText,
            });
            await notifications.NotifyAsync(rid,
                PushNotificationId.PlayerEventCreated,
                new { ev.Id, ev.Title, ev.StartsAt, From = sender });
        }
        await db.SaveChangesAsync();
        return Ok(new { FailedInvites = failed, Result = 0 });
    }

    public sealed class BroadcastRequest
    {
        public long PlayerEventId { get; set; }
        public string? Message { get; set; }
    }

    [HttpPost("api/playerevents/v1/broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastRequest req)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        if (ev is null) return NotFound();
        if (ev.CreatorPlayerId != Me) return Forbid();

        var recipients = await db.PlayerEventResponses
            .Where(r => r.EventId == ev.Id && r.Response != 2)
            .Select(r => r.PlayerId)
            .Distinct()
            .ToListAsync();
        var body = string.IsNullOrWhiteSpace(req.Message)
            ? $"Update for '{ev.Title}'"
            : req.Message.Trim();
        foreach (var playerId in recipients.Where(id => id != Me))
        {
            db.Messages.Add(new MessageEntity
            {
                SenderPlayerId = Me,
                RecipientPlayerId = playerId,
                Body = body,
            });
            await notifications.NotifyAsync(playerId,
                PushNotificationId.PlayerEventResponseChanged,
                new { ev.Id, ev.Title, Message = body });
        }

        await db.SaveChangesAsync();
        return Ok(new { Success = true, Sent = recipients.Count });
    }

    // ── Report (Phase 8) ─────────────────────────────────────────────────

    public sealed class PlayerEventReportRequest
    {
        public int ReportCategory { get; set; }
        public long PlayerEventId { get; set; }
        public string? Details { get; set; }
    }

    private async Task<PlayerEventEntity?> GetOwnedEventAsync(long eventId)
        => await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.CreatorPlayerId == Me);

    private async Task<List<long>> ReadEventIdsAsync(EventBulkRequest? body)
    {
        var ids = new List<long>();
        if (body?.PlayerEventIds is { Count: > 0 }) ids.AddRange(body.PlayerEventIds);
        if (body?.EventIds is { Count: > 0 }) ids.AddRange(body.EventIds);
        if (body?.Ids is { Count: > 0 }) ids.AddRange(body.Ids);

        foreach (var value in Request.Query.SelectMany(q => q.Value))
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (long.TryParse(part, out var id) && id > 0) ids.Add(id);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in new[] { "playerEventIds", "PlayerEventIds", "eventIds", "EventIds", "ids", "Ids" })
            foreach (var value in form[key])
            foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, out var id) && id > 0) ids.Add(id);
        }

        return ids.Distinct().Take(200).ToList();
    }

    private async Task<string?> ReadStringFieldAsync(params string[] names)
    {
        var fields = await ReadRequestFieldsAsync();
        foreach (var name in names)
            if (fields.TryGetValue(name, out var value))
                return value;
        return null;
    }

    private async Task<long?> ReadLongFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<int?> ReadIntFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<bool?> ReadBoolFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<DateTime?> ReadDateFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<Dictionary<string, string>> ReadRequestFieldsAsync()
    {
        const string itemKey = "__playerevent_fields";
        if (HttpContext.Items.TryGetValue(itemKey, out var cached)
            && cached is Dictionary<string, string> existing)
        {
            return existing;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Request.Query)
            fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var pair in form)
                fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
        }
        else if ((Request.ContentLength ?? 0) > 0
                 && Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? string.Empty
                            : prop.Value.GetRawText();
                }
            }
            catch (JsonException)
            {
            }
        }

        HttpContext.Items[itemKey] = fields;
        return fields;
    }

    private async Task SetEventSettingAsync(PlayerEventEntity evt, string key, string value)
    {
        var settingKey = $"playerevent:{evt.Id}:{key}";
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == evt.CreatorPlayerId && s.Key == settingKey);
        if (row is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = evt.CreatorPlayerId,
                Key = settingKey,
                Value = value,
            });
        }
        else
        {
            row.Value = value;
        }

        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>POST <c>api/playerevents/v1/report</c> — file a
    /// moderation report against an event. Wire response is
    /// <c>KLAMKCBENEA{Success, Message}</c>; returning a bare
    /// <c>{Reported:true}</c> throws on the strict deserialiser.</summary>
    [HttpPost("api/playerevents/v1/report")]
    public async Task<IActionResult> Report([FromBody] PlayerEventReportRequest req)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        if (ev is null) return Ok(new { Success = false, Message = "no_such_event" });
        var msg = (req.Details ?? string.Empty).Trim();
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = Me,
            TargetPlayerId = ev.CreatorPlayerId,
            TargetEventId = req.PlayerEventId,
            Category = req.ReportCategory,
            Message = msg[..Math.Min(1000, msg.Length)],
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty });
    }
}
