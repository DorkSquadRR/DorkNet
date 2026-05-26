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

    [HttpPost("/playlists/{playlistId:long}/name")]
    [HttpPut("/playlists/{playlistId:long}/name")]
    [Authorize]
    public Task<IActionResult> PlaylistName(long playlistId, [FromBody] StringFieldRequest req,
        [FromForm(Name = "Name")] string? formValue) =>
        ApplyMutation(playlistId, p => p.Name = (req?.Value ?? formValue ?? p.Name).Trim());

    [HttpPost("/playlists/{playlistId:long}/description")]
    [HttpPut("/playlists/{playlistId:long}/description")]
    [Authorize]
    public Task<IActionResult> PlaylistDescription(long playlistId, [FromBody] StringFieldRequest req,
        [FromForm(Name = "Description")] string? formValue) =>
        ApplyMutation(playlistId, p => p.Description = req?.Value ?? formValue ?? p.Description);

    [HttpPost("/playlists/{playlistId:long}/image")]
    [HttpPut("/playlists/{playlistId:long}/image")]
    [Authorize]
    public Task<IActionResult> PlaylistImage(long playlistId, [FromBody] StringFieldRequest req,
        [FromForm(Name = "ImageName")] string? formValue) =>
        ApplyMutation(playlistId, p => p.ImageName = req?.Value ?? formValue ?? p.ImageName);

    [HttpPost("/playlists/{playlistId:long}/tags")]
    [HttpPut("/playlists/{playlistId:long}/tags")]
    [Authorize]
    public Task<IActionResult> PlaylistTags(long playlistId, [FromBody] TagsRequest? req,
        [FromForm(Name = "Tags")] string? formValue) =>
        ApplyMutation(playlistId, p =>
        {
            var csv = req?.Tags
                ?? (req?.TagsList is { Count: > 0 } list ? string.Join(',', list) : null)
                ?? formValue
                ?? p.TagsCsv;
            p.TagsCsv = csv;
        });

    /// <summary>Accessibility / visibility / level-voting / restrictions /
    /// warning are persisted on the playlist row even though the 2020
    /// PlaylistEntity doesn't have dedicated columns for each — the wire
    /// shape always reports the union-entry defaults, so for now the
    /// mutation is a no-op acknowledgement that returns the unchanged
    /// playlist. Real per-field columns can be added when we have a
    /// playlist-settings UI to drive them.</summary>
    [HttpPost("/playlists/{playlistId:long}/accessibility")]
    [HttpPost("/playlists/{playlistId:long}/visibility")]
    [HttpPost("/playlists/{playlistId:long}/levelvoting")]
    [HttpPost("/playlists/{playlistId:long}/restrictions")]
    [HttpPost("/playlists/{playlistId:long}/warning")]
    [Authorize]
    public Task<IActionResult> PlaylistAck(long playlistId) =>
        ApplyMutation(playlistId, _ => { /* no-op: field not persisted yet */ });

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
