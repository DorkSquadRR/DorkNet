using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.PlayStationPlus;

[ApiController]
[Authorize]
public class PlayStationPlusController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/playstationplus/membership")]
    public async Task<IActionResult> Membership()
    {
        var me = this.RequireCurrentPlayerId();
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "playstationplus:expires");
        var expires = row is not null && DateTime.TryParse(row.Value, out var parsed)
            ? parsed
            : DateTime.MinValue;
        var active = expires > DateTime.UtcNow;
        return Ok(new
        {
            IsMember = active,
            MembershipActive = active,
            ExpiresAt = active ? expires : (DateTime?)null,
        });
    }

    [HttpPost("api/playstationplus/expire")]
    public async Task<IActionResult> Expire()
    {
        var me = this.RequireCurrentPlayerId();
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == "playstationplus:expires");
        if (row is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = me,
                Key = "playstationplus:expires",
                Value = DateTime.UtcNow.AddSeconds(-1).ToString("O"),
            });
        }
        else
        {
            row.Value = DateTime.UtcNow.AddSeconds(-1).ToString("O");
        }
        await db.SaveChangesAsync();
        return Ok(new { Success = true, IsMember = false });
    }
}
