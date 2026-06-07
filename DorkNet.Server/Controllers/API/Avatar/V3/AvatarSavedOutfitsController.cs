using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Avatar.V3;

/// <summary>
/// api.rec.net/api/avatar/v{2,3}/saved — saved-outfit slots. Wire
/// type <c>RecNet.SavedOutfit</c> (per agent ISIL extraction):
/// <c>Slot(int), PreviewImageName(string), OutfitSelections(string),
/// FaceFeatures(string), SkinColor(string), HairColor(string)</c>.
///
/// State persists in <see cref="AvatarEntity.SavedOutfitsJson"/> as
/// a JSON list of <see cref="SavedOutfitDto"/> records keyed by
/// Slot. Caps at 20 entries per player to keep the column under
/// the SQLite text-blob comfort zone.
/// </summary>
[ApiController]
[Authorize]
public class AvatarSavedOutfitsController(DorkNetDbContext db) : ControllerBase
{
    private const int MaxSlots = 20;

    [HttpGet("api/avatar/v1/saved")]
    [HttpGet("api/avatar/v2/saved")]
    [HttpGet("api/avatar/v3/saved")]
    [HttpGet("api/avatar/v4/saved")]
    public async Task<IActionResult> List()
    {
        var pid = this.RequireCurrentPlayerId();
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == pid);
        var list = ParseList(avatar?.SavedOutfitsJson);
        return Ok(list);
    }

    /// <summary>POST <c>v3/saved/set</c> — upsert one outfit by Slot.
    /// Body is the SavedOutfit shape directly.</summary>
    [HttpPost("api/avatar/v1/saved/set")]
    [HttpPost("api/avatar/v3/saved/set")]
    [HttpPost("api/avatar/v2/saved/set")]
    public async Task<IActionResult> Set([FromBody] SavedOutfitDto body)
    {
        if (body is null) return BadRequest("missing body");
        var pid = this.RequireCurrentPlayerId();
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == pid);
        if (avatar is null)
        {
            avatar = new AvatarEntity { PlayerId = pid };
            db.Avatars.Add(avatar);
        }
        var list = ParseList(avatar.SavedOutfitsJson);
        var idx = list.FindIndex(o => o.Slot == body.Slot);
        if (idx >= 0) list[idx] = body;
        else
        {
            if (list.Count >= MaxSlots) return BadRequest($"max {MaxSlots} saved outfits per player");
            list.Add(body);
        }
        avatar.SavedOutfitsJson = JsonSerializer.Serialize(list);
        avatar.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(body);
    }

    [HttpDelete("api/avatar/v3/saved/{slot:int}")]
    [HttpDelete("api/avatar/v2/saved/{slot:int}")]
    public async Task<IActionResult> Delete(int slot)
    {
        var pid = this.RequireCurrentPlayerId();
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == pid);
        if (avatar is null) return NotFound();
        var list = ParseList(avatar.SavedOutfitsJson);
        var removed = list.RemoveAll(o => o.Slot == slot);
        if (removed == 0) return NotFound();
        avatar.SavedOutfitsJson = JsonSerializer.Serialize(list);
        avatar.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }

    private static List<SavedOutfitDto> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<SavedOutfitDto>>(json) ?? new();
        }
        catch { return new(); }
    }
}

public sealed class SavedOutfitDto
{
    [JsonPropertyName("Slot")] public int Slot { get; set; }
    [JsonPropertyName("PreviewImageName")] public string PreviewImageName { get; set; } = string.Empty;
    [JsonPropertyName("OutfitSelections")] public string OutfitSelections { get; set; } = string.Empty;
    [JsonPropertyName("FaceFeatures")] public string FaceFeatures { get; set; } = string.Empty;
    [JsonPropertyName("SkinColor")] public string SkinColor { get; set; } = string.Empty;
    [JsonPropertyName("HairColor")] public string HairColor { get; set; } = string.Empty;
}
