using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Controllers.API.Rooms.V2;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Playlists;

/// <summary>
/// 2020.12 playlist CRUD surface — every <c>playlists/*</c> route the
/// watch hits beyond the basic listing/detail reads in
/// <see cref="RoomsController"/>. URL templates are bare (no
/// <c>/roomserver/</c> prefix) because the 2020.12 client emits them
/// that way on the Rooms host; the same routes are NOT mirrored under
/// <c>/roomserver/</c> here to avoid duplicating definitions that already
/// exist on <c>RoomsController</c>.
///
/// Wire shape conventions:
///   • Single mutation responses use <c>BMFAGMFKODA</c> — the union
///     <c>KMKPEOGJDFK</c> entry + <c>Rooms</c> + <c>Tags</c> arrays
///     (same shape that <c>PlaylistDetails</c> in <see cref="RoomsController"/>
///     returns). See <see cref="BuildDetailsResponseAsync"/>.
///   • List responses (cheeredby/me, createdby/me, etc.) use bare
///     <c>List&lt;KMKPEOGJDFK&gt;</c> — the union entry alone.
///   • Bookmark / cheer toggles return <c>{success: true}</c>; the
///     watch's reads come back through <c>interactionby/me</c>.
/// </summary>
[ApiController]
public class PlaylistsController(
    PlaylistService playlists,
    DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    // ── Create / delete ──────────────────────────────────────────────

    public sealed class CreatePlaylistRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public string? Tags { get; set; }
    }

    [HttpPost("/playlists")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreatePlaylistRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "missing_name" });
        var created = await playlists.CreateAsync(Me, req.Name, req.Description, req.ImageName, req.Tags);
        return await BuildDetailsResponseAsync(created.Id);
    }

    [HttpDelete("/playlists/{playlistId:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long playlistId)
    {
        try
        {
            var ok = await playlists.DeleteAsync(playlistId, Me);
            return ok ? Ok() : NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ── Per-field mutations (each returns BMFAGMFKODA) ───────────────

    public sealed class StringFieldRequest { public string? Value { get; set; } }
    public sealed class IntFieldRequest { public int? Value { get; set; } }
    public sealed class BoolFieldRequest { public bool? Value { get; set; } }
    public sealed class TagsRequest { public string? Tags { get; set; } public List<string>? TagsList { get; set; } }

    // The 2023-03-21 client sends these as form-urlencoded PUTs. A
    // non-optional [FromBody] parameter made [ApiController] demand JSON and
    // reject every one with 415 before the action ran, so renaming, re-imaging,
    // re-tagging and publishing a playlist all failed silently. Values are now
    // read from form, query or JSON body, whichever arrived.

    [HttpPost("/playlists/{playlistId:long}/name")]
    [HttpPut("/playlists/{playlistId:long}/name")]
    [Authorize]
    public async Task<IActionResult> PlaylistName(long playlistId)
    {
        var value = await ReadFieldAsync("name", "Name", "value", "Value");
        return await ApplyMutation(playlistId, p => p.Name = (value ?? p.Name).Trim());
    }

    [HttpPost("/playlists/{playlistId:long}/description")]
    [HttpPut("/playlists/{playlistId:long}/description")]
    [Authorize]
    public async Task<IActionResult> PlaylistDescription(long playlistId)
    {
        var value = await ReadFieldAsync("description", "Description", "value", "Value");
        return await ApplyMutation(playlistId, p => p.Description = value ?? p.Description);
    }

    [HttpPost("/playlists/{playlistId:long}/image")]
    [HttpPut("/playlists/{playlistId:long}/image")]
    [Authorize]
    public async Task<IActionResult> PlaylistImage(long playlistId)
    {
        var value = await ReadFieldAsync("imageName", "ImageName", "value", "Value");
        return await ApplyMutation(playlistId, p => p.ImageName = value ?? p.ImageName);
    }

    /// <summary>The client sends REPEATED form fields <c>tag</c> and
    /// <c>autoTag</c>, not a single CSV <c>Tags</c> value.</summary>
    [HttpPost("/playlists/{playlistId:long}/tags")]
    [HttpPut("/playlists/{playlistId:long}/tags")]
    [Authorize]
    public async Task<IActionResult> PlaylistTags(long playlistId)
    {
        var tags = await ReadFieldsAsync("tag", "Tag", "autoTag", "AutoTag", "tags", "Tags");
        return await ApplyMutation(playlistId, p =>
        {
            if (tags.Count > 0)
                p.TagsCsv = string.Join(',', tags.Distinct(StringComparer.OrdinalIgnoreCase));
        });
    }

    [HttpPost("/playlists/{playlistId:long}/accessibility")]
    [HttpPut("/playlists/{playlistId:long}/accessibility")]
    [HttpPost("/playlists/{playlistId:long}/visibility")]
    [HttpPut("/playlists/{playlistId:long}/visibility")]
    [Authorize]
    public async Task<IActionResult> PlaylistAccessibility(long playlistId)
    {
        var value = await ReadFieldAsync(
            "accessibility", "Accessibility", "visibility", "Visibility", "value", "Value");
        return await ApplyMutation(playlistId, p =>
        {
            if (int.TryParse(value, out var v)) p.Accessibility = v;
        });
    }

    [HttpPost("/playlists/{playlistId:long}/levelvoting")]
    [HttpPut("/playlists/{playlistId:long}/levelvoting")]
    [Authorize]
    public async Task<IActionResult> PlaylistLevelVoting(long playlistId)
    {
        var value = await ReadFieldAsync("supportsLevelVoting", "SupportsLevelVoting", "value", "Value");
        return await ApplyMutation(playlistId, p =>
        {
            if (bool.TryParse(value, out var v)) p.SupportsLevelVoting = v;
        });
    }

    [HttpPost("/playlists/{playlistId:long}/restrictions")]
    [HttpPut("/playlists/{playlistId:long}/restrictions")]
    [Authorize]
    public async Task<IActionResult> PlaylistRestrictions(long playlistId)
    {
        var juniors  = await ReadFieldAsync("supportsJuniors", "SupportsJuniors");
        var screens  = await ReadFieldAsync("supportsScreens", "SupportsScreens");
        var teleport = await ReadFieldAsync("supportsTeleportVR", "SupportsTeleportVR");
        var walk     = await ReadFieldAsync("supportsWalkVR", "SupportsWalkVR");
        return await ApplyMutation(playlistId, p =>
        {
            if (bool.TryParse(juniors,  out var j)) p.SupportsJuniors    = j;
            if (bool.TryParse(screens,  out var c)) p.SupportsScreens    = c;
            if (bool.TryParse(teleport, out var t)) p.SupportsTeleportVR = t;
            if (bool.TryParse(walk,     out var w)) p.SupportsWalkVR     = w;
        });
    }

    [HttpPost("/playlists/{playlistId:long}/warning")]
    [HttpPut("/playlists/{playlistId:long}/warning")]
    [Authorize]
    public async Task<IActionResult> PlaylistWarning(long playlistId)
    {
        var mask   = await ReadFieldAsync("warningMask", "WarningMask");
        var custom = await ReadFieldAsync("customWarning", "CustomWarning");
        return await ApplyMutation(playlistId, p =>
        {
            if (int.TryParse(mask, out var m)) p.WarningMask = m;
            if (custom is not null) p.CustomWarning = custom.Length > 512 ? custom[..512] : custom;
        });
    }

    /// <summary>First value present under any of the given names, looking at
    /// the form, the query string, then a JSON body. Null means "not sent",
    /// which every caller treats as "leave this field alone".</summary>
    private async Task<string?> ReadFieldAsync(params string[] names)
    {
        var all = await ReadFieldsAsync(names);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Every value present under any of the given names — the client
    /// repeats a field name to send a list.</summary>
    private async Task<List<string>> ReadFieldsAsync(params string[] names)
    {
        var found = new List<string>();

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var n in names)
                foreach (var v in form[n])
                    if (!string.IsNullOrWhiteSpace(v)) found.Add(v!);
        }
        foreach (var n in names)
            foreach (var v in Request.Query[n])
                if (!string.IsNullOrWhiteSpace(v)) found.Add(v!);
        if (found.Count > 0) return found;

        if (!Request.HasFormContentType)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(Request.Body);
                Request.Body.Position = 0;
                var root = doc.RootElement;

                // The tags endpoint sends a bare JSON array of strings.
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in root.EnumerateArray())
                        if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                            found.Add(el.GetString()!);
                    return found;
                }

                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var n in names)
                    {
                        if (!root.TryGetProperty(n, out var v)) continue;
                        if (v.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var el in v.EnumerateArray()) found.Add(el.ToString());
                        }
                        else if (v.ValueKind != System.Text.Json.JsonValueKind.Null)
                        {
                            found.Add(v.ToString());
                        }
                        if (found.Count > 0) break;
                    }
                }
            }
            catch (System.Text.Json.JsonException) { /* no usable body */ }
        }
        return found;
    }

    private async Task<IActionResult> ApplyMutation(long playlistId, Action<PlaylistEntity> mutator)
    {
        try
        {
            var updated = await playlists.ModifyAsync(playlistId, Me, mutator);
            if (updated is null) return NotFound();
            return await BuildDetailsResponseAsync(playlistId);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ── Member-room mutations ────────────────────────────────────────

    [HttpPut("/playlists/{playlistId:long}/rooms/{roomId:long}")]
    [HttpPost("/playlists/{playlistId:long}/rooms/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> AddRoom(long playlistId, long roomId)
    {
        try
        {
            if (!await playlists.AddRoomAsync(playlistId, roomId, Me)) return NotFound();
            return await BuildDetailsResponseAsync(playlistId);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpDelete("/playlists/{playlistId:long}/rooms/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> RemoveRoom(long playlistId, long roomId)
    {
        try
        {
            if (!await playlists.RemoveRoomAsync(playlistId, roomId, Me)) return NotFound();
            return await BuildDetailsResponseAsync(playlistId);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // ── Cheer / favorite toggles ─────────────────────────────────────

    [HttpPost("/playlists/{playlistId:long}/interactionby/me/cheer")]
    [HttpPut("/playlists/{playlistId:long}/interactionby/me/cheer")]
    [Authorize]
    public async Task<IActionResult> Cheer(long playlistId)
    {
        await playlists.SetInteractionAsync(playlistId, Me, cheered: true);
        return Ok(new { success = true });
    }

    [HttpDelete("/playlists/{playlistId:long}/interactionby/me/cheer")]
    [Authorize]
    public async Task<IActionResult> Uncheer(long playlistId)
    {
        await playlists.SetInteractionAsync(playlistId, Me, cheered: false);
        return Ok(new { success = true });
    }

    [HttpPost("/playlists/{playlistId:long}/interactionby/me/favorite")]
    [HttpPut("/playlists/{playlistId:long}/interactionby/me/favorite")]
    [Authorize]
    public async Task<IActionResult> Favorite(long playlistId)
    {
        await playlists.SetInteractionAsync(playlistId, Me, favorited: true);
        return Ok(new { success = true });
    }

    [HttpDelete("/playlists/{playlistId:long}/interactionby/me/favorite")]
    [Authorize]
    public async Task<IActionResult> Unfavorite(long playlistId)
    {
        await playlists.SetInteractionAsync(playlistId, Me, favorited: false);
        return Ok(new { success = true });
    }

    // ── My-playlists tabs (bare-list KMKPEOGJDFK) ───────────────────

    [HttpGet("/playlists/createdby/me")]
    [Authorize]
    public async Task<IActionResult> CreatedByMe()
    {
        var rows = await playlists.CreatedByAsync(Me);
        return Ok(rows.Select(RoomsController.BuildPlaylistUnionEntry).ToList());
    }

    [HttpGet("/playlists/cheeredby/me")]
    [Authorize]
    public async Task<IActionResult> CheeredByMe()
    {
        var rows = await playlists.CheeredByAsync(Me);
        return Ok(rows.Select(RoomsController.BuildPlaylistUnionEntry).ToList());
    }

    [HttpGet("/playlists/favoritedby/me")]
    [Authorize]
    public async Task<IActionResult> FavoritedByMe()
    {
        var rows = await playlists.FavoritedByAsync(Me);
        return Ok(rows.Select(RoomsController.BuildPlaylistUnionEntry).ToList());
    }

    /// <summary>GET <c>/playlists/visitedby/me</c> — we don't track
    /// per-player playlist visits yet (visits are tracked at the Room
    /// level via RoomVisitEntity). Return empty so the watch's
    /// playlists tab renders cleanly; populated once we add a
    /// PlaylistVisitEntity or derive from member-room visits.</summary>
    [HttpGet("/playlists/visitedby/me")]
    [Authorize]
    public IActionResult VisitedByMe() => Ok(Array.Empty<object>());

    // ── Bulk lookup ──────────────────────────────────────────────────

    /// <summary>GET <c>/playlists/bulk?id=A&amp;id=B</c> — bulk-by-id
    /// lookup for the watch's playlist cache warmup. Bare list of
    /// KMKPEOGJDFK entries, one per resolved id (missing ids
    /// silently dropped).</summary>
    [HttpGet("/playlists/bulk")]
    public async Task<IActionResult> Bulk()
    {
        var ids = Request.Query["id"].Concat(Request.Query["ids"])
            .SelectMany(v => (v ?? string.Empty).Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => long.TryParse(s, out var n) ? n : 0)
            .Where(n => n != 0)
            .ToList();
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await playlists.BulkAsync(ids);
        return Ok(rows.Select(RoomsController.BuildPlaylistUnionEntry).ToList());
    }

    // ── Detail-response builder (BMFAGMFKODA shape) ─────────────────

    /// <summary>
    /// BMFAGMFKODA wire shape: KMKPEOGJDFK union entry + Rooms list
    /// (List&lt;KLCOGEIGEBJ&gt;) + Tags list (List&lt;DPHPFLGAICI&gt;).
    /// Mirrors the inline logic in <see cref="RoomsController.PlaylistDetails"/>
    /// so the BARE-PATH mutation routes here return the same shape the
    /// watch's <c>PlaylistDetails.Deserialize</c> reads.
    /// </summary>
    private async Task<IActionResult> BuildDetailsResponseAsync(long playlistId)
    {
        var p = await playlists.GetByIdAsync(playlistId);
        if (p is null) return NotFound();

        var roomIds = await playlists.RoomIdsAsync(playlistId);
        List<RoomEntity> memberRooms = new();
        Dictionary<long, IReadOnlyList<RoomSceneEntity>> scenesByRoom = new();
        if (roomIds.Count > 0)
        {
            var roomRows = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToListAsync();
            var byId = roomRows.ToDictionary(r => r.Id);
            memberRooms = roomIds
                .Select(rid => byId.TryGetValue(rid, out var r) ? r : null)
                .Where(r => r is not null).Select(r => r!).ToList();

            var sceneRows = await db.RoomScenes
                .Where(s => roomIds.Contains(s.RoomId))
                .OrderBy(s => s.OrderIndex).ToListAsync();
            scenesByRoom = sceneRows.GroupBy(s => s.RoomId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());
        }

        var roomsWire = memberRooms
            .Select(r => RoomsController.BuildRoomServerDetails(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList();

        var tagsWire = string.IsNullOrWhiteSpace(p.TagsCsv)
            ? new List<object>()
            : p.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => (object)new { Type = 0, Tag = t })
                .ToList();

        var result = RoomsController.BuildPlaylistUnionEntry(p);
        result["Rooms"] = roomsWire;
        result["Tags"] = tagsWire;
        return Ok(result);
    }
}
