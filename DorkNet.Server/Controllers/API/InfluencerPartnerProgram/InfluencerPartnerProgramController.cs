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
    // 2023 client influencer API (RecNet.Runtime IBEEFFGHGND) — REAL wire
    // shapes verified against the 2023.03.21 ISIL decompile:
    //
    //   influencers  → OBJECT {"InfluencerIds":[...],"ContinuationToken":null}
    //     - IBEEFFGHGND.txt:1612 — response callback is
    //       Func<FEAJJMACBMF, FGLDKEJLAKB<List<Int32>>>, i.e. the body is
    //       deserialized as DTO object FEAJJMACBMF, NOT a bare array
    //       (a bare array crashes the strict reader:
    //       "FGNDDNDAFCI: expected:'{', actual:'['").
    //     - FLEDNKHKIND.txt:215-265 — FEAJJMACBMF's generated reader
    //       registers keys "InfluencerIds" and "ContinuationToken"
    //       (Pascal/camel/lower probes, so camelCase is accepted).
    //     - IBEEFFGHGND_NestedType___c.txt:318-358
    //       (<GetAllInfluencersInternal>b__12_0) — a non-null
    //       ContinuationToken triggers a recursive next-page fetch, so it
    //       must be null once the single page is exhausted.
    //     - IBEEFFGHGND.txt:1547-1575 (OELDHDEIFGP) — client sends
    //       take=1000 + continuationToken query params.
    [HttpGet("api/influencerpartnerprogram/influencers")]
    [AllowAnonymous]
    public async Task<IActionResult> Influencers([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 1000);
        var creatorIds = await db.Rooms.Select(r => r.CreatorPlayerId)
            .Concat(db.Inventions.Where(i => !i.IsDeleted).Select(i => i.CreatorPlayerId))
            .Concat(db.CustomAvatarItems.Select(i => i.CreatorPlayerId))
            .Distinct()
            .Take(take)
            .ToListAsync();
        return Ok(new
        {
            InfluencerIds = creatorIds.Select(id => (int)id).ToArray(),
            ContinuationToken = (string?)null,
        });
    }

    // influencer / myinfluencer → bare Int32 scalar, "no influencer" = 0:
    //   - IBEEFFGHGND.txt:598/951 — both pipelines parse the body as Int32
    //     and convert via Func<Int32, Nullable<Int32>>.
    //   - IBEEFFGHGND_NestedType___c.txt:61-111
    //     (<GetMySupportedInfluencer>b__8_0) — the converter maps
    //     id == 0 → null, else (int?)id. So the correct "none" response is
    //     the literal number 0. A "null"/empty body throws
    //     CHGIJBGGJAG "Response was empty" (Player.log:1947-2031).
    [HttpGet("api/influencerpartnerprogram/influencer")]
    [AllowAnonymous]
    public async Task<IActionResult> Influencer([FromQuery] long? accountId = null, [FromQuery] string? username = null)
    {
        var playerId = accountId is long id
            ? await db.Players.Where(p => p.Id == id).Select(p => (long?)p.Id).FirstOrDefaultAsync()
            : await db.Players.Where(p => p.Username == username).Select(p => (long?)p.Id).FirstOrDefaultAsync();
        return Content(playerId is long pid ? ((int)pid).ToString() : "0", "application/json");
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
                : "0",
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
