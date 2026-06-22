using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.AgeVerification;

[ApiController]
[Route("api/ageverification")]
[Authorize]
public class AgeVerificationController(DorkNetDbContext db) : ControllerBase
{
    [HttpPost("generateCode")]
    [HttpGet("generateCode")]
    public async Task<IActionResult> GenerateCode()
    {
        var pid = this.RequireCurrentPlayerId();
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = pid,
            Key = $"ageverification:{DateTime.UtcNow:yyyyMMddHHmmss}",
            Value = code,
        });
        await db.SaveChangesAsync();
        return Ok(new { Code = code, VerificationCode = code, ExpiresAt = DateTime.UtcNow.AddMinutes(15) });
    }
}
