using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Rooms.V2;

/// <summary>
/// api.rec.net/api/rooms/v{1,2}/{modify,modifyPermissions,banfromroom,
/// importroombans,roombans,report,modify/scene,modify/sceneParent,
/// modify/tags,modifyscene} — owner-side mutation surface for rooms.
///
/// Split from <see cref="RoomsController"/> to keep that file
/// (browse / details / clone) at a manageable size. All endpoints
/// are owner-gated (the caller must equal
/// <see cref="RoomEntity.CreatorPlayerId"/>) or admin-bypassed via
/// <see cref="AdminOnlyAttribute"/> on the admin-only sub-routes.
/// </summary>
[ApiController]
[Authorize]
public class RoomsModerationController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    private async Task<RoomEntity?> RequireOwnedRoomAsync(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return null;
        return room.CreatorPlayerId == Me ? room : null;
    }

    // ── /modify (general fields) ─────────────────────────────────────────

    public sealed class ModifyRoomRequest
    {
        public long RoomId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public string? Tags { get; set; }
        public int? Accessibility { get; set; }
        public bool? SupportsScreens { get; set; }
        public bool? SupportsMobile { get; set; }
        public bool? SupportsVRLow { get; set; }
        public bool? SupportsWalkVR { get; set; }
        public bool? SupportsTeleportVR { get; set; }
        public bool? DisableMicAutoMute { get; set; }
    }

    [HttpPost("api/rooms/v2/modify")]
    public async Task<IActionResult> Modify([FromBody] ModifyRoomRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) room.Name = req.Name.Trim();
        if (req.Description is not null) room.Description = req.Description;
        if (req.ImageName is not null) room.ImageName = req.ImageName;
        if (req.Tags is not null) room.TagsCsv = req.Tags;
        if (req.Accessibility.HasValue) room.Accessibility = Math.Clamp(req.Accessibility.Value, 0, 2);
        if (req.SupportsScreens.HasValue) room.SupportsScreens = req.SupportsScreens.Value;
        if (req.SupportsMobile.HasValue) room.SupportsMobile = req.SupportsMobile.Value;
        if (req.SupportsVRLow.HasValue) room.SupportsVRLow = req.SupportsVRLow.Value;
        if (req.SupportsWalkVR.HasValue) room.SupportsWalkVR = req.SupportsWalkVR.Value;
        if (req.SupportsTeleportVR.HasValue) room.SupportsTeleportVR = req.SupportsTeleportVR.Value;
        if (req.DisableMicAutoMute.HasValue) room.DisableMicAutoMute = req.DisableMicAutoMute.Value;
        room.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Likely a unique-name collision.
            return Conflict(new { Result = 2, Message = "name taken" });
        }
        return Ok(new { Result = 0, Room = Services.RoomService.ToWireRoom(room) });
    }

    public sealed class ModifyPermissionsRequest
    {
        public long RoomId { get; set; }
        public int? Accessibility { get; set; }
        public bool? SupportsLevelVoting { get; set; }
        public bool? AllowsJuniors { get; set; }
        public bool? CloningAllowed { get; set; }
        public int? RoomWarningMask { get; set; }
        public string? CustomRoomWarning { get; set; }
    }

    [HttpPost("api/rooms/v2/modifyPermissions")]
    public async Task<IActionResult> ModifyPermissions([FromBody] ModifyPermissionsRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();

        if (req.Accessibility.HasValue) room.Accessibility = Math.Clamp(req.Accessibility.Value, 0, 2);
        if (req.SupportsLevelVoting.HasValue) room.SupportsLevelVoting = req.SupportsLevelVoting.Value;
        if (req.AllowsJuniors.HasValue) room.AllowsJuniors = req.AllowsJuniors.Value;
        if (req.CloningAllowed.HasValue) room.CloningAllowed = req.CloningAllowed.Value;
        if (req.RoomWarningMask.HasValue) room.RoomWarningMask = req.RoomWarningMask.Value;
        if (req.CustomRoomWarning is not null) room.CustomRoomWarning = req.CustomRoomWarning;
        room.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(Services.RoomService.ToWireRoom(room));
    }

    // ── Scene mutations (LocationReplicationId) ──────────────────────────

    public sealed class ModifySceneRequest
    {
        public long RoomId { get; set; }
        public string? LocationReplicationId { get; set; }
    }

    [HttpPost("api/rooms/v1/modify/scene")]
    [HttpPost("api/rooms/v1/modifyscene")]
    public async Task<IActionResult> ModifyScene([FromBody] ModifySceneRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.LocationReplicationId))
            room.LocationReplicationId = req.LocationReplicationId;
        room.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(Services.RoomService.ToWireRoom(room));
    }

    public sealed class ModifySceneParentRequest
    {
        public long RoomId { get; set; }
        public long? ParentRoomId { get; set; }
    }

    /// <summary>Sub-room / nested-scene parent override. We don't
    /// model sub-rooms as a real graph, so we accept-and-ignore but
    /// still owner-gate to prevent grief.</summary>
    [HttpPost("api/rooms/v1/modify/sceneParent")]
    public async Task<IActionResult> ModifySceneParent([FromBody] ModifySceneParentRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();
        return Ok(new { Result = 0 });
    }

    public sealed class ModifyTagsRequest
    {
        public long RoomId { get; set; }
        public string? Tags { get; set; }
    }

    [HttpPost("api/rooms/v1/modify/tags")]
    public async Task<IActionResult> ModifyTags([FromBody] ModifyTagsRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();
        room.TagsCsv = req.Tags ?? string.Empty;
        room.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(Services.RoomService.ToWireRoom(room));
    }

    // ── Bans ─────────────────────────────────────────────────────────────

    public sealed class BanFromRoomRequest
    {
        public long RoomId { get; set; }
        public long PlayerId { get; set; }
        public int BanType { get; set; }      // 0=Soft (kick), 1=Permanent, 2=Temp
        public DateTime? Until { get; set; }
        public string? Reason { get; set; }
    }

    [HttpPost("api/rooms/v2/banfromroom")]
    public async Task<IActionResult> BanFromRoom([FromBody] BanFromRoomRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();
        db.RoomBans.Add(new RoomBanEntity
        {
            RoomId = req.RoomId,
            BannedPlayerId = req.PlayerId,
            BannedByPlayerId = Me,
            BanType = req.BanType,
            Until = req.Until,
            Reason = req.Reason ?? string.Empty,
        });
        await db.SaveChangesAsync();
        return Ok(new { Result = 0 });
    }

    public sealed class ImportRoomBansRequest
    {
        public long RoomId { get; set; }
        public List<BanFromRoomRequest>? Bans { get; set; }
    }

    [HttpPost("api/rooms/v1/importroombans")]
    public async Task<IActionResult> ImportRoomBans([FromBody] ImportRoomBansRequest req)
    {
        var room = await RequireOwnedRoomAsync(req.RoomId);
        if (room is null) return NotFound();
        if (req.Bans is null || req.Bans.Count == 0) return Ok(new { Imported = 0 });

        foreach (var b in req.Bans)
        {
            db.RoomBans.Add(new RoomBanEntity
            {
                RoomId = req.RoomId,
                BannedPlayerId = b.PlayerId,
                BannedByPlayerId = Me,
                BanType = b.BanType,
                Until = b.Until,
                Reason = b.Reason ?? string.Empty,
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { Imported = req.Bans.Count });
    }

    [HttpGet("api/rooms/v1/roombans/{roomId:long}")]
    public async Task<IActionResult> RoomBansList(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        // Only owner sees the ban list (admins also via existing
        // admin endpoints).
        if (room.CreatorPlayerId != Me) return Forbid();
        var rows = await db.RoomBans
            .Where(b => b.RoomId == roomId && (b.Until == null || b.Until > DateTime.UtcNow))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(b => new
        {
            b.Id,
            b.RoomId,
            b.BannedPlayerId,
            b.BannedByPlayerId,
            b.BanType,
            b.Until,
            b.Reason,
            b.CreatedAt,
        }));
    }

    // ── Report ───────────────────────────────────────────────────────────

    public sealed class RoomReportRequest
    {
        public long RoomId { get; set; }
        public int Category { get; set; }
        public string? Message { get; set; }
    }

    [HttpPost("api/rooms/v2/report")]
    public async Task<IActionResult> Report([FromBody] RoomReportRequest req)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId);
        if (room is null) return NotFound();
        var msg = req.Message ?? string.Empty;
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = Me,
            TargetPlayerId = room.CreatorPlayerId,
            TargetRoomId = req.RoomId,
            RoomId = req.RoomId,
            Category = req.Category,
            Message = msg[..Math.Min(1000, msg.Length)],
        });
        await db.SaveChangesAsync();
        return Ok(new { Reported = true });
    }

    public sealed class RoomRolePermissionRequest
    {
        public long RoomId { get; set; }
        public long PlayerId { get; set; }
    }

    /// <summary>POST <c>api/rooms/v1/roomRolePermissions</c> — watch
    /// posts its self-computed role + permission grants for
    /// server-side validation. Role enum:
    /// 0=Visitor, 5=Member, 10=Mod, 20=Host, 30=Owner. Room creator
    /// is always Owner; everyone else defaults to Visitor.</summary>
    [HttpPost("api/rooms/v1/roomRolePermissions")]
    [AllowAnonymous]
    public async Task<IActionResult> RoomRolePermissions(
        [FromBody] RoomRolePermissionRequest? body,
        [FromForm(Name = "RoomId")] long? roomIdForm,
        [FromForm(Name = "PlayerId")] long? playerIdForm)
    {
        var roomId = body?.RoomId ?? roomIdForm ?? 0;
        var playerId = body?.PlayerId ?? playerIdForm ?? (this.CurrentPlayerId() ?? 0);
        if (roomId <= 0 || playerId <= 0)
            return Ok(new { success = true, error = "" });

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        int role = (room?.CreatorPlayerId == playerId) ? 30 : 0;
        long permissions = role switch
        {
            30 => -1L,
            10 => 0x0FFFFFFFL,
            5  => 0x000000FFL,
            _  => 0x0000000FL,
        };
        return Ok(new
        {
            success = true,
            error = "",
            RoomId = roomId,
            PlayerId = (int)playerId,
            Role = role,
            Permissions = permissions,
        });
    }
}
