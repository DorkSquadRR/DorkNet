using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Avatar.V2;

/// <summary>
/// api.rec.net/api/avatar/v2 — older Avatar settings endpoint used by
/// the 2020 client (the V3 endpoint is for newer builds and uses a
/// different shape entirely).
///
/// Wire shape verified by disassembling RecNet.Avatar.Deserialize at
/// RVA 0xFA3C30. Required keys (Util.GetKey&lt;string&gt;): OutfitSelections,
/// HairColor, SkinColor, FaceFeatures. Empty strings satisfy the
/// deserializer; the client interprets them as "use defaults" and falls
/// back to its built-in starter avatar.
///
/// This implementation persists each set/get to <see cref="AvatarEntity"/>
/// instead of stubbing — round-trip across sessions, multi-user-safe
/// (one row per player keyed by PlayerId).
/// </summary>
[ApiController]
[Authorize]
public class AvatarV2Controller(DorkNetDbContext db, ILogger<AvatarV2Controller> logger) : ControllerBase
{
    [HttpGet("api/avatar/v2")]
    public async Task<ActionResult<AvatarV2Dto>> GetAvatar()
    {
        var pid = this.RequireCurrentPlayerId();
        var a = await db.Avatars.FirstOrDefaultAsync(x => x.PlayerId == pid);
        return Ok(ToDto(a));
    }

