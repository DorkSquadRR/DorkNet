using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

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
            // The 2023 banned-players reader (IBHAKOOKEEE) has EXACTLY three
            // members — AccountId (Int32), BannedByAccountId (Int32?) and
            // BanStartTime (DateTime) — registered at
            // IsilDump/RecNet.Runtime/ECEBBLBCFKO.txt:279 / :306 / :330 (the
            // property types come from IBHAKOOKEEE.txt:3/:83/:103). None of
            // them overlapped the legacy Id/RoomId/BannedPlayerId keys, so
            // the in-room "banned players" list rendered all-zero rows.
            // AccountId is Int32 on the wire, so clamp the long id.
            AccountId = ToClientAccountId(b.BannedPlayerId),
            BannedByAccountId = ToClientAccountId(b.BannedByPlayerId),
            BanStartTime = b.CreatedAt,
            // Legacy keys kept for the 2020.12 watch — both readers ignore
            // members they don't know.
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

    /// <summary>Account ids are Int64 server-side but Int32 in the 2023
    /// wire DTOs (IBHAKOOKEEE.AccountId is System.Int32). Clamp rather than
    /// wrap so an out-of-range id is obviously wrong instead of aliasing
    /// onto a real account.</summary>
    private static int ToClientAccountId(long accountId)
        => accountId > int.MaxValue
            ? int.MaxValue
            : accountId < int.MinValue
                ? int.MinValue
                : (int)accountId;

    // ── Report ───────────────────────────────────────────────────────────

    // The 2023 client posts the room report as x-www-form-urlencoded with
    // PascalCase keys RoomId (Int64) / RoomKeyId (Int64?) / Details (string) /
    // ReportCategory (Int32) — IsilDump/RecNet.Runtime/IBEOONPEELF.txt:23471
    // ("api/rooms/v2/report"), verb byte 2 = POST at :23473, field names at
    // :23486 / :23499 / :23506 / :23521. A [FromBody] parameter makes
    // ASP.NET reject the form POST with 415 BEFORE the handler runs (the same
    // mechanism documented for clone at :963-968 below), so reports were never
    // persisted and the client logged "Failed to report room"
    // (IBEOONPEELF.txt:23537). Read form-or-JSON manually and keep the older
    // Category/Message names as aliases for the 2020.12 watch.
    [HttpPost("api/rooms/v2/report")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> Report()
    {
        var payload = await ReadPayloadAsync();
        var roomId = LongValue(payload, "RoomId", "RoomKeyId") ?? 0;
        if (roomId <= 0) return BadRequest(new { error = "missing_room" });

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        var msg = RawValue(payload, "Details", "Message") ?? string.Empty;
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = Me,
            TargetPlayerId = room.CreatorPlayerId,
            TargetRoomId = roomId,
            RoomId = roomId,
            Category = IntValue(payload, "ReportCategory", "Category") ?? 0,
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

    // ── Bare-path per-field mutations (2020.12 watch + 2023 roomserver) ──
    //
    // Both clients emit per-field mutation URLs like
    // PUT rooms/{id}/description rather than POST api/rooms/v2/modify with
    // one field set. Each handler below shares the same RequireOwnedRoomAsync
    // gate as /modify and returns the FULL room-details object via
    // ApplyAndReturn (see the note there).
    //
    // Every handler reads the body itself instead of taking a [FromBody]
    // parameter: both clients send x-www-form-urlencoded, and under
    // [ApiController] a [FromBody] parameter makes ASP.NET reject a form body
    // with 415 BEFORE the handler runs (mechanism documented at the clone
    // handler below).
    //
    // Each per-field route registers BOTH POST and PUT — the 2020.12
    // watch's request-builder wraps the HTTP method inside opaque
    // BPHGKAEDBPE helpers; the existing /name handler at
    // RoomsController.cs:1455-1456 set the precedent of "register both
    // because the ISIL is opaque". Same pattern here so the bind never
    // 405s. (The 2023 client is unambiguous: verb byte 3 = PUT, e.g.
    // IsilDump/RecNet.Runtime/NLDBPDCNNCF.txt:5369.)
    //
    // …and both bare and roomserver/-prefixed paths, because the 2023
    // in-room roomserver client (NLDBPDCNNCF) issues all of these under the
    // roomserver host prefix — a bare-only registration 404s there.

    /// <summary>PUT <c>rooms/{id}/description</c> — form key
    /// <c>description</c> (NLDBPDCNNCF/HBKMCDLDHHO:57).</summary>
    [HttpPost("rooms/{roomId:long}/description")]
    [HttpPut("rooms/{roomId:long}/description")]
    [HttpPost("roomserver/rooms/{roomId:long}/description")]
    [HttpPut("roomserver/rooms/{roomId:long}/description")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareDescription(long roomId)
    {
        // RawValue, not the whitespace-skipping reader: clearing the
        // description sends the key with an empty value.
        var payload = await ReadPayloadAsync();
        var value = RawValue(payload, "description", "value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is not null) r.Description = value;
        });
    }

    /// <summary>PUT <c>rooms/{id}/image</c> — form key <c>imageName</c>
    /// (NLDBPDCNNCF/MCDHHIBPJPD:57).</summary>
    [HttpPost("rooms/{roomId:long}/image")]
    [HttpPut("rooms/{roomId:long}/image")]
    [HttpPost("roomserver/rooms/{roomId:long}/image")]
    [HttpPut("roomserver/rooms/{roomId:long}/image")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareImage(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var value = RawValue(payload, "imageName", "image", "name", "value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is not null) r.ImageName = value;
        });
    }

    /// <summary>PUT <c>rooms/{id}/tags</c>. The 2023 client sends the tag
    /// lists as REPEATED form keys — <c>autoTag</c> then <c>tag</c>
    /// (NLDBPDCNNCF/FOPHALMJKGA:70 and :77, both fed from
    /// <c>IReadOnlyList&lt;string&gt;</c> parameters, NLDBPDCNNCF.txt:5543) —
    /// NOT a single CSV field, so the old <c>Tags</c> binding read nothing
    /// and the edit was a silent no-op.</summary>
    [HttpPost("rooms/{roomId:long}/tags")]
    [HttpPut("rooms/{roomId:long}/tags")]
    [HttpPost("roomserver/rooms/{roomId:long}/tags")]
    [HttpPut("roomserver/rooms/{roomId:long}/tags")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareTags(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var tags = Values(payload, "autoTag", "tag");
        // 2020.12 watch / admin tooling: one comma-separated "Tags" field.
        var csv = Value(payload, "tags");
        if (csv is not null)
            tags.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Only rewrite when the request actually carried a tag field —
        // otherwise a stray/emptied request would silently wipe the room's
        // tags. Sending the keys with no values IS how the client clears
        // them, so key presence (not value count) is the signal.
        var carriesTags = payload.ContainsKey("autoTag") || payload.ContainsKey("tag") || payload.ContainsKey("tags");
        var joined = string.Join(',', tags.Distinct(StringComparer.OrdinalIgnoreCase));
        return await ApplyAndReturn(roomId, r =>
        {
            if (carriesTags) r.TagsCsv = joined;
        });
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

    // ── Multi-field payload reader ──────────────────────────────────────
    //
    // The single-key Read*Async helpers above each parse Request.Body, and a
    // request body can only be consumed once — calling two of them in the
    // same handler silently loses every field after the first. Handlers that
    // need several fields (report / modify / restrictions / warning / bans /
    // loadscreen / promo_external) read the payload ONCE into the map below.
    //
    // It is a key → values map because the 2023 client encodes list
    // parameters as REPEATED form keys (tags sends tag=…&tag=…,
    // bans sends id=…&id=…). Keys are matched case-insensitively, which is
    // what lets one lookup serve both the 2023 camelCase names and the
    // 2020.12 PascalCase ones.

    private async Task<Dictionary<string, List<string>>> ReadPayloadAsync()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string? value)
        {
            if (string.IsNullOrEmpty(key) || value is null) return;
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
            list.Add(value);
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in form.Keys)
                foreach (var value in form[key])
                    Add(key, value);
        }
        else if ((Request.ContentLength ?? 0) > 0)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                            foreach (var item in prop.Value.EnumerateArray())
                                Add(prop.Name, JsonScalar(item));
                        else
                            Add(prop.Name, JsonScalar(prop.Value));
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON / truncated body — fall through to the query string.
            }
        }

        // Query string is the last-resort source (a few of these fields ride
        // on the URL for the 2020.12 watch). Never let it shadow a body field.
        foreach (var key in Request.Query.Keys)
        {
            if (map.ContainsKey(key)) continue;
            foreach (var value in Request.Query[key])
                Add(key, value);
        }

        return map;
    }

    private static string? JsonScalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
        _ => null,
    };

    /// <summary>First NON-EMPTY value for any of <paramref name="keys"/>.</summary>
    private static string? Value(Dictionary<string, List<string>> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var list)) continue;
            foreach (var value in list)
                if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>First value for any of <paramref name="keys"/> INCLUDING the
    /// empty string. Text fields (description, customWarning) are cleared by
    /// sending the key with an empty value, so the non-empty filter in
    /// <see cref="Value"/> would turn "clear" into a no-op.</summary>
    private static string? RawValue(Dictionary<string, List<string>> map, params string[] keys)
    {
        foreach (var key in keys)
            if (map.TryGetValue(key, out var list) && list.Count > 0)
                return list[0];
        return null;
    }

    /// <summary>Every non-empty value across <paramref name="keys"/>, in key
    /// order. Keys that differ only by case are visited once (the map is
    /// case-insensitive, so they'd otherwise duplicate the same list).</summary>
    private static List<string> Values(Dictionary<string, List<string>> map, params string[] keys)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!seen.Add(key)) continue;
            if (!map.TryGetValue(key, out var list)) continue;
            result.AddRange(list.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
        }
        return result;
    }

    private static int? IntValue(Dictionary<string, List<string>> map, params string[] keys)
        => int.TryParse(Value(map, keys), out var value) ? value : null;

    private static long? LongValue(Dictionary<string, List<string>> map, params string[] keys)
        => long.TryParse(Value(map, keys), out var value) ? value : null;

    private static bool? BoolValue(Dictionary<string, List<string>> map, params string[] keys)
        => TryParseBool(Value(map, keys), out var value) ? value : null;

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

    /// <summary>PUT <c>rooms/{id}/cloning</c> — form key
    /// <c>cloningAllowed</c> (NLDBPDCNNCF/ECMHAPMBNMA:67).</summary>
    [HttpPost("rooms/{roomId:long}/cloning")]
    [HttpPut("rooms/{roomId:long}/cloning")]
    [HttpPost("roomserver/rooms/{roomId:long}/cloning")]
    [HttpPut("roomserver/rooms/{roomId:long}/cloning")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareCloning(long roomId)
    {
        var value = await ReadBareBoolAsync("cloningAllowed", "CloningAllowed", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is bool v) r.CloningAllowed = v;
        });
    }

    /// <summary>PUT <c>rooms/{id}/automute</c>. The form key is
    /// <c>disable</c>, NOT DisableMicAutoMute
    /// (NLDBPDCNNCF/MAICLJKDBOB:67, from
    /// <c>ChangeRoomMicAutoMute(bool)</c>) — true means "mic auto-mute is
    /// disabled", so it maps straight onto DisableMicAutoMute.</summary>
    [HttpPost("rooms/{roomId:long}/automute")]
    [HttpPut("rooms/{roomId:long}/automute")]
    [HttpPost("roomserver/rooms/{roomId:long}/automute")]
    [HttpPut("roomserver/rooms/{roomId:long}/automute")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareAutomute(long roomId)
    {
        var value = await ReadBareBoolAsync("disable", "Disable", "DisableMicAutoMute", "value", "Value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (value is bool v) r.DisableMicAutoMute = v;
        });
    }

    /// <summary>PUT <c>rooms/{id}/restrictions</c> — the "who can play here"
    /// panel. The 2023 client sends FOUR booleans in one request:
    /// <c>supportsScreens</c>, <c>supportsWalkVR</c>,
    /// <c>supportsTeleportVR</c>, <c>supportsJuniors</c>
    /// (NLDBPDCNNCF/GNCDFBNCADM:26/:39/:52/:65, matching the four-bool
    /// signature at NLDBPDCNNCF.txt:6025). The old single-bool
    /// AllowsJuniors binding dropped three of the four settings.</summary>
    [HttpPost("rooms/{roomId:long}/restrictions")]
    [HttpPut("rooms/{roomId:long}/restrictions")]
    [HttpPost("roomserver/rooms/{roomId:long}/restrictions")]
    [HttpPut("roomserver/rooms/{roomId:long}/restrictions")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareRestrictions(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var screens = BoolValue(payload, "supportsScreens");
        var walkVR = BoolValue(payload, "supportsWalkVR");
        var teleportVR = BoolValue(payload, "supportsTeleportVR");
        // 2020.12 watch sent a bare AllowsJuniors/Value instead.
        var juniors = BoolValue(payload, "supportsJuniors", "allowsJuniors", "value");
        return await ApplyAndReturn(roomId, r =>
        {
            if (screens is bool s) r.SupportsScreens = s;
            if (walkVR is bool w) r.SupportsWalkVR = w;
            if (teleportVR is bool t) r.SupportsTeleportVR = t;
            if (juniors is bool j) r.AllowsJuniors = j;
        });
    }

    /// <summary>PUT <c>rooms/{id}/warning</c> — content warnings. Form keys
    /// are <c>warningMask</c> (int) and <c>customWarning</c> (string)
    /// (NLDBPDCNNCF/AIDDFKACPLN:80 and :89), not the RoomWarningMask /
    /// CustomRoomWarning names the old [FromBody] DTO used.</summary>
    [HttpPost("rooms/{roomId:long}/warning")]
    [HttpPut("rooms/{roomId:long}/warning")]
    [HttpPost("roomserver/rooms/{roomId:long}/warning")]
    [HttpPut("roomserver/rooms/{roomId:long}/warning")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareWarning(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var mask = IntValue(payload, "warningMask", "RoomWarningMask");
        // RawValue so clearing the custom warning (empty string) sticks.
        var custom = RawValue(payload, "customWarning", "CustomRoomWarning");
        return await ApplyAndReturn(roomId, r =>
        {
            if (mask is int m) r.RoomWarningMask = m;
            if (custom is not null) r.CustomRoomWarning = custom.Length > 512 ? custom[..512] : custom;
        });
    }

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

    /// <summary>PUT <c>rooms/{id}/loadscreen</c> — form keys
    /// <c>imageName</c> / <c>title</c> / <c>subtitle</c>
    /// (NLDBPDCNNCF/AFBGGBEMOGH:83/:93/:103, verb byte 3 = PUT at
    /// NLDBPDCNNCF.txt:8519). Builds the exact three-member object the
    /// client's load-screen reader wants — ImageName / Title / Subtitle
    /// (EOIMIBBJBCB.txt:255/:282/:298) — and APPENDS it. The old code
    /// serialised the whole form dict as one blob and REPLACED the list, so
    /// a room could never hold more than one load screen.</summary>
    [HttpPost("rooms/{roomId:long}/loadscreen")]
    [HttpPut("rooms/{roomId:long}/loadscreen")]
    [HttpPost("roomserver/rooms/{roomId:long}/loadscreen")]
    [HttpPut("roomserver/rooms/{roomId:long}/loadscreen")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareLoadScreen(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var imageName = Value(payload, "imageName", "name", "value") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(imageName)) return BadRequest(new { error = "missing_image" });
        var title = RawValue(payload, "title") ?? string.Empty;
        var subtitle = RawValue(payload, "subtitle") ?? string.Empty;
        return await ApplyAndReturn(roomId, r =>
        {
            // Re-setting an existing image replaces that entry rather than
            // stacking a duplicate.
            var screens = ReadJsonElementList(r.LoadScreensJson)
                .Where(s => !LoadScreenMatches(s, imageName))
                .ToList();
            screens.Add(JsonSerializer.SerializeToElement(new
            {
                ImageName = imageName,
                Title = title,
                Subtitle = subtitle,
            }));
            r.LoadScreensJson = SerializeLimited(screens);
        });
    }

    /// <summary>DELETE <c>rooms/{id}/loadscreen</c> — "remove room loading
    /// screen" (NLDBPDCNNCF.txt:8677, verb byte 4 = DELETE at :8678), form
    /// key <c>imageName</c> (NLDBPDCNNCF/DMOMLNEJHGE:61). Was not registered
    /// at all, so removing a load screen 405'd.</summary>
    [HttpDelete("rooms/{roomId:long}/loadscreen")]
    [HttpDelete("roomserver/rooms/{roomId:long}/loadscreen")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareLoadScreenRemove(long roomId)
    {
        var imageName = await ReadStringValueAsync("imageName", "ImageName", "name", "Name", "value", "Value")
                        ?? string.Empty;
        if (string.IsNullOrWhiteSpace(imageName)) return BadRequest(new { error = "missing_image" });
        return await ApplyAndReturn(roomId, r =>
        {
            var screens = ReadJsonElementList(r.LoadScreensJson)
                .Where(s => !LoadScreenMatches(s, imageName))
                .ToList();
            r.LoadScreensJson = SerializeLimited(screens);
        });
    }

    /// <summary>A stored load screen matches when its ImageName does — that
    /// is the only identity the client sends on remove.</summary>
    private static bool LoadScreenMatches(JsonElement item, string imageName)
    {
        if (item.ValueKind == JsonValueKind.String)
            return string.Equals(item.GetString(), imageName, StringComparison.OrdinalIgnoreCase);
        if (item.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in new[] { "ImageName", "imageName", "Name", "name" })
        {
            if (item.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                string.Equals(prop.GetString(), imageName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

    /// <summary>PUT <c>rooms/{id}/promo_external</c> — "add room promotional
    /// content" (verb byte 3 = PUT at NLDBPDCNNCF.txt:8181). Both the add and
    /// the remove closures send exactly two form keys: <c>type</c> — a BOXED
    /// System.Int32 (NLDBPDCNNCF/OJAKIAHKALI:017 boxes typeof(System.Int32),
    /// key at :022) — and <c>reference</c>, a string (:031).
    ///
    /// The old code stored the raw form dictionary, so Type landed on the
    /// wire as the STRING "1"; the client's promo-external reader
    /// (EPPPACFECMH.txt:191/:210 registers Type + Reference, and :233 reads
    /// Type through the numeric accessor FAHHHNKECAB while :254 reads
    /// Reference through the string accessor BKKEIINKNCL) rejects a string
    /// there. Build the two members explicitly and typed.</summary>
    [HttpPost("rooms/{roomId:long}/promo_external")]
    [HttpPut("rooms/{roomId:long}/promo_external")]
    [HttpPost("roomserver/rooms/{roomId:long}/promo_external")]
    [HttpPut("roomserver/rooms/{roomId:long}/promo_external")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoExternalAdd(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var reference = Value(payload, "reference", "Reference", "url", "Url", "value", "Value");
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "missing_promo_external" });
        var type = IntValue(payload, "type", "Type") ?? 0;
        return await ApplyAndReturn(roomId, r =>
        {
            var items = ReadJsonElementList(r.PromoExternalContentJson)
                .Where(i => !PromoExternalMatches(i, type, reference))
                .ToList();
            items.Add(JsonSerializer.SerializeToElement(new
            {
                Type = type,
                Reference = reference,
            }));
            r.PromoExternalContentJson = SerializeLimited(items);
        });
    }

    /// <summary>DELETE <c>rooms/{id}/promo_external</c> — "remove room
    /// promotional content" (verb byte 4 = DELETE at NLDBPDCNNCF.txt:8343).
    /// Identity is the (<c>type</c>, <c>reference</c>) PAIR
    /// (NLDBPDCNNCF/LLFPMJMCLBP:022 and :031), not an id/url — the old
    /// id/url/value lookup always missed, so every remove 400'd with
    /// missing_promo_external and promo links could not be deleted.</summary>
    [HttpDelete("rooms/{roomId:long}/promo_external")]
    [HttpDelete("roomserver/rooms/{roomId:long}/promo_external")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BarePromoExternalRemove(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var reference = Value(payload, "reference", "Reference", "id", "Id", "url", "Url", "value", "Value");
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "missing_promo_external" });
        // type is absent on the 2020.12 watch's remove — treat "no type" as
        // "match on reference alone".
        var type = IntValue(payload, "type", "Type");
        return await ApplyAndReturn(roomId, r =>
        {
            var items = ReadJsonElementList(r.PromoExternalContentJson)
                .Where(i => !PromoExternalMatches(i, type, reference))
                .ToList();
            r.PromoExternalContentJson = SerializeLimited(items);
        });
    }

    /// <summary>A stored promo-external entry matches when its Reference
    /// does and (when the caller supplied one) its Type agrees. Legacy rows
    /// written before the typed shape landed may be bare strings or carry
    /// Url/Id instead of Reference, so those spellings are still probed.
    /// </summary>
    private static bool PromoExternalMatches(JsonElement item, int? type, string reference)
    {
        if (item.ValueKind == JsonValueKind.String)
            return string.Equals(item.GetString(), reference, StringComparison.OrdinalIgnoreCase);
        if (item.ValueKind != JsonValueKind.Object) return false;

        var referenceMatches = false;
        foreach (var key in new[] { "Reference", "reference", "Url", "url", "Id", "id", "Value", "value" })
        {
            if (item.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                string.Equals(prop.GetString(), reference, StringComparison.OrdinalIgnoreCase))
            {
                referenceMatches = true;
                break;
            }
        }
        if (!referenceMatches) return false;
        if (type is not int wanted) return true;

        foreach (var key in new[] { "Type", "type" })
        {
            if (!item.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n)) return n == wanted;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out n)) return n == wanted;
        }
        // Untyped legacy row — reference alone is enough.
        return true;
    }

    /// <summary><c>rooms/{id}/comments</c> (form key <c>disable</c>, Boolean —
    /// NLDBPDCNNCF/EKLEDLOPLJB:015/:020) and
    /// <c>rooms/{id}/voice_chat_encryption</c> (form key
    /// <c>encryptVoiceChat</c>, Boolean — NLDBPDCNNCF/NDKDDENPHJP:015/:020).
    /// Both are PUT (verb byte 3 at NLDBPDCNNCF.txt:6879 and :7033) and both
    /// are issued under the roomserver/ prefix by the 2023 in-room settings
    /// panel — the missing twins made them 404 there.
    ///
    /// The VALUES still aren't persisted: RoomEntity has no
    /// DisableRoomComments / EncryptVoiceChat column, so RoomService.cs:1122
    /// and RoomsController.cs:2130 hardcode both to false on the wire.
    /// Adding those columns (plus a migration and the two detail builders)
    /// is outside this file. Until then this stays an owner-gated
    /// acknowledgement that answers with the full details object the call
    /// site's reader [0x1884C1948] expects, so the panel doesn't report a
    /// save failure on top of the toggle not sticking.</summary>
    [HttpPost("rooms/{roomId:long}/comments")]
    [HttpPut("rooms/{roomId:long}/comments")]
    [HttpPost("roomserver/rooms/{roomId:long}/comments")]
    [HttpPut("roomserver/rooms/{roomId:long}/comments")]
    [HttpPost("rooms/{roomId:long}/voice_chat_encryption")]
    [HttpPut("rooms/{roomId:long}/voice_chat_encryption")]
    [HttpPost("roomserver/rooms/{roomId:long}/voice_chat_encryption")]
    [HttpPut("roomserver/rooms/{roomId:long}/voice_chat_encryption")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareAck(long roomId) =>
        await ApplyAndReturn(roomId, _ => { /* value not persistable: no column */ });

    /// <summary>PUT <c>rooms/{id}/modify</c> — the room-settings panel's bulk
    /// save (verb byte 3 = PUT at NLDBPDCNNCF.txt:10829, "modify room"), and
    /// the 2023 client issues it under the roomserver/ prefix too.
    ///
    /// The body is x-www-form-urlencoded with ELEVEN camelCase keys, emitted
    /// in this order by NLDBPDCNNCF/MMMNLOBFFKO: <c>name</c> (string, :039),
    /// <c>description</c> (string, :049), <c>accessibility</c> (Int32, boxed
    /// at :059 / key :063), then eight Booleans — <c>supportsScreens</c>
    /// (:076), <c>supportsWalkVR</c> (:089), <c>supportsTeleportVR</c>
    /// (:102), <c>supportsJuniors</c> (:115), <c>cloningAllowed</c> (:128),
    /// <c>disableMicAutoMute</c> (:141), <c>disableRoomComments</c> (:154)
    /// and <c>encryptVoiceChat</c> (:167).
    ///
    /// The old <c>[FromBody] ModifyRoomRequest</c> forwarder 415'd on that
    /// form body before the handler ran, was missing four of the fields, and
    /// answered <c>{Result, Room}</c> — but the call site attaches the full
    /// details reader [0x1884C1948] (:10826), the same one clone uses, so
    /// the response must be BuildRoomServerDetails.
    ///
    /// disableRoomComments / encryptVoiceChat are deliberately NOT read here:
    /// RoomEntity has no column for either, so there is nowhere to put them
    /// (see <see cref="BareAck"/> above).</summary>
    [HttpPost("rooms/{roomId:long}/modify")]
    [HttpPut("rooms/{roomId:long}/modify")]
    [HttpPost("roomserver/rooms/{roomId:long}/modify")]
    [HttpPut("roomserver/rooms/{roomId:long}/modify")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareModify(long roomId)
    {
        var payload = await ReadPayloadAsync();
        var name = Value(payload, "name", "Name");
        // RawValue for description: clearing it sends the key with "".
        var description = RawValue(payload, "description", "Description");
        var accessibility = IntValue(payload, "accessibility", "Accessibility");
        var imageName = Value(payload, "imageName", "ImageName");
        var screens = BoolValue(payload, "supportsScreens", "SupportsScreens");
        var walkVR = BoolValue(payload, "supportsWalkVR", "SupportsWalkVR");
        var teleportVR = BoolValue(payload, "supportsTeleportVR", "SupportsTeleportVR");
        var juniors = BoolValue(payload, "supportsJuniors", "AllowsJuniors");
        var cloning = BoolValue(payload, "cloningAllowed", "CloningAllowed");
        var autoMute = BoolValue(payload, "disableMicAutoMute", "DisableMicAutoMute");
        var mobile = BoolValue(payload, "supportsMobile", "SupportsMobile");
        var vrLow = BoolValue(payload, "supportsVRLow", "SupportsVRLow");

        var room = await RequireOwnedRoomAsync(roomId);
        if (room is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(name)) room.Name = name.Trim();
        if (description is not null) room.Description = description;
        if (imageName is not null) room.ImageName = imageName;
        if (accessibility is int a) room.Accessibility = Math.Clamp(a, 0, 2);
        if (screens is bool s) room.SupportsScreens = s;
        if (walkVR is bool w) room.SupportsWalkVR = w;
        if (teleportVR is bool t) room.SupportsTeleportVR = t;
        if (juniors is bool j) room.AllowsJuniors = j;
        if (cloning is bool c) room.CloningAllowed = c;
        if (autoMute is bool m) room.DisableMicAutoMute = m;
        if (mobile is bool mo) room.SupportsMobile = mo;
        if (vrLow is bool vl) room.SupportsVRLow = vl;
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
        return Ok(await BuildRoomDetailsAsync(room));
    }

    /// <summary>POST <c>rooms/{id}/clone</c> — bare-path alias of
    /// <c>api/rooms/v1/clone</c>. The actual clone logic lives on
    /// <see cref="DorkNet.Server.Controllers.API.Rooms.V2.RoomsController"/>;
    /// this handler proxies via the service so we don't duplicate the
    /// substantial clone-rooms logic.</summary>
    // The 2023 client posts the new name as an x-www-form-urlencoded field
    // (`name=...`), NOT JSON. A `[FromBody]` parameter demands a JSON
    // content-type and makes ASP.NET reject the form POST with 415 BEFORE the
    // handler runs → the client reports "Failed to clone room" and boots the
    // player to their dorm. Accept all three content types and read the name
    // manually from form / JSON body / query.
    // The 2023 in-room "copy room" flow (RecNet.Runtime NLDBPDCNNCF, the
    // roomserver client that also sends the subrooms/... mutations) POSTs
    // this with the roomserver/ prefix — bare-only routing 404s with an
    // empty body, which the client's promise layer surfaces as a
    // message-less exception ("Failed to copy room: Exception of type
    // '...' was thrown"). Same fix as maxplayers/modify/name.
    [HttpPost("rooms/{roomId:long}/clone")]
    [HttpPost("roomserver/rooms/{roomId:long}/clone")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareClone(long roomId)
    {
        var newName = await ReadCloneNameAsync();
        var source = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (source is null) return NotFound();
        // RRO/AG rooms are first-party templates meant to be cloned — the
        // room-creation "base room" picker starts a new build by cloning one
        // (RecCenter, MakerRoom, …). Their seed rows have CloningAllowed=false,
        // so without the IsAGRoom exception every base-room create 403s.
        if (!source.CloningAllowed && !source.IsAGRoom && source.CreatorPlayerId != Me)
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

        // Assign an explicit user-room id (> 1000), the same way
        // RoomService.CloneAsync does. Relying on DB auto-increment is unsafe:
        // seeded rooms were inserted with manual ids (100..1000) without
        // advancing the Postgres identity sequence, so auto-increment collides
        // with a seeded id → PK violation → the clone POST 500s → the client
        // reports "Failed to clone room" and boots the player to their dorm.
        // (SQLite in tests tracks max(id), which is why the probe passed.)
        var nextId = await db.Rooms
            .Where(r => r.Id > 1000)
            .MaxAsync(r => (long?)r.Id) ?? 1000L;

        var clone = new RoomEntity
        {
            Id = nextId + 1,
            Name = newName,
            Description = source.Description,
            CreatorPlayerId = Me,
            ImageName = source.ImageName,
            State = 0,
            // Clones start PRIVATE regardless of the source's visibility —
            // a fresh copy is the owner's workbench, not something the
            // public should matchmake into until they flip it public
            // themselves (rooms/{id}/accessibility).
            Accessibility = 0,
            SupportsLevelVoting = source.SupportsLevelVoting,
            // Inherit IsAGRoom from the source. This is REQUIRED for cloning a
            // baked RRO room (RecCenter, the games): the client uses IsRRO to
            // decide how to load the scene — RRO rooms load baked geometry from
            // the client's own assets, non-RRO rooms load from a data blob. A
            // non-RRO clone pointing at a baked RRO scene can't load ("Room
            // Load faulted" → "Failed to copy room"). Marking the clone AG lets
            // it load the same baked scene, and the RRO MakerPen overlay makes
            // it editable. Safe for admin: the purge gate and "RR Original"
            // badge both key on CreatorPlayerId == SystemAccountId, so a
            // user-owned clone is still shown as Custom and stays purgeable.
            IsAGRoom = source.IsAGRoom,
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
            // A user clone is community content — NOT an RRO. Copying the
            // source's tags verbatim would keep "recroomoriginal,quest,…" on
            // the clone, making the client treat it as a quest room (wrong
            // badge, and quest-instance logic can boot the player). Match
            // RoomService.CloneAsync and tag it "community".
            TagsCsv = "community",
            // First-party RRO templates (AG rooms owned by the system
            // account — RecCenter and friends) keep their content in baked
            // client geometry; any blob on the row is a MakerPen OVERLAY
            // save made against the shared template. A clone must start
            // from the pristine baked scene with a fresh (empty) blob, not
            // inherit that overlay. Scoped to system-owned templates so
            // copying a USER's RRO-derived room (IsAGRoom inherited) still
            // carries their MakerPen edits along.
            CurrentDataBlobName = IsFirstPartyTemplate(source) ? string.Empty : source.CurrentDataBlobName,
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
        // Scene ids need to be explicit for the same reason room ids do —
        // RoomSceneEntity.Id is an auto/identity column whose Postgres sequence
        // lags behind the manually-seeded rows, so relying on DB generation
        // collides and the whole clone SaveChanges 500s.
        var nextSceneId = await db.RoomScenes.MaxAsync(s => (long?)s.Id) ?? 0L;
        var clonedScenes = sourceScenes.Select(s => new RoomSceneEntity
        {
            Id = ++nextSceneId,
            RoomId = clone.Id,
            OrderIndex = s.OrderIndex,
            Name = s.Name,
            RoomSceneLocationId = s.RoomSceneLocationId,
            // Fresh blob when cloning a first-party template — see
            // CurrentDataBlobName above.
            DataBlobName = IsFirstPartyTemplate(source) ? string.Empty : s.DataBlobName,
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
        // (FGCPNAACHIK — the same type get-by-id / rename return on the
        // roomserver host), NOT the slim list-room shape. This is the
        // roomserver-family path (rooms/{id}/clone), so use
        // BuildRoomServerDetails — the exact shape GET rooms/{id} returns for
        // the 2023 client. Returning ToWireRoom (or the wrong details variant)
        // made the strict reader fail → "Failed to copy room: Failed to clone
        // room".
        return Ok(RoomsController.BuildRoomServerDetails(clone, clonedScenes));
    }
    /// <summary>A seeded first-party RRO template (RecCenter, the games):
    /// AG-flagged AND owned by the system account. User clones inherit the
    /// AG flag but are owned by the player, so they don't match.</summary>
    private static bool IsFirstPartyTemplate(RoomEntity room)
        => room.IsAGRoom && room.CreatorPlayerId == PlayerService.SystemAccountId;

    private async Task<string> ReadCloneNameAsync()
    {
        // Form: name=... (what the 2023 client sends).
        if (Request.HasFormContentType)
        {
            foreach (var k in new[] { "name", "Name" })
            {
                var v = Request.Form[k].ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        else
        {
            // JSON body: { "name": "..." }.
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var k in new[] { "name", "Name" })
                        if (doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                            return v.GetString() ?? string.Empty;
            }
            catch { /* non-JSON / empty body */ }
        }
        return Request.Query["name"].ToString();
    }

    // ── Bare-path bans aliases ─────────────────────────────────────────
    //
    // Every one of these needs the roomserver/ twin: the 2023 in-room
    // banned-players panel issues them under that prefix
    // (NLDBPDCNNCF.txt:12901 GET, :13044 POST "ban players", :13191 POST
    // "rooms/{0}/bans/import", :13353 DELETE "rooms/{0}/bans/{1}"), and a
    // bare-only registration 404s there.

    [HttpGet("rooms/{roomId:long}/bans")]
    [HttpGet("roomserver/rooms/{roomId:long}/bans")]
    public Task<IActionResult> BareBansList(long roomId) => RoomBansList(roomId);

    /// <summary>POST <c>rooms/{id}/bans</c> — "ban players" (verb byte 2 =
    /// POST at NLDBPDCNNCF.txt:13060). The client sends
    /// x-www-form-urlencoded with ONE <c>banMask</c> (boxed Int32) plus a
    /// REPEATED <c>id</c> key — one per account being banned
    /// (NLDBPDCNNCF/DMMILEJMMCC:024 and :034; the `id` binder is the
    /// list-valued overload, fed from the
    /// <c>IReadOnlyList&lt;Int32&gt;</c> parameter of the issuing method at
    /// NLDBPDCNNCF.txt:12920). The old <c>[FromBody] BareBanRequest</c>
    /// rejected the form body with 415 before the handler ran, and named
    /// PlayerId/BanType — keys the client never sends — so in-room bans
    /// never persisted. No response reader is attached at the call site
    /// (plain BDBHCEGKGDC dispatch, :13065), so the ack shape is free.
    /// </summary>
    [HttpPost("rooms/{roomId:long}/bans")]
    [HttpPost("roomserver/rooms/{roomId:long}/bans")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareBan(long roomId)
    {
        var room = await RequireOwnedRoomAsync(roomId);
        if (room is null) return NotFound();

        var payload = await ReadPayloadAsync();
        // banMask IS the BanType enum on the wire. Default 1 (Permanent):
        // the in-room panel's only ban action is a permanent ban.
        var banType = IntValue(payload, "banMask", "banType", "type", "BanType") ?? 1;
        var reason = RawValue(payload, "reason", "Reason") ?? string.Empty;
        DateTime? until = DateTime.TryParse(Value(payload, "until", "Until"), out var parsedUntil)
            ? (DateTime?)parsedUntil.ToUniversalTime()
            : null;

        // "id" is the 2023 key; the others are 2020.12 / admin spellings. The
        // payload map is case-insensitive, so PascalCase variants fall out.
        var ids = Values(payload, "id", "playerId", "accountId")
            .Select(v => long.TryParse(v, out var n) ? n : 0L)
            .Where(n => n > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return BadRequest(new { error = "missing_player" });

        var existing = await db.RoomBans
            .Where(b => b.RoomId == roomId && ids.Contains(b.BannedPlayerId))
            .ToListAsync();
        foreach (var id in ids)
        {
            // Re-banning an already-banned player updates the row rather than
            // stacking duplicates — the panel re-sends the whole selection.
            var row = existing.FirstOrDefault(b => b.BannedPlayerId == id);
            if (row is null)
            {
                db.RoomBans.Add(new RoomBanEntity
                {
                    RoomId = roomId,
                    BannedPlayerId = id,
                    BannedByPlayerId = Me,
                    BanType = banType,
                    Until = until,
                    Reason = reason.Length > 512 ? reason[..512] : reason,
                });
            }
            else
            {
                row.BanType = banType;
                row.Until = until;
                if (reason.Length > 0) row.Reason = reason.Length > 512 ? reason[..512] : reason;
            }
        }
        await db.SaveChangesAsync();
        return Ok(new { Result = 0, Banned = ids.Count });
    }

    [HttpDelete("rooms/{roomId:long}/bans/{playerId:long}")]
    [HttpDelete("roomserver/rooms/{roomId:long}/bans/{playerId:long}")]
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

    /// <summary>POST <c>rooms/{id}/bans/import</c> — "import ban list"
    /// (verb byte 2 = POST at NLDBPDCNNCF.txt:13207). The client names a
    /// SOURCE ROOM whose bans should be copied in — a single Int64 form key
    /// <c>sourceRoomId</c> (NLDBPDCNNCF/KGDNOLMFENE:015 boxes System.Int64,
    /// :020 is the key) — NOT an explicit ban list. The old
    /// <c>[FromBody] ImportRoomBansRequest</c> therefore 415'd on the form
    /// body AND modelled the wrong contract. The JSON ban-list shape stays
    /// available on the legacy <c>api/rooms/v1/importroombans</c>.</summary>
    [HttpPost("rooms/{roomId:long}/bans/import")]
    [HttpPost("roomserver/rooms/{roomId:long}/bans/import")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> BareBansImport(long roomId)
    {
        var target = await RequireOwnedRoomAsync(roomId);
        if (target is null) return NotFound();

        var payload = await ReadPayloadAsync();
        // Deliberately NOT aliased to "roomId" — that name means the TARGET
        // room elsewhere in this surface and would silently self-import.
        var sourceRoomId = LongValue(payload, "sourceRoomId", "SourceRoomId") ?? 0;
        if (sourceRoomId <= 0) return BadRequest(new { error = "missing_source_room" });
        if (sourceRoomId == roomId) return Ok(new { Imported = 0 });

        // Owner-gate BOTH rooms — importing bulk-reads the source room's ban
        // list, which is owner-only information (see RoomBansList).
        var source = await RequireOwnedRoomAsync(sourceRoomId);
        if (source is null) return Forbid();

        var sourceBans = await db.RoomBans
            .Where(b => b.RoomId == sourceRoomId)
            .ToListAsync();
        var seen = new HashSet<long>(await db.RoomBans
            .Where(b => b.RoomId == roomId)
            .Select(b => b.BannedPlayerId)
            .ToListAsync());

        var imported = 0;
        foreach (var b in sourceBans)
        {
            if (!seen.Add(b.BannedPlayerId)) continue;
            db.RoomBans.Add(new RoomBanEntity
            {
                RoomId = roomId,
                BannedPlayerId = b.BannedPlayerId,
                BannedByPlayerId = Me,
                BanType = b.BanType,
                Until = b.Until,
                Reason = b.Reason,
            });
            imported++;
        }
        if (imported > 0) await db.SaveChangesAsync();
        return Ok(new { Imported = imported });
    }

    private async Task<IActionResult> ApplyAndReturn(long roomId, Action<RoomEntity> mutator)
    {
        var room = await RequireOwnedRoomAsync(roomId);
        if (room is null) return NotFound();
        mutator(room);
        room.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { Result = 2 }); }
        return Ok(await BuildRoomDetailsAsync(room));
    }

    /// <summary>The response body EVERY bare-path room mutation must return.
    ///
    /// The 2023 roomserver client dispatches all of them through the same
    /// typed helper (<c>0x1835E50A0</c>) with the same response-reader token
    /// <c>[0x1884C1948]</c> — see NLDBPDCNNCF.txt:5366 (description),
    /// :5691 (tags), :6006 (accessibility), :6169 (restrictions),
    /// :6335 (cloning), :6710 (automute), :6876 (comments), :7195 (min_level),
    /// :7360 (allow_new_users), :7859 (promo_images), :8178 (promo_external),
    /// :8516 (loadscreen), :10826 (modify). That is the SAME token the clone
    /// call site uses (:4815), which the repo already established is the full
    /// FGCPNAACHIK details object built by BuildRoomServerDetails (see
    /// <see cref="BareClone"/>). The slim RoomService.ToWireRoom has no
    /// SubRooms/Roles/Tags/LoadScreens/PromoImages/DataBlob, so the strict
    /// reader's dispose-walk NRE'd on the missing nested lists and every
    /// mutation reported failure even though it had persisted.</summary>
    private async Task<object> BuildRoomDetailsAsync(RoomEntity room)
    {
        var scenes = await db.RoomScenes
            .Where(s => s.RoomId == room.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roles = await db.RoomRoles
            .Where(r => r.RoomId == room.Id)
            .ToListAsync();
        return RoomsController.BuildRoomServerDetails(room, scenes, roles: roles);
    }
}
