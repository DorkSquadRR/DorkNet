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

    [HttpPost("claim")]
    public async Task<IActionResult> Claim()
    {
        var referralCount = await db.PlayerSettings
            .CountAsync(s => s.Key.StartsWith($"referral:credited:{Me}:"));
        if (referralCount < 1)
            return Ok(new { Success = false, Error = "not_enough_referrals" });
        if (await db.PlayerSettings.AnyAsync(s => s.PlayerId == Me && s.Key == "referral:reward_claimed"))
            return Ok(new { Success = false, Error = "already_claimed" });

        var balance = await level.GrantCurrencyAsync(Me, 2, 250, "incentivized_referral");
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = Me,
            Key = "referral:reward_claimed",
            Value = DateTime.UtcNow.ToString("O"),
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Balance = balance, CurrencyType = 2, Currency = 250 });
    }
}
