using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using System.Text.Json.Serialization;

namespace DorkNet.Server.Controllers.API.Settings.V2;

/// <summary>
/// api.rec.net/api/settings/v2 — player settings download / set / remove.
/// URL patterns (verified by disassembling RecNet.Settings$$DowloadLocalPlayerSettings
/// at RVA 0xAF55B0 and RecNet.Settings$$StoreLocalPlayerSetting at RVA 0xAF5650):
///
///   GET    api/settings/v2/        — returns List&lt;{Key, Value}&gt;
///   POST   api/settings/v2/set     — body { Key, Value } (set or update one)
///   POST   api/settings/v2/remove  — body { Key } (delete one)
///
/// The CLIENT side parses the GET response with Util.DeserializeList&lt;Setting&gt;
/// and reads each entry's "Key" and "Value" fields (verified by string literal
/// references in the deserializer body — both are PascalCase).
///
/// Returning a Dictionary&lt;string,string&gt; here serializes as "{}"
/// when empty, which the client casts to a List and dies with
/// InvalidCastException: Unable to cast object of type 'Dictionary`2' to
/// type 'List`1'. Always return a real List.
/// </summary>
[ApiController]
[Route("api/[controller]/v2")]
[Authorize]
public class SettingsController(DorkNetDbContext db) : ControllerBase
{
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    [HttpGet]
    [HttpGet("get")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await db.PlayerSettings
            .Where(s => s.PlayerId == CurrentPlayerId)
            .Select(s => new SettingDto { Key = s.Key, Value = s.Value })
            .ToListAsync();
        return Ok(settings);
    }

    [HttpPost("set")]
    public async Task<ActionResult> SetSetting([FromBody] SetSettingRequest req)
    {
        var existing = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == CurrentPlayerId && s.Key == req.Key);

        if (existing is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = CurrentPlayerId,
                Key = req.Key,
                Value = req.Value,
            });
        }
        else
        {
            existing.Value = req.Value;
        }

        await db.SaveChangesAsync();
        return Ok(new SettingDto { Key = req.Key, Value = req.Value });
    }

    [HttpPost("remove")]
    public async Task<ActionResult> RemoveSetting([FromBody] SetSettingRequest req)
    {
        var existing = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == CurrentPlayerId && s.Key == req.Key);
        if (existing is not null)
        {
            db.PlayerSettings.Remove(existing);
            await db.SaveChangesAsync();
        }
        return Ok();
    }
}

public class SettingDto
{
    [JsonPropertyName("Key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;
}

public class SetSettingRequest
{
    [JsonPropertyName("Key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;
}
