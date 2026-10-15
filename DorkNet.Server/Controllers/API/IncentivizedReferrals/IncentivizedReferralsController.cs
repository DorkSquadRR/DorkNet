using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.IncentivizedReferrals;

[ApiController]
[Authorize]
[Route("api/incentivizedreferrals")]
public class IncentivizedReferralsController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("")]
    [HttpGet("progress")]
    public async Task<IActionResult> Progress()
    {
        var referralCount = await db.PlayerSettings
            .CountAsync(s => s.Key.StartsWith($"referral:credited:{Me}:"));
        var claimed = await db.PlayerSettings
            .AnyAsync(s => s.PlayerId == Me && s.Key == "referral:reward_claimed");
        return Ok(new
        {
            ReferralCount = referralCount,
            RequiredReferralCount = 1,
            CanClaim = referralCount >= 1 && !claimed,
            Claimed = claimed,
            RewardCurrencyType = 2,
            RewardCurrency = 250,
        });
    }

    /// <summary>The caller's referred players, paged.
    ///
    /// <c>FGLDKEJLAKB&lt;GPJIMAHMAIO&gt; EGEEFJHAGAM(Int32? take, String continuationToken)</c>
    /// (RecNet.Runtime/LINKIECPDML.txt:337) builds this through the URL builder
    /// LPPEFJMCJFN ("api/incentivizedreferrals/" + "referrals", :677-701, with
    /// <c>take</c> and <c>continuationToken</c> query params at :716/:737) — which
    /// is why the plain route-literal sweep never saw it — and issues a GET
    /// (:164-170). Reply formatter KOEEACLMAFE.txt:203-238:
    /// <c>{Referrals: [...], ContinuationToken: string}</c>; each entry
    /// (ADAFNGNADFH.txt:279-346) is <c>{InviteeAccountId: Int32, CreatedAt:
    /// DateTime, VerifiedAt: DateTime?}</c>.
    ///
    /// A referral is recorded when a brand-new player redeems the caller's
    /// referral code (LinkController.CreditReferralAsync), and that redemption
    /// is what counts it, so it is verified at the moment it is created. The
    /// continuation token is the next offset; null once the list is exhausted.</summary>
    [HttpGet("referrals")]
    public async Task<IActionResult> Referrals([FromQuery] int? take, [FromQuery] string? continuationToken)
    {
        var size = Math.Clamp(take ?? 20, 1, 100);
        var offset = int.TryParse(continuationToken, out var parsed) && parsed > 0 ? parsed : 0;
        var prefix = $"referral:credited:{Me}:";

        var rows = await db.PlayerSettings
            .Where(s => s.Key.StartsWith(prefix))
            .OrderByDescending(s => s.Id)
            .Skip(offset)
            .Take(size + 1)
            .Select(s => new { s.PlayerId, s.Value })
            .ToListAsync();

        var page = rows.Take(size).Select(r =>
        {
            var at = DateTime.TryParse(r.Value, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var credited)
                ? credited
                : DateTime.UnixEpoch;
            return new
            {
                InviteeAccountId = (int)r.PlayerId,
                CreatedAt = at,
                VerifiedAt = (DateTime?)at,
            };
        }).ToList();

        return Ok(new
        {
            Referrals = page,
            ContinuationToken = rows.Count > size ? (offset + size).ToString() : null,
        });
    }

    // Client contract: the claim response deserializes as a top-level
    // List<RewardSelection> where each entry is
    // { rewardSelectionId: long, rewardType: string } (RecNet.Runtime
    // AFEFBIKADAP/GLOFCEJBIGB). Returning a {Success,...} object crashes the
    // strict array reader. When there's nothing to grant, return an empty
    // array — a valid empty list, not an error object.
    [HttpPost("claim")]
    public async Task<IActionResult> Claim()
    {
        var referralCount = await db.PlayerSettings
            .CountAsync(s => s.Key.StartsWith($"referral:credited:{Me}:"));
        var alreadyClaimed = await db.PlayerSettings
            .AnyAsync(s => s.PlayerId == Me && s.Key == "referral:reward_claimed");
        if (referralCount < 1 || alreadyClaimed)
            return Ok(Array.Empty<object>());

        await level.GrantCurrencyAsync(Me, 2, 250, "incentivized_referral");
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = "referral:reward_claimed",
            Value = DateTime.UtcNow.ToString("O"),
        });
        await db.SaveChangesAsync();

        // One granted reward selection: 250 of currency type 2.
        return Ok(new[]
        {
            new { rewardSelectionId = unchecked(Me * 397L + 2), rewardType = "Currency" },
        });
    }
}
