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

    /// <summary>Key prefix for "player X redeemed one of player Y's invites".
    /// Row layout is <c>PlayerId = the invitee</c>,
    /// <c>Key = externalinvite:redeemed:{kind}:{inviterAccountId}</c>,
    /// <c>Value = redemption timestamp</c>. The referrer endpoints below read
    /// these rows; the invite-claim path is what writes them.</summary>
    private const string RedeemedPrefix = "externalinvite:redeemed:";

    /// <summary>Records an out-of-game friend invite aimed at a platform (Steam,
    /// Oculus, …) account.
    ///
    /// The client only ever sends the single form field <c>platformId</c>
    /// (RecNet.Runtime/OMHDDLFIPLP.txt:876) — <c>platform</c> keeps its default
    /// so the older callers that do send it still bind.</summary>
    [HttpPost("api/externalfriendinvite/v1/createplatforminvite")]
    public async Task<IActionResult> CreatePlatformInvite([FromForm] int platform = 0, [FromForm] string? platformId = null)
    {
        var code = NewInviteCode();
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = $"externalinvite:platform:{code}",
            Value = $"{platform}|{platformId ?? string.Empty}|{DateTime.UtcNow:O}",
        });
        await db.SaveChangesAsync();
        var url = $"{Request.Scheme}://{Request.Host}/invite/{code}";
        var result = Success();
        result["InviteCode"] = code;
        result["InviteUrl"] = url;
        result["Platform"] = platform;
        result["PlatformId"] = platformId ?? string.Empty;
        return Ok(result);
    }

    /// <summary>Records an SMS friend invite.
    ///
    /// Form fields are <c>phoneNumber</c>, <c>friendCode</c> and
    /// <c>senderName</c> (OMHDDLFIPLP.txt:497/505/515) — there is no "message"
    /// field, so the friend code that the text is supposed to carry has to be
    /// persisted from <c>friendCode</c> or it is lost.</summary>
    [HttpPost("api/externalfriendinvite/v1/sendtextmessageinvite")]
    public async Task<IActionResult> SendTextMessageInvite(
        [FromForm] string? phoneNumber,
        [FromForm] string? friendCode,
        [FromForm] string? senderName)
    {
        var code = NewInviteCode();
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = $"externalinvite:text:{code}",
            Value = $"{phoneNumber ?? string.Empty}|{friendCode ?? string.Empty}|{senderName ?? string.Empty}|{DateTime.UtcNow:O}",
        });
        await db.SaveChangesAsync();
        var result = Success();
        result["InviteCode"] = code;
        result["PhoneNumber"] = phoneNumber ?? string.Empty;
        return Ok(result);
    }

    /// <summary>Ids of the accounts that referred the caller through a platform
    /// invite.
    ///
    /// The response is a bare array of account ids — the issuing method is
    /// <c>FGLDKEJLAKB&lt;List&lt;System.Int32&gt;&gt; POJMMCCLNCB()</c>
    /// (RecNet.Runtime/OMHDDLFIPLP.txt:923-996, body-less POST). Returning
    /// objects (<c>{InviteCode, Kind, Value, CreatedAt}</c>) made the strict
    /// Int32 reader throw as soon as any row existed, which faulted
    /// SessionManager.CheckForAndHandlePlatformFriendOrTextMessageReferrers
    /// (SessionManager.txt:31439) during the login bootstrap.</summary>
    [HttpGet("api/externalfriendinvite/v1/getplatformreferrers")]
    [HttpPost("api/externalfriendinvite/v1/getplatformreferrers")]
    public async Task<IActionResult> GetPlatformReferrers()
        => Ok(await ReferrerIdsAsync("platform"));

    /// <summary>Same bare-id-array contract as
    /// <see cref="GetPlatformReferrers"/> (OMHDDLFIPLP.txt:998-1071), for
    /// text-message invites.</summary>
    [HttpGet("api/externalfriendinvite/v1/gettextmessagereferrers")]
    [HttpPost("api/externalfriendinvite/v1/gettextmessagereferrers")]
    public async Task<IActionResult> GetTextMessageReferrers()
        => Ok(await ReferrerIdsAsync("text"));

    /// <summary>Account ids recorded as having referred the caller with an
    /// invite of the given kind. The inviter id is the trailing key segment of
    /// the redemption row, so no id can leak in from an invite's own payload
    /// (a numeric <c>platformId</c> used to be misread as an account id).</summary>
    private async Task<List<int>> ReferrerIdsAsync(string kind)
    {
        var prefix = RedeemedPrefix + kind + ":";
        var keys = await db.PlayerSettings
            .Where(s => s.PlayerId == Me && s.Key.StartsWith(prefix))
            .OrderByDescending(s => s.Id)
            .Select(s => s.Key)
            .ToListAsync();

        return keys
            .Select(k => int.TryParse(k[prefix.Length..], out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private static string NewInviteCode() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>Both write routes await <c>FGLDKEJLAKB&lt;DJMHAFPGLLN&gt;</c> and
    /// hand it to <c>BFDGEKKNAPB.ExpandRecNetResult</c>, whose lambda pops the
    /// <c>Error</c> string as an error toast whenever <c>Success</c> is false
    /// (BFDGEKKNAPB_NestedType___c.txt:61 &lt;ExpandRecNetResult&gt;b__2_0).
    /// Utf8Json leaves absent members at their default, so a response without
    /// these two keys reads as Success=false and every successful invite raised
    /// an empty error toast. Key names are literal from the generated formatter
    /// (LMCCNLLHLCJ.txt:191 "Success", :210 "Error"; it also accepts the
    /// camelCase aliases at :202/:218, and skips members it does not know, so
    /// the per-route extras below are inert).</summary>
    private static Dictionary<string, object?> Success() => new(StringComparer.Ordinal)
    {
        ["Success"] = true,
        ["Error"] = string.Empty,
    };
}
