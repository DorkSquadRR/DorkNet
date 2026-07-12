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
