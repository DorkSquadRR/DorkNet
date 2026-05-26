using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using System.Text.Json.Serialization;

namespace DorkNet.Server.Controllers.PlayerSettings;

/// <summary>
/// playersettings.rec.net — dedicated player settings service.
/// </summary>
[ApiController]
[Authorize]
public class PlayerSettingsController(DorkNetDbContext db) : ControllerBase
{
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    [HttpGet("/settings/v1/{accountId:long}")]
    public async Task<ActionResult<Dictionary<string, string>>> Get(long accountId)
    {
        // Settings are private — only the owner can read them. Returning
        // 403 instead of leaking the dict.
        if (accountId != CurrentPlayerId) return Forbid();

        var settings = await db.PlayerSettings
            .Where(s => s.PlayerId == accountId)
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }

    [HttpPut("/settings/v1/{accountId:long}")]
    public async Task<IActionResult> Set(long accountId, [FromBody] Dictionary<string, string> updates)
    {
        if (accountId != CurrentPlayerId) return Forbid();

        foreach (var (key, value) in updates)
        {
            var existing = await db.PlayerSettings
                .FirstOrDefaultAsync(s => s.PlayerId == accountId && s.Key == key);
            if (existing is null)
                db.PlayerSettings.Add(new PlayerSettingEntity { PlayerId = accountId, Key = key, Value = value });
            else
                existing.Value = value;
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("/settings/v1/{accountId:long}/{key}")]
    public async Task<IActionResult> Delete(long accountId, string key)
    {
        if (accountId != CurrentPlayerId) return Forbid();

        var setting = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == accountId && s.Key == key);
        if (setting is null) return NotFound();

        db.PlayerSettings.Remove(setting);
        await db.SaveChangesAsync();
        return Ok();
    }
}
