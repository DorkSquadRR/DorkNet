using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using System.Text.Json;
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

    [HttpGet("/playersettings")]
    public async Task<ActionResult<List<SettingDto>>> GetMine()
    {
        var accountId = CurrentPlayerId;
        var settings = await db.PlayerSettings
            .Where(s => s.PlayerId == accountId)
            .Select(s => new SettingDto { Key = s.Key, Value = s.Value })
            .ToListAsync();
        return Ok(settings);
    }

    [HttpPut("/playersettings")]
    public async Task<IActionResult> SetMine()
    {
        var (key, value) = await ReadSettingKeyValueAsync();
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new PlayerSettingsResult { Success = false, Error = "missing_key" });

        key = key.Trim();
        value ??= string.Empty;

        await UpsertSettingAsync(CurrentPlayerId, key, value);
        return Ok(new SettingDto { Key = key, Value = value });
    }

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
            await UpsertSettingAsync(accountId, key, value, save: false);

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

    private async Task UpsertSettingAsync(long accountId, string key, string value, bool save = true)
    {
        var existing = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == accountId && s.Key == key);
        if (existing is null)
            db.PlayerSettings.Add(new PlayerSettingEntity { PlayerId = accountId, Key = key, Value = value });
        else
            existing.Value = value;

        if (save) await db.SaveChangesAsync();
    }

    private async Task<(string? Key, string? Value)> ReadSettingKeyValueAsync()
    {
        string? key = FirstValue(Request.Query, "key", "Key");
        string? value = FirstValue(Request.Query, "value", "Value");

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            key ??= FirstValue(form, "key", "Key");
            value ??= FirstValue(form, "value", "Value");
        }

        if (key is null && Request.ContentLength.GetValueOrDefault() > 0)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(Request.Body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    key = FirstJsonString(document.RootElement, "key", "Key");
                    value = FirstJsonString(document.RootElement, "value", "Value");
                }
            }
            catch (JsonException)
            {
                // Non-JSON bodies are handled by form/query parsing above.
            }
        }

        return (key, value);
    }

    private static string? FirstValue(IQueryCollection values, params string[] names)
    {
        foreach (var name in names)
            if (values.TryGetValue(name, out var value))
                return value.ToString();
        return null;
    }

    private static string? FirstValue(IFormCollection values, params string[] names)
    {
        foreach (var name in names)
            if (values.TryGetValue(name, out var value))
                return value.ToString();
        return null;
    }

    private static string? FirstJsonString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.TryGetProperty(name, out var prop))
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        return null;
    }
}

public class SettingDto
{
    [JsonPropertyName("Key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;
}

public class PlayerSettingsResult
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Error")]
    public string Error { get; set; } = string.Empty;
}
