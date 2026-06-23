using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.InfluencerPartnerProgram;

[ApiController]
public class InfluencerPartnerProgramController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/influencerpartnerprogram/influencers")]
    [AllowAnonymous]
    public async Task<IActionResult> Influencers([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var creatorIds = await db.Rooms.Select(r => r.CreatorPlayerId)
            .Concat(db.Inventions.Where(i => !i.IsDeleted).Select(i => i.CreatorPlayerId))
            .Concat(db.CustomAvatarItems.Select(i => i.CreatorPlayerId))
            .Distinct()
            .Take(take)
            .ToListAsync();
        var players = await db.Players
            .Where(p => creatorIds.Contains(p.Id))
            .OrderBy(p => p.Username)
            .ToListAsync();
        return Ok(players.Select(ToInfluencerWire));
    }

    [HttpGet("api/influencerpartnerprogram/influencer")]
    [AllowAnonymous]
    public async Task<IActionResult> Influencer([FromQuery] long? accountId = null, [FromQuery] string? username = null)
    {
        var player = accountId is long id
            ? await db.Players.FirstOrDefaultAsync(p => p.Id == id)
            : await db.Players.FirstOrDefaultAsync(p => p.Username == username);
        if (player is null) return NotFound();
        return Ok(ToInfluencerWire(player));
    }

    [HttpGet("api/influencerpartnerprogram/myinfluencer")]
    [Authorize]
    public async Task<IActionResult> MyInfluencer()
    {
        var me = this.RequireCurrentPlayerId();
        var setting = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "influencer:supported");
        if (setting is null || !long.TryParse(setting.Value, out var influencerId))
            return Ok(new { Influencer = (object?)null });
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == influencerId);
        return Ok(new { Influencer = player is null ? null : ToInfluencerWire(player) });
    }

    [HttpPost("api/influencerpartnerprogram/support")]
    [Authorize]
    public async Task<IActionResult> Support([FromForm] long? influencerAccountId, [FromForm] string? username)
    {
        var me = this.RequireCurrentPlayerId();
        var influencer = influencerAccountId is long id
            ? await db.Players.FirstOrDefaultAsync(p => p.Id == id)
            : await db.Players.FirstOrDefaultAsync(p => p.Username == username);
        if (influencer is null) return NotFound();

        var setting = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "influencer:supported");
        if (setting is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = me,
                Key = "influencer:supported",
                Value = influencer.Id.ToString(),
            });
        }
        else
        {
            setting.Value = influencer.Id.ToString();
        }
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Influencer = ToInfluencerWire(influencer) });
    }

    [HttpPost("api/influencerpartnerprogram/remove")]
    [Authorize]
    public async Task<IActionResult> Remove()
    {
        var me = this.RequireCurrentPlayerId();
        await db.PlayerSettings
            .Where(s => s.PlayerId == me && s.Key == "influencer:supported")
            .ExecuteDeleteAsync();
        return Ok(new { Success = true });
    }

    private static object ToInfluencerWire(PlayerEntity player) => new
    {
        AccountId = (int)player.Id,
        RawUsername = player.Username,
        player.Username,
        DisplayName = player.DisplayName ?? player.Username,
        ProfileImage = player.ProfileImageName ?? string.Empty,
        TreatAsJunior = false,
        HasBirthday = true,
        Platforms = 1,
        IsInfluencer = true,
        SupportedInfluencerId = (int?)null,
        LocalSupportedInfluencerId = (int?)null,
    };
}
