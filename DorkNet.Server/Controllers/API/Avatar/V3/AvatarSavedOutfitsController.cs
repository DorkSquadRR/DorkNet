using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Controllers.API.Avatar.V2;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Avatar.V3;

/// <summary>
/// api.rec.net/api/avatar/v{2,3}/saved — saved-outfit slots. Client wire
/// type BEPMJICDNDD, whose converter IMMOJHBENFI registers exactly
/// <c>Slot, PreviewImageName, OutfitSelections, OutfitSelectionsV2,
/// FaceFeatures, SkinColor, HairColor, CustomAvatarItems</c>
/// (RecNet.Runtime/IMMOJHBENFI.txt:587-766 reader, :781-910 writer).
/// CustomAvatarItems is a list of BGNNOMBFMLH
/// <c>{CustomAvatarItemId, BodyPart}</c>, the same element the avatar
/// settings (<c>api/avatar/v2</c>) use.
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
    /// Verb ordinal 2 = POST with a raw JSON body of one serialised
    /// BEPMJICDNDD (RecNet.Runtime/FFFIMAGLKEG.txt:4071-4095, NCILAADFJMM).
    ///
    /// Parsed by hand rather than <c>[FromBody] SavedOutfitDto</c>: the old
    /// DTO silently dropped OutfitSelectionsV2 and CustomAvatarItems, and the
    /// 2023 client wears OutfitSelectionsV2 whenever it is non-empty
    /// (BEPMJICDNDD.NLGCNDMLACA, BEPMJICDNDD.txt:237-266, falls back to
    /// OutfitSelections only when V2 is empty). Loading a saved slot
    /// therefore restored a stale V1 selection (or nothing) and lost every
    /// custom shirt in it.</summary>
    [HttpPost("api/avatar/v3/saved/set")]
    [HttpPost("api/avatar/v2/saved/set")]
    public async Task<IActionResult> Set()
    {
        string raw;
        using (var reader = new StreamReader(Request.Body))
            raw = await reader.ReadToEndAsync();

        var body = ParseOutfit(raw);
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
        list.Sort((a, b) => a.Slot.CompareTo(b.Slot));
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

    /// <summary>Read one outfit object, matching keys case-insensitively
    /// (the client reader accepts Pascal/camel/lower spellings, so any of
    /// them is a valid write). String fields tolerate null; a nested
    /// object/array is kept as its raw JSON text, mirroring how
    /// <c>avatar/v2/set</c> treats the same fields. Returns null for a body
    /// that is not a JSON object.</summary>
    internal static SavedOutfitDto? ParseOutfit(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var dto = new SavedOutfitDto();
            foreach (var prop in root.EnumerateObject())
            {
                switch (prop.Name.ToLowerInvariant())
                {
                    case "slot":
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var slot)) dto.Slot = slot;
                        else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out slot)) dto.Slot = slot;
                        break;
                    case "previewimagename": dto.PreviewImageName = AsString(prop.Value); break;
                    case "outfitselections": dto.OutfitSelections = AsString(prop.Value); break;
                    case "outfitselectionsv2": dto.OutfitSelectionsV2 = AsString(prop.Value); break;
                    case "facefeatures": dto.FaceFeatures = AsString(prop.Value); break;
                    case "skincolor": dto.SkinColor = AsString(prop.Value); break;
                    case "haircolor": dto.HairColor = AsString(prop.Value); break;
                    case "customavataritems": dto.CustomAvatarItems = AsCustomItems(prop.Value); break;
                }
            }
            return dto;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => v.GetRawText(),
    };

    /// <summary>BGNNOMBFMLH list. Elements without a parseable guid are
    /// dropped instead of failing the whole save.</summary>
    private static List<CustomAvatarItemRefDto> AsCustomItems(JsonElement v)
    {
        var result = new List<CustomAvatarItemRefDto>();
        if (v.ValueKind != JsonValueKind.Array) return result;
        foreach (var el in v.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            Guid? id = null;
            byte bodyPart = 0;
            foreach (var p in el.EnumerateObject())
            {
                if (p.Name.Equals("CustomAvatarItemId", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(p.Value.GetString(), out var g))
                    id = g;
                else if (p.Name.Equals("BodyPart", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.Number
                    && p.Value.TryGetByte(out var b))
                    bodyPart = b;
            }
            if (id is { } guid) result.Add(new CustomAvatarItemRefDto { CustomAvatarItemId = guid, BodyPart = bodyPart });
        }
        return result;
    }

    private static List<SavedOutfitDto> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var list = JsonSerializer.Deserialize<List<SavedOutfitDto>>(json) ?? new();
            // Rows written before CustomAvatarItems existed carry no key;
            // keep the wire array non-null.
            foreach (var o in list) o.CustomAvatarItems ??= new();
            return list;
        }
        catch { return new(); }
    }
}

public sealed class SavedOutfitDto
{
    [JsonPropertyName("Slot")] public int Slot { get; set; }
    [JsonPropertyName("PreviewImageName")] public string PreviewImageName { get; set; } = string.Empty;
    [JsonPropertyName("OutfitSelections")] public string OutfitSelections { get; set; } = string.Empty;
    /// <summary>The selection the 2023 client actually wears when non-empty
    /// (BEPMJICDNDD.NLGCNDMLACA). Rows saved before this field existed
    /// deserialize to "" and the client falls back to OutfitSelections.</summary>
    [JsonPropertyName("OutfitSelectionsV2")] public string OutfitSelectionsV2 { get; set; } = string.Empty;
    [JsonPropertyName("FaceFeatures")] public string FaceFeatures { get; set; } = string.Empty;
    [JsonPropertyName("SkinColor")] public string SkinColor { get; set; } = string.Empty;
    [JsonPropertyName("HairColor")] public string HairColor { get; set; } = string.Empty;
    /// <summary>Custom shirts worn in this slot. Always an array on the wire
    /// (the client's ctor allocates an empty list, BEPMJICDNDD.txt:321-360).</summary>
    [JsonPropertyName("CustomAvatarItems")] public List<CustomAvatarItemRefDto> CustomAvatarItems { get; set; } = new();
}
