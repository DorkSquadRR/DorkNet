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
    // The 2023 client's influencer API (RecNet.Runtime IBEEFFGHGND) reads
    // BARE scalars, NOT wrapped account objects:
    //   influencers   → List<Int32>       (account ids)
    //   influencer    → Nullable<Int32>   (the account id, or null)
    //   myinfluencer  → Nullable<Int32>   (supported influencer's id, or null)
    // Returning an object here crashes the strict reader
    // ("expected:'Number Token', actual:'{'"), which is what broke the
    // profile view (and the cheer flow that opens it).
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
        return Ok(creatorIds.Select(id => (int)id).ToArray());
    }

    [HttpGet("api/influencerpartnerprogram/influencer")]
    [AllowAnonymous]
    public async Task<IActionResult> Influencer([FromQuery] long? accountId = null, [FromQuery] string? username = null)
    {
        var playerId = accountId is long id
            ? await db.Players.Where(p => p.Id == id).Select(p => (long?)p.Id).FirstOrDefaultAsync()
            : await db.Players.Where(p => p.Username == username).Select(p => (long?)p.Id).FirstOrDefaultAsync();
        return Content(playerId is long pid ? ((int)pid).ToString() : "null", "application/json");
    }

    [HttpGet("api/influencerpartnerprogram/myinfluencer")]
    [Authorize]
    public async Task<IActionResult> MyInfluencer()
    {
        var me = this.RequireCurrentPlayerId();
        var setting = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "influencer:supported");
        return Content(
            setting is not null && long.TryParse(setting.Value, out var influencerId)
                ? ((int)influencerId).ToString()
                : "null",
            "application/json");
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
        // Client return type is LDGADANDBIO (the fire-and-forget POST response
        // handle, same as PlayerCheer create) — it only checks success and
        // ignores the body, so no typed shape is required here.
        return Ok(new { Success = true, Influencer = (int)influencer.Id });
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
}
