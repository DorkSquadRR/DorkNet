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

    /// <summary>Accounts who joined via this player's platform invites.
    ///
    /// The response is a bare array of account ids — the issuing method is
    /// <c>FGLDKEJLAKB&lt;List&lt;System.Int32&gt;&gt; POJMMCCLNCB()</c>
    /// (RecNet.Runtime/OMHDDLFIPLP.txt:923). Returning objects
    /// (<c>{InviteCode, Kind, Value, CreatedAt}</c>) made Json.NET throw as soon
    /// as any referrer existed, so the referral screen broke for exactly the
    /// players who had referred someone.</summary>
    [HttpGet("api/externalfriendinvite/v1/getplatformreferrers")]
    [HttpPost("api/externalfriendinvite/v1/getplatformreferrers")]
    public async Task<IActionResult> GetPlatformReferrers()
        => Ok(await ReferrerIdsAsync("externalinvite:platform:"));

    /// <summary>Same bare-id-array contract as
    /// <see cref="GetPlatformReferrers"/>, for text-message invites.</summary>
    [HttpGet("api/externalfriendinvite/v1/gettextmessagereferrers")]
    [HttpPost("api/externalfriendinvite/v1/gettextmessagereferrers")]
    public async Task<IActionResult> GetTextMessageReferrers()
        => Ok(await ReferrerIdsAsync("externalinvite:text:"));

    /// <summary>Account ids recorded against the caller's invites of the given
    /// kind. The redeeming account id is stored as the second pipe-delimited
    /// field once an invite is claimed; unclaimed invites contribute nothing.</summary>
    private async Task<List<int>> ReferrerIdsAsync(string keyPrefix)
    {
        var rows = await db.PlayerSettings
            .Where(s => s.PlayerId == Me && s.Key.StartsWith(keyPrefix))
            .OrderByDescending(s => s.Id)
            .Select(s => s.Value)
            .ToListAsync();

        return rows
            .Select(v => v.Split('|'))
            .Select(parts => int.TryParse(parts.ElementAtOrDefault(1), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

}
