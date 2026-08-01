using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.AgeVerification;

[ApiController]
[Route("api/ageverification")]
[Authorize]
public class AgeVerificationController(DorkNetDbContext db) : ControllerBase
{
    /// <summary>The 2023-03-21 client POSTs this (verb register <c>rdx=2</c>
    /// at RecNet.Runtime/ALLHCFFLMLD.txt:216-222) and deserialises the body as
    /// a bare <c>String</c>: the issuing method is
    /// <c>FGLDKEJLAKB&lt;IPCJLCNIBEG&lt;System.String&gt;&gt; GKEBPOPAEJC()</c>
    /// (:150). Returning a JSON object here made Json.NET throw, so the
    /// promise rejected and the code was never shown in the watch.
    /// GET stays registered because it costs nothing.</summary>
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
        return Content(JsonSerializer.Serialize(code), "application/json");
    }
}