    /// <summary>
    /// POST /api/avatar/v2/set — replace the player's avatar settings.
    ///
    /// CRITICAL: do NOT use <c>[FromBody] AvatarV2Dto</c> here. The
    /// watch's <c>RecNet.Avatar.Serialize()</c> emits each of
    /// OutfitSelections / HairColor / SkinColor / FaceFeatures as
    /// dictionary values whose runtime types are determined by the
    /// IRecNetObject serializer it uses — FaceFeatures in particular
    /// sometimes ships as a nested JSON OBJECT instead of a JSON
    /// string, and the watch may also omit fields entirely when
    /// they're empty.
    ///
    /// System.Text.Json's strict type binder responds to either of those
    /// with a 400 (model state invalid → ApiController auto-400) and
    /// the watch then retries every frame, accumulating "Failed to
    /// upload Rec Room player avatar changes: HTTP Error 400" errors
    /// (output_log.txt:852+, 1000+ repeats) until the player object
    /// gets destroyed.
    ///
    /// Parse the body manually as JsonElement so we accept any shape
    /// the watch sends: string-or-object for FaceFeatures (re-encode
    /// the object as a string before persisting), missing fields (keep
    /// existing values), null values (treat as empty).
    /// </summary>
    [HttpPost("api/avatar/v2")]
    [HttpPost("api/avatar/v2/set")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "text/plain")]
    public async Task<ActionResult<AvatarV2Dto>> SetAvatar()
    {
        var pid = this.RequireCurrentPlayerId();

        // Read the raw body so we control parsing — bypasses the
        // model binder's strict type matching that was causing 400s.
        string raw;
        using (var reader = new System.IO.StreamReader(Request.Body))
            raw = await reader.ReadToEndAsync();

        string? outfitSelections = null;
        string? hairColor = null;
        string? skinColor = null;
        string? faceFeatures = null;
        string? outfitSelectionsV2 = null;
        string? customAvatarItems = null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    outfitSelections = ReadStringField(root, "OutfitSelections");
                    hairColor        = ReadStringField(root, "HairColor");
                    skinColor        = ReadStringField(root, "SkinColor");
                    faceFeatures     = ReadStringField(root, "FaceFeatures");
                    // 2023 client (HHDLNAPEMGP writer EKHJKDNHOPB) also
                    // uploads OutfitSelectionsV2 + CustomAvatarItems —
                    // persist them so remote-player fetches round-trip.
                    outfitSelectionsV2 = ReadStringField(root, "OutfitSelectionsV2");
                    customAvatarItems  = ReadStringField(root, "CustomAvatarItems");
                }
            }
            catch (JsonException ex)
            {
                // Fall through with all-null updates if the body isn't
                // JSON. Log so we can see what the watch actually sends.
                logger.LogWarning(
                    "[avatar-set] could not parse body as JSON ({Msg}); raw={Raw}",
                    ex.Message, raw.Length > 500 ? raw.Substring(0, 500) + "…" : raw);
            }
        }

        var a = await db.Avatars.FirstOrDefaultAsync(x => x.PlayerId == pid);
        if (a is null)
        {
            a = new AvatarEntity { PlayerId = pid };
            db.Avatars.Add(a);
        }
        // Only overwrite fields the watch actually sent; missing keys
        // keep the previously-stored value so a partial PATCH-style
        // update doesn't accidentally wipe HairColor/SkinColor/etc.
        if (outfitSelections is not null) a.OutfitSelections = outfitSelections;
        if (hairColor        is not null) a.HairColor        = hairColor;
        if (skinColor        is not null) a.SkinColor        = skinColor;
        if (faceFeatures     is not null) a.FaceFeatures     = faceFeatures;
        if (outfitSelectionsV2 is not null) a.OutfitSelectionsV2 = outfitSelectionsV2;
        if (customAvatarItems is not null)
        {
            // CustomAvatarItems arrives as a JSON ARRAY of
            // {"CustomAvatarItemId":guid,"BodyPart":byte}; ReadStringField
            // re-encodes arrays as raw JSON. Guard against non-array junk.
            a.CustomAvatarItemsJson = customAvatarItems.TrimStart().StartsWith('[')
                ? customAvatarItems
                : "[]";
        }
        a.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToDto(a));
    }

    /// <summary>Read a field that should be a string but may arrive
    /// as <c>null</c>, a JSON object/array (re-encoded back to a JSON
    /// string), or be missing entirely (return null → caller keeps
    /// the existing DB value).</summary>
    private static string? ReadStringField(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String           => v.GetString() ?? string.Empty,
            JsonValueKind.Null             => string.Empty,
            JsonValueKind.Object           => v.GetRawText(),
            JsonValueKind.Array            => v.GetRawText(),
            JsonValueKind.Number           => v.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            _                              => null,
        };
    }

    /// <summary>GET <c>api/avatar/v2/{accountId}</c> — the 2023 client's
    /// remote-player avatar settings fetch. Decompile evidence
    /// (2023.03.21 ISIL, RecNet.Runtime FFFIMAGLKEG.txt:4845-5006,
    /// method KAFJNMDBKDB): URL is String.Format("{0}v2/{1}",
    /// "api/avatar/", accountId) — the account id is a bare path segment
    /// directly under v2 (NOT under /equipped/). The response is parsed
    /// as object HHDLNAPEMGP whose reader (EKHJKDNHOPB.txt:471-766)
    /// registers OutfitSelections / OutfitSelectionsV2 / FaceFeatures /
    /// SkinColor / HairColor / CustomAvatarItems. Without this route the
    /// request 404'd with an empty body → "Failed to load Rec Room
    /// remote player avatar settings" (Player.log:2597).</summary>
    [HttpGet("api/avatar/v2/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<AvatarV2Dto>> GetSettingsFor(long playerId)
    {
        var a = await db.Avatars.FirstOrDefaultAsync(x => x.PlayerId == playerId);
        return Ok(ToDto(a));
    }

    /// <summary>Public lookup of another player's equipped avatar — the
    /// client uses this to render remote players' avatars in shared
    /// rooms. No auth requirement on read so anonymous calls in the
    /// boot path don't 401.</summary>
    [HttpGet("api/avatar/v2/equipped/{playerId:long}")]
    [HttpGet("api/avatar/v3/equipped/{playerId:long}")]
    [HttpGet("api/avatar/v4/equipped/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult<AvatarV2Dto>> GetEquippedFor(long playerId)
    {
        var a = await db.Avatars.FirstOrDefaultAsync(x => x.PlayerId == playerId);
        return Ok(ToDto(a));
    }

    /// <summary>Bare <c>api/avatar/v{2,3}/equipped</c> returns the
    /// caller's own equipped avatar. Without the bare path the watch's
    /// "what am I wearing" query falls through to the catch-all and
    /// gets <c>[]</c>.</summary>
    [HttpGet("api/avatar/v2/equipped")]
    [HttpGet("api/avatar/v3/equipped")]
    [HttpGet("api/avatar/v4/equipped")]
    public async Task<ActionResult<AvatarV2Dto>> GetEquippedSelf()
    {
        var pid = this.RequireCurrentPlayerId();
        var a = await db.Avatars.FirstOrDefaultAsync(x => x.PlayerId == pid);
        return Ok(ToDto(a));
    }

    private static AvatarV2Dto ToDto(AvatarEntity? a) => new()
    {
        OutfitSelections = a?.OutfitSelections ?? string.Empty,
        FaceFeatures = a?.FaceFeatures ?? string.Empty,
        HairColor = a?.HairColor ?? string.Empty,
        SkinColor = a?.SkinColor ?? string.Empty,
        OutfitSelectionsV2 = a?.OutfitSelectionsV2 ?? string.Empty,
        CustomAvatarItems = ParseCustomAvatarItems(a?.CustomAvatarItemsJson),
    };

    /// <summary>Re-hydrate the stored CustomAvatarItems JSON into typed
    /// wire items so the response always carries a well-formed array
    /// (never a raw string). Bad/legacy blobs degrade to [].</summary>
    private static List<CustomAvatarItemRefDto> ParseCustomAvatarItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<CustomAvatarItemRefDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

public class AvatarV2Dto
{
    [JsonPropertyName("OutfitSelections")]
    public string OutfitSelections { get; set; } = string.Empty;

    [JsonPropertyName("HairColor")]
    public string HairColor { get; set; } = string.Empty;

    [JsonPropertyName("SkinColor")]
    public string SkinColor { get; set; } = string.Empty;

    [JsonPropertyName("FaceFeatures")]
    public string FaceFeatures { get; set; } = string.Empty;

    /// <summary>2023 client field — HHDLNAPEMGP reader key
    /// "OutfitSelectionsV2" (EKHJKDNHOPB.txt:498-514). Extra keys are
    /// skipped by the 2020 reader, so this is safe cross-version.</summary>
    [JsonPropertyName("OutfitSelectionsV2")]
    public string OutfitSelectionsV2 { get; set; } = string.Empty;

    /// <summary>2023 client field — HHDLNAPEMGP reader key
    /// "CustomAvatarItems" (EKHJKDNHOPB.txt:594-610), a list of
    /// BGNNOMBFMLH items.</summary>
    [JsonPropertyName("CustomAvatarItems")]
    public List<CustomAvatarItemRefDto> CustomAvatarItems { get; set; } = new();
}

/// <summary>BGNNOMBFMLH — { Guid CustomAvatarItemId, Byte BodyPart }
/// (property types BGNNOMBFMLH.txt:3-39; JSON keys registered by reader
/// MKAIABAJIAJ.txt:215-258).</summary>
public class CustomAvatarItemRefDto
{
    [JsonPropertyName("CustomAvatarItemId")]
    public Guid CustomAvatarItemId { get; set; }

    [JsonPropertyName("BodyPart")]
    public byte BodyPart { get; set; }
}
