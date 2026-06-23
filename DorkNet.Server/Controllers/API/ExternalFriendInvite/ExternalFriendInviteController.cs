using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.ExternalFriendInvite;

[ApiController]
[Authorize]
public class ExternalFriendInviteController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpPost("api/externalfriendinvite/v1/createplatforminvite")]
    public async Task<IActionResult> CreatePlatformInvite([FromForm] int platform = 0, [FromForm] string? platformId = null)
    {
        var code = Guid.NewGuid().ToString("N")[..10];
        var key = $"externalinvite:platform:{code}";
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = key,
            Value = $"{platform}|{platformId ?? string.Empty}|{DateTime.UtcNow:O}",
        });
        await db.SaveChangesAsync();
        var url = $"{Request.Scheme}://{Request.Host}/invite/{code}";
        return Ok(new { InviteCode = code, InviteUrl = url, Platform = platform, PlatformId = platformId ?? string.Empty });
    }

    [HttpPost("api/externalfriendinvite/v1/sendtextmessageinvite")]
    public async Task<IActionResult> SendTextMessageInvite([FromForm] string? phoneNumber, [FromForm] string? message)
    {
        var code = Guid.NewGuid().ToString("N")[..10];
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = $"externalinvite:text:{code}",
            Value = $"{phoneNumber ?? string.Empty}|{message ?? string.Empty}|{DateTime.UtcNow:O}",
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, InviteCode = code, PhoneNumber = phoneNumber ?? string.Empty });
    }

    [HttpGet("api/externalfriendinvite/v1/getplatformreferrers")]
    [HttpPost("api/externalfriendinvite/v1/getplatformreferrers")]
    public async Task<IActionResult> GetPlatformReferrers()
    {
        var rows = await db.PlayerSettings
            .Where(s => s.PlayerId == Me && s.Key.StartsWith("externalinvite:platform:"))
            .OrderByDescending(s => s.Id)
            .ToListAsync();
        return Ok(rows.Select(ToInviteWire));
    }

    [HttpGet("api/externalfriendinvite/v1/gettextmessagereferrers")]
    [HttpPost("api/externalfriendinvite/v1/gettextmessagereferrers")]
    public async Task<IActionResult> GetTextMessageReferrers()
    {
        var rows = await db.PlayerSettings
            .Where(s => s.PlayerId == Me && s.Key.StartsWith("externalinvite:text:"))
            .OrderByDescending(s => s.Id)
            .ToListAsync();
        return Ok(rows.Select(ToInviteWire));
    }

    private static object ToInviteWire(PlayerSettingEntity row)
    {
        var parts = row.Value.Split('|');
        return new
        {
            InviteCode = row.Key.Split(':').Last(),
            Kind = row.Key.Contains(":text:") ? "text" : "platform",
            Value = parts.FirstOrDefault() ?? string.Empty,
            CreatedAt = parts.LastOrDefault() ?? string.Empty,
        };
    }
}
