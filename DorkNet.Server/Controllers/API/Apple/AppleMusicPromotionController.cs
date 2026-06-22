using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Apple;

[ApiController]
public class AppleMusicPromotionController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/apple/musicpromotion/active")]
    [AllowAnonymous]
    public IActionResult Active()
    {
        var start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);
        return Ok(new { Active = true, StartAt = start, EndAt = end });
    }

    [HttpPost("api/apple/musicpromotion/code")]
    [HttpGet("api/apple/musicpromotion/code")]
    [Authorize]
    public async Task<IActionResult> Code()
    {
        var me = this.RequireCurrentPlayerId();
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "applemusic:promotion_code");
        if (row is null)
        {
            row = new PlayerSettingEntity
            {
                PlayerId = me,
                Key = "applemusic:promotion_code",
                Value = $"RR-{Guid.NewGuid():N}"[..16],
            };
            db.PlayerSettings.Add(row);
            await db.SaveChangesAsync();
        }

        return Ok(new { Code = row.Value, Redeemed = false });
    }
}
