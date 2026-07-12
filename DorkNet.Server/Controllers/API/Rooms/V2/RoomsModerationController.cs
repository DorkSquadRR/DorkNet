using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private const int PublicAccessibility = 1;
    private const long CanUseShareCamPermission = 1L << 18;

    private long Me => this.RequireCurrentPlayerId();

    private async Task<RoomEntity?> RequireOwnedRoomAsync(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return null;
        if (room.CreatorPlayerId == Me) return room;

        var coOwner = await db.RoomRoles.AnyAsync(r =>
            r.RoomId == roomId && r.PlayerId == Me && r.Accepted && r.Role == 0);
        return coOwner ? room : null;
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
        var acceptedRoles = await db.RoomRoles
            .Where(r => r.RoomId == roomId && r.PlayerId == playerId && r.Accepted)
            .Select(r => r.Role)
            .ToListAsync();
        int role = (room?.CreatorPlayerId == playerId)
            ? 30
            : acceptedRoles.Select(RoomRoleRank).DefaultIfEmpty(0).Max();
        long permissions = role switch
        {
            30 => -1L,
            20 => 0x0FFFFFFFL,
            10 => 0x0FFFFFFFL,
            5  => 0x000000FFL,
            _  => 0x0000000FL,
        };
        // RoomRole.BJICCBAKLAF.CAN_USE_SHARE_CAM = 18 in the
        // 2020.12 decompile. Public rooms should allow desktop/share
        // screens without requiring host/mod/co-owner, while private
        // rooms keep their explicit role gate.
        if (room?.Accessibility == PublicAccessibility)
        {
            permissions |= CanUseShareCamPermission;
        }
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

    /// <summary>POST <c>api/rooms/v1/verifyRole</c> — status-only
    /// gate used by 2020.12 before privileged room actions. Body is
    /// form or JSON with roomId, role, and optional context.</summary>
    [HttpPost("api/rooms/v1/verifyRole")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> VerifyRole()
    {
        var req = await ReadVerifyRoleRequestAsync();
        if (req.RoomId <= 0) return BadRequest();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId);
        if (room is null) return NotFound();

        var me = Me;
        var required = RoleRank(req.Role);
        if (required <= 0) return Ok();

        var isAdmin = await db.Players
            .Where(p => p.Id == me)
            .Select(p => p.IsAdmin)
            .FirstOrDefaultAsync();
        if (isAdmin || room.CreatorPlayerId == me) return Ok();

        var acceptedRoles = await db.RoomRoles
            .Where(r => r.RoomId == req.RoomId && r.PlayerId == me && r.Accepted)
            .Select(r => r.Role)
            .ToListAsync();
        var actual = acceptedRoles.Select(RoomRoleRank).DefaultIfEmpty(0).Max();
        return actual >= required ? Ok() : Forbid();
    }

    private sealed class VerifyRoleRequest
    {
        public long RoomId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
    }

    private async Task<VerifyRoleRequest> ReadVerifyRoleRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new VerifyRoleRequest
            {
                RoomId = long.TryParse(form["roomId"].FirstOrDefault()
                                       ?? form["RoomId"].FirstOrDefault(), out var roomId)
                    ? roomId
                    : 0,
                Role = form["role"].FirstOrDefault()
                       ?? form["Role"].FirstOrDefault()
                       ?? string.Empty,
                Context = form["context"].FirstOrDefault()
                          ?? form["Context"].FirstOrDefault()
                          ?? string.Empty,
            };
        }

        try
        {
            var req = await JsonSerializer.DeserializeAsync<VerifyRoleRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return req ?? new VerifyRoleRequest();
        }
        catch (JsonException)
        {
            return new VerifyRoleRequest();
        }
    }

    private static int RoomRoleRank(int role) => role switch
    {
        0 => 30, // CoOwner
        1 => 10, // Moderator
        2 => 20, // Host
        _ => 0,
    };

    private static int RoleRank(string role)
    {
        if (int.TryParse(role, out var n)) return n;
        return role.Trim().ToLowerInvariant() switch
        {
            "owner" or "creator" or "coowner" or "co-owner" => 30,
            "host" => 20,
            "moderator" or "mod" => 10,
            "member" => 5,
            "visitor" or "" => 0,
            _ => 0,
        };
    }

    // ── Bare-path per-field mutations (2020.12 watch URLs) ─────────────
    //
    // The 2020.12 client emits per-field mutation URLs like
    // POST rooms/{id}/description rather than POST api/rooms/v2/modify
    // with one field set. Each handler below shares the same
    // RequireOwnedRoomAsync gate as /modify and returns the same
    // ToWireRoom shape so the watch's CreateModifyRoomResponse
    // deserializer reads cleanly.

    public sealed class BareDescriptionRequest { public string? Description { get; set; } }
    public sealed class BareImageRequest { public string? ImageName { get; set; } }
    public sealed class BareTagsRequest { public string? Tags { get; set; } public List<string>? TagsList { get; set; } }
    public sealed class BareIntRequest { public int? Value { get; set; } }
    public sealed class BareBoolRequest { public bool? Value { get; set; } }
    public sealed class BareWarningRequest { public int? RoomWarningMask { get; set; } public string? CustomRoomWarning { get; set; } }

    // Each per-field route registers BOTH POST and PUT — the 2020.12
    // watch's request-builder wraps the HTTP method inside opaque
    // BPHGKAEDBPE helpers; the existing /name handler at
    // RoomsController.cs:1455-1456 set the precedent of "register both
    // because the ISIL is opaque". Same pattern here so the bind never
    // 405s.

    [HttpPost("rooms/{roomId:long}/description")]
    [HttpPut("rooms/{roomId:long}/description")]
    public async Task<IActionResult> BareDescription(long roomId,
        [FromBody] BareDescriptionRequest? body,
        [FromForm(Name = "Description")] string? form) =>
        await ApplyAndReturn(roomId, r => r.Description = body?.Description ?? form ?? r.Description);

    [HttpPost("rooms/{roomId:long}/image")]
    [HttpPut("rooms/{roomId:long}/image")]
    public async Task<IActionResult> BareImage(long roomId,
        [FromBody] BareImageRequest? body,
        [FromForm(Name = "ImageName")] string? form) =>
        await ApplyAndReturn(roomId, r => r.ImageName = body?.ImageName ?? form ?? r.ImageName);

    [HttpPost("rooms/{roomId:long}/tags")]
    [HttpPut("rooms/{roomId:long}/tags")]
    public async Task<IActionResult> BareTags(long roomId,
        [FromBody] BareTagsRequest? body,
        [FromForm(Name = "Tags")] string? form)
    {
        var csv = body?.Tags
            ?? (body?.TagsList is { Count: > 0 } list ? string.Join(',', list) : null)
            ?? form;
        return await ApplyAndReturn(roomId, r => r.TagsCsv = csv ?? r.TagsCsv);
    }

    [HttpPost("rooms/{roomId:long}/accessibility")]
    [HttpPut("rooms/{roomId:long}/accessibility")]
    [HttpPost("roomserver/rooms/{roomId:long}/accessibility")]
    [HttpPut("roomserver/rooms/{roomId:long}/accessibility")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareAccessibility(long roomId)
    {
        var value = await ReadBareIntAsync("accessibility", "Accessibility", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is int vv) r.Accessibility = Math.Clamp(vv, 0, 2);
        });
    }

    private async Task<int?> ReadBareIntAsync(params string[] keys)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in keys)
                if (int.TryParse(form[key].FirstOrDefault(), out var value)) return value;
            return null;
        }

        foreach (var key in keys)
            if (int.TryParse(Request.Query[key].FirstOrDefault(), out var value)) return value;

        if ((Request.ContentLength ?? 0) <= 0) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in keys)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n)) return n;
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out n)) return n;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<long?> ReadBareLongAsync(params string[] keys)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in keys)
                if (long.TryParse(form[key].FirstOrDefault(), out var value)) return value;
            return null;
        }

        foreach (var key in keys)
            if (long.TryParse(Request.Query[key].FirstOrDefault(), out var value)) return value;

        if ((Request.ContentLength ?? 0) <= 0) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in keys)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var n)) return n;
                if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out n)) return n;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<bool?> ReadBareBoolAsync(params string[] keys)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in keys)
                if (TryParseBool(form[key].FirstOrDefault(), out var value)) return value;
            return null;
        }

        foreach (var key in keys)
            if (TryParseBool(Request.Query[key].FirstOrDefault(), out var value)) return value;

        if ((Request.ContentLength ?? 0) <= 0) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return doc.RootElement.ValueKind == JsonValueKind.True
                    ? true
                    : doc.RootElement.ValueKind == JsonValueKind.False
                        ? false
                        : null;
            foreach (var key in keys)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n)) return n != 0;
                if (prop.ValueKind == JsonValueKind.String && TryParseBool(prop.GetString(), out var parsed)) return parsed;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<string?> ReadStringValueAsync(params string[] keys)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in keys)
            {
                var value = form[key].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        foreach (var key in keys)
        {
            var value = Request.Query[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        if ((Request.ContentLength ?? 0) <= 0) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return doc.RootElement.GetString();
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in keys)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Number || prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                    return prop.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<JsonElement?> ReadJsonElementAsync(params string[] stringKeys)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in form.Keys)
            {
                var value = form[key].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value)) dict[key] = value;
            }
            if (dict.Count == 0) return null;
            return JsonSerializer.SerializeToElement(dict);
        }

        if ((Request.ContentLength ?? 0) > 0)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        foreach (var key in stringKeys)
        {
            var value = Request.Query[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return JsonSerializer.SerializeToElement(value);
        }

        return null;
    }

    private static bool TryParseBool(string? raw, out bool value)
    {
        if (bool.TryParse(raw, out value)) return true;
        if (int.TryParse(raw, out var n))
        {
            value = n != 0;
            return true;
        }
        value = false;
        return false;
    }

    private static List<JsonElement> ReadJsonElementList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<JsonElement>();
        try
        {
            return JsonSerializer.Deserialize<JsonElement[]>(json)?.ToList() ?? new List<JsonElement>();
        }
        catch (JsonException)
        {
            return new List<JsonElement>();
        }
    }

    private static List<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                   ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string SerializeLimited<T>(IEnumerable<T> values, int maxChars = 4096)
    {
        var json = JsonSerializer.Serialize(values);
        return json.Length <= maxChars ? json : "[]";
    }

    [HttpPost("rooms/{roomId:long}/cloning")]
    [HttpPut("rooms/{roomId:long}/cloning")]
    public async Task<IActionResult> BareCloning(long roomId, [FromBody] BareBoolRequest? body,
        [FromForm(Name = "CloningAllowed")] bool? form) =>
        await ApplyAndReturn(roomId, r =>
        {
            var v = body?.Value ?? form;
            if (v is bool vv) r.CloningAllowed = vv;
        });

    [HttpPost("rooms/{roomId:long}/automute")]
    [HttpPut("rooms/{roomId:long}/automute")]
    public async Task<IActionResult> BareAutomute(long roomId, [FromBody] BareBoolRequest? body,
        [FromForm(Name = "DisableMicAutoMute")] bool? form) =>
        await ApplyAndReturn(roomId, r =>
        {
            var v = body?.Value ?? form;
            if (v is bool vv) r.DisableMicAutoMute = vv;
        });

    /// <summary><c>rooms/{id}/restrictions</c> — junior / age-related
    /// restriction toggle. Body bool maps to AllowsJuniors (true = no
    /// restriction, false = juniors restricted).</summary>
    [HttpPost("rooms/{roomId:long}/restrictions")]
    [HttpPut("rooms/{roomId:long}/restrictions")]
    public async Task<IActionResult> BareRestrictions(long roomId, [FromBody] BareBoolRequest? body,
        [FromForm(Name = "AllowsJuniors")] bool? form) =>
        await ApplyAndReturn(roomId, r =>
        {
            var v = body?.Value ?? form;
            if (v is bool vv) r.AllowsJuniors = vv;
        });

    [HttpPost("rooms/{roomId:long}/warning")]
    [HttpPut("rooms/{roomId:long}/warning")]
    public async Task<IActionResult> BareWarning(long roomId, [FromBody] BareWarningRequest? body) =>
        await ApplyAndReturn(roomId, r =>
        {
            if (body?.RoomWarningMask is int m) r.RoomWarningMask = m;
            if (body?.CustomRoomWarning is not null) r.CustomRoomWarning = body.CustomRoomWarning;
        });

    [HttpPost("rooms/{roomId:long}/creator")]
    [HttpPut("rooms/{roomId:long}/creator")]
    [HttpPost("roomserver/rooms/{roomId:long}/creator")]
    [HttpPut("roomserver/rooms/{roomId:long}/creator")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareCreator(long roomId)
    {
        var creatorId = await ReadBareLongAsync(
            "creatorAccountId", "CreatorAccountId",
            "creatorPlayerId", "CreatorPlayerId",
            "accountId", "AccountId",
            "value", "Value");
        if (creatorId is null or <= 0) return BadRequest(new { error = "missing_creator" });
        var exists = await db.Players.AnyAsync(p => p.Id == creatorId.Value);
        if (!exists) return NotFound(new { error = "creator_not_found", creatorId });
        return await ApplyAndReturn(roomId, r => r.CreatorPlayerId = creatorId.Value);
    }

    [HttpPost("rooms/{roomId:long}/allow_new_users")]
    [HttpPut("rooms/{roomId:long}/allow_new_users")]
    [HttpPost("roomserver/rooms/{roomId:long}/allow_new_users")]
    [HttpPut("roomserver/rooms/{roomId:long}/allow_new_users")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareAllowNewUsers(long roomId)
    {
        var value = await ReadBareBoolAsync("allowNewUsers", "AllowNewUsers", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is bool v) r.AllowNewUsers = v;
        });
    }

    [HttpPost("rooms/{roomId:long}/min_level")]
    [HttpPut("rooms/{roomId:long}/min_level")]
    [HttpPost("roomserver/rooms/{roomId:long}/min_level")]
    [HttpPut("roomserver/rooms/{roomId:long}/min_level")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareMinLevel(long roomId)
    {
        var value = await ReadBareIntAsync("minLevel", "MinLevel", "level", "Level", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is int v) r.MinLevel = Math.Max(0, v);
        });
    }

    [HttpPost("rooms/{roomId:long}/max_player_calculation_mode")]
    [HttpPut("rooms/{roomId:long}/max_player_calculation_mode")]
    [HttpPost("roomserver/rooms/{roomId:long}/max_player_calculation_mode")]
    [HttpPut("roomserver/rooms/{roomId:long}/max_player_calculation_mode")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareMaxPlayerCalculationMode(long roomId)
    {
        var value = await ReadBareIntAsync(
            "maxPlayerCalculationMode", "MaxPlayerCalculationMode",
            "mode", "Mode",
            "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is int v) r.MaxPlayerCalculationMode = Math.Max(0, v);
        });
    }

    [HttpPost("rooms/{roomId:long}/loadscreen")]
    [HttpPut("rooms/{roomId:long}/loadscreen")]
    [HttpPost("roomserver/rooms/{roomId:long}/loadscreen")]
    [HttpPut("roomserver/rooms/{roomId:long}/loadscreen")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareLoadScreen(long roomId)
    {
        var payload = await ReadJsonElementAsync("loadScreen", "LoadScreen", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (payload is JsonElement p)
                r.LoadScreensJson = p.ValueKind == JsonValueKind.Array
                    ? p.GetRawText()
                    : SerializeLimited(new[] { p });
        });
    }

    [HttpPost("rooms/{roomId:long}/promo_images")]
    [HttpPut("rooms/{roomId:long}/promo_images")]
    [HttpPost("roomserver/rooms/{roomId:long}/promo_images")]
    [HttpPut("roomserver/rooms/{roomId:long}/promo_images")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoImagesAdd(long roomId)
    {
        var image = await ReadStringValueAsync("imageName", "ImageName", "name", "Name", "value", "Value");
        if (string.IsNullOrWhiteSpace(image)) return BadRequest(new { error = "missing_image" });
        return await ApplyAndReturn(roomId, r =>
        {
            var images = ReadStringList(r.PromoImagesJson);
            if (!images.Contains(image, StringComparer.OrdinalIgnoreCase))
                images.Add(image);
            r.PromoImagesJson = SerializeLimited(images);
        });
    }

    [HttpDelete("rooms/{roomId:long}/promo_images")]
    [HttpDelete("roomserver/rooms/{roomId:long}/promo_images")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoImagesRemove(long roomId)
    {
        var image = await ReadStringValueAsync("imageName", "ImageName", "name", "Name", "value", "Value");
        if (string.IsNullOrWhiteSpace(image)) return BadRequest(new { error = "missing_image" });
        return await ApplyAndReturn(roomId, r =>
        {
            var images = ReadStringList(r.PromoImagesJson)
                .Where(v => !string.Equals(v, image, StringComparison.OrdinalIgnoreCase))
                .ToList();
            r.PromoImagesJson = SerializeLimited(images);
        });
    }

    [HttpPost("rooms/{roomId:long}/promo_external")]
    [HttpPut("rooms/{roomId:long}/promo_external")]
    [HttpPost("roomserver/rooms/{roomId:long}/promo_external")]
    [HttpPut("roomserver/rooms/{roomId:long}/promo_external")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoExternalAdd(long roomId)
    {
        var payload = await ReadJsonElementAsync("url", "Url", "value", "Value");
        if (payload is null) return BadRequest(new { error = "missing_promo_external" });
        return await ApplyAndReturn(roomId, r =>
        {
            var items = ReadJsonElementList(r.PromoExternalContentJson);
            var raw = payload.Value.GetRawText();
            if (!items.Any(i => string.Equals(i.GetRawText(), raw, StringComparison.Ordinal)))
                items.Add(payload.Value);
            r.PromoExternalContentJson = SerializeLimited(items);
        });
    }

    [HttpDelete("rooms/{roomId:long}/promo_external")]
    [HttpDelete("roomserver/rooms/{roomId:long}/promo_external")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoExternalRemove(long roomId)
    {
        var match = await ReadStringValueAsync("id", "Id", "url", "Url", "value", "Value");
        if (string.IsNullOrWhiteSpace(match)) return BadRequest(new { error = "missing_promo_external" });
        return await ApplyAndReturn(roomId, r =>
        {
            var items = ReadJsonElementList(r.PromoExternalContentJson)
                .Where(i => !PromoExternalMatches(i, match))
                .ToList();
            r.PromoExternalContentJson = SerializeLimited(items);
        });
    }

    private static bool PromoExternalMatches(JsonElement item, string match)
    {
        if (string.Equals(item.GetRawText(), match, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.ValueKind == JsonValueKind.String)
            return string.Equals(item.GetString(), match, StringComparison.OrdinalIgnoreCase);
        if (item.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in new[] { "Id", "id", "Url", "url", "Value", "value" })
        {
            if (item.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                string.Equals(prop.GetString(), match, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary><c>rooms/{id}/comments</c> + <c>voice_chat_encryption</c>
    /// — fields not persisted in the 2020 RoomEntity. Owner-gated
    /// acknowledgement so the watch's settings panel doesn't surface
    /// a "save failed" error; populated when the columns land.</summary>
    [HttpPost("rooms/{roomId:long}/comments")]
    [HttpPut("rooms/{roomId:long}/comments")]
    [HttpPost("rooms/{roomId:long}/voice_chat_encryption")]
    [HttpPut("rooms/{roomId:long}/voice_chat_encryption")]
    public async Task<IActionResult> BareAck(long roomId) =>
        await ApplyAndReturn(roomId, _ => { /* no-op: not modelled yet */ });

    /// <summary><c>rooms/{id}/modify</c> — bare-path equivalent
    /// of <c>api/rooms/v2/modify</c>. Reuses <see cref="Modify"/> by
    /// forwarding the body with RoomId pre-populated from the URL.</summary>
    [HttpPost("rooms/{roomId:long}/modify")]
    [HttpPut("rooms/{roomId:long}/modify")]
    public async Task<IActionResult> BareModify(long roomId, [FromBody] ModifyRoomRequest req)
    {
        req.RoomId = roomId;
        return await Modify(req);
    }

    /// <summary>POST <c>rooms/{id}/clone</c> — bare-path alias of
    /// <c>api/rooms/v1/clone</c>. The actual clone logic lives on
    /// <see cref="DorkNet.Server.Controllers.API.Rooms.V2.RoomsController"/>;
    /// this handler proxies via the service so we don't duplicate the
    /// substantial clone-rooms logic.</summary>
    [HttpPost("rooms/{roomId:long}/clone")]
    public async Task<IActionResult> BareClone(long roomId,
        [FromBody] CloneBareRequest? body,
        [FromForm(Name = "Name")] string? formName)
    {
        var newName = body?.Name ?? formName ?? string.Empty;
        var source = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (source is null) return NotFound();
        if (!source.CloningAllowed && source.CreatorPlayerId != Me)
            return Forbid();

        if (string.IsNullOrWhiteSpace(newName))
            newName = $"{source.Name}Copy";
        else
            newName = newName.Trim();

        // Uniqueness — append a numeric suffix until free.
        var baseName = newName;
        int suffix = 1;
        while (await db.Rooms.AnyAsync(r => r.Name == newName))
        {
            suffix += 1;
            newName = $"{baseName}{suffix}";
        }

        var clone = new RoomEntity
        {
            Name = newName,
            Description = source.Description,
            CreatorPlayerId = Me,
            ImageName = source.ImageName,
            State = 0,
            Accessibility = source.Accessibility,
            SupportsLevelVoting = source.SupportsLevelVoting,
            IsAGRoom = false,
            IsDormRoom = false,
            CloningAllowed = source.CloningAllowed,
            SupportsVRLow = source.SupportsVRLow,
            SupportsMobile = source.SupportsMobile,
            SupportsScreens = source.SupportsScreens,
            SupportsWalkVR = source.SupportsWalkVR,
            SupportsTeleportVR = source.SupportsTeleportVR,
            AllowsJuniors = source.AllowsJuniors,
            AllowNewUsers = source.AllowNewUsers,
            MinLevel = source.MinLevel,
            MaxPlayerCalculationMode = source.MaxPlayerCalculationMode,
            LoadScreensJson = source.LoadScreensJson,
            PromoImagesJson = source.PromoImagesJson,
            PromoExternalContentJson = source.PromoExternalContentJson,
            RoomWarningMask = source.RoomWarningMask,
            CustomRoomWarning = source.CustomRoomWarning,
            DisableMicAutoMute = source.DisableMicAutoMute,
            LocationReplicationId = source.LocationReplicationId,
            IsStudioRoom = source.IsStudioRoom,
            IsRoomLinkedToRecRoomStudio = source.IsRoomLinkedToRecRoomStudio,
            StudioSessionId = source.StudioSessionId,
            TagsCsv = source.TagsCsv,
            CurrentDataBlobName = source.CurrentDataBlobName,
        };
        db.Rooms.Add(clone);
        await db.SaveChangesAsync();

        // Copy the source's scenes (subrooms) so the clone is structurally
        // complete. Without this the clone has zero subrooms and the client's
        // room reader rejects it. Shallow copy — scenes point at the same data
        // blobs (copy-on-write happens when the clone is later saved).
        var sourceScenes = await db.RoomScenes
            .Where(s => s.RoomId == source.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var clonedScenes = sourceScenes.Select(s => new RoomSceneEntity
        {
            RoomId = clone.Id,
            OrderIndex = s.OrderIndex,
            Name = s.Name,
            RoomSceneLocationId = s.RoomSceneLocationId,
            DataBlobName = s.DataBlobName,
            StudioSubRoomDataSaveId = s.StudioSubRoomDataSaveId,
            StudioUnityAssetId = s.StudioUnityAssetId,
            StudioAssetBundleNamesCsv = s.StudioAssetBundleNamesCsv,
            MaxPlayers = s.MaxPlayers,
            IsSandbox = s.IsSandbox,
            CanMatchmakeInto = s.CanMatchmakeInto,
            DataModifiedAt = DateTime.UtcNow,
        }).ToList();
        if (clonedScenes.Count > 0)
        {
            db.RoomScenes.AddRange(clonedScenes);
            await db.SaveChangesAsync();
        }

        // The 2023 client's clone (RecNet.Runtime NLDBPDCNNCF.GDHIIAHCBMN)
        // deserializes the response as the FULL room-details object
        // (FGCPNAACHIK — the same type get-by-id / rename return), NOT the
        // slim list-room shape. Returning ToWireRoom here made the strict
        // reader fail → "Failed to copy room: Failed to clone room".
        return Ok(RoomsController.BuildRoomDetails(clone, clonedScenes));
    }
    public sealed class CloneBareRequest { public string? Name { get; set; } }

    // ── Bare-path bans aliases ─────────────────────────────────────────

    public sealed class BareBanRequest { public long PlayerId { get; set; } public int BanType { get; set; } public DateTime? Until { get; set; } public string? Reason { get; set; } }

    [HttpGet("rooms/{roomId:long}/bans")]
    public Task<IActionResult> BareBansList(long roomId) => RoomBansList(roomId);

    [HttpPost("rooms/{roomId:long}/bans")]
    public Task<IActionResult> BareBan(long roomId, [FromBody] BareBanRequest req) =>
        BanFromRoom(new BanFromRoomRequest
        {
            RoomId = roomId,
            PlayerId = req.PlayerId,
            BanType = req.BanType,
            Until = req.Until,
            Reason = req.Reason,
        });

    [HttpDelete("rooms/{roomId:long}/bans/{playerId:long}")]
    public async Task<IActionResult> BareUnban(long roomId, long playerId)
    {
        var room = await RequireOwnedRoomAsync(roomId);
        if (room is null) return NotFound();
        var rows = await db.RoomBans
            .Where(b => b.RoomId == roomId && b.BannedPlayerId == playerId)
            .ToListAsync();
        if (rows.Count == 0) return Ok(new { Removed = 0 });
        db.RoomBans.RemoveRange(rows);
        await db.SaveChangesAsync();
        return Ok(new { Removed = rows.Count });
    }

    [HttpPost("rooms/{roomId:long}/bans/import")]
    public Task<IActionResult> BareBansImport(long roomId, [FromBody] ImportRoomBansRequest req)
    {
        req.RoomId = roomId;
        return ImportRoomBans(req);
    }

    private async Task<IActionResult> ApplyAndReturn(long roomId, Action<RoomEntity> mutator)
    {
        var room = await RequireOwnedRoomAsync(roomId);
        if (room is null) return NotFound();
        mutator(room);
        room.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { Result = 2 }); }
        return Ok(Services.RoomService.ToWireRoom(room));
    }
}
