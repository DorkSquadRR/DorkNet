using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public async Task<ActionResult> GetOne(long eventId)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        return Ok(ToWire(ev));
    }

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
    /// Idempotent — re-responding overwrites.</summary>
    [HttpPost("api/playerevents/v1/respond")]
    public async Task<IActionResult> RespondForm(
        [FromForm(Name = "EventId")] long? eventId,
        [FromForm(Name = "Response")] int response = 0)
    {
        if (eventId is not long evt || evt <= 0) return Ok(new { success = false });
        var existing = await db.PlayerEventResponses
            .FirstOrDefaultAsync(r => r.PlayerId == Me && r.EventId == evt);
        if (existing is null)
            db.PlayerEventResponses.Add(new PlayerEventResponseEntity { PlayerId = Me, EventId = evt, Response = response });
        else
            existing.Response = response;
        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }

    /// <summary>POST <c>api/playerevents/v1/deleteResponse</c> — drop
    /// the caller's RSVP row.</summary>
    [HttpPost("api/playerevents/v1/deleteResponse")]
    public async Task<IActionResult> DeleteResponseForm([FromForm(Name = "EventId")] long? eventId)
    {
        if (eventId is not long evt) return Ok(new { success = true });
        await db.PlayerEventResponses
            .Where(r => r.PlayerId == Me && r.EventId == evt)
            .ExecuteDeleteAsync();
        return Ok(new { success = true, error = "" });
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

    // ── Report (Phase 8) ─────────────────────────────────────────────────

    public sealed class PlayerEventReportRequest
    {
        public int ReportCategory { get; set; }
        public long PlayerEventId { get; set; }
        public string? Details { get; set; }
    }

    [HttpPost("api/playerevents/v1/report")]
    public async Task<IActionResult> Report([FromBody] PlayerEventReportRequest req)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        if (ev is null) return NotFound();
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
        return Ok(new { Reported = true });
    }
}
