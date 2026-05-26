using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Auth;

/// <summary>
/// Photon custom-auth webhook (api.rec.net).
///
/// Configure in Photon dashboard:
///   Authentication → Custom → URL = https://api.rec.net/api/auth/v1/photon
///   Query params sent by client: token=&lt;our_jwt&gt;
///
/// Photon ResultCode: 1 = success, 2 = fail, 3 = param error
/// </summary>
[ApiController]
public class PhotonAuthController(AuthService authService, PlayerService playerService) : ControllerBase
{
    [HttpGet("api/auth/v1/photon")]
    [HttpPost("api/auth/v1/photon")]
    public async Task<IActionResult> Authenticate([FromQuery] string? token, [FromQuery] string? Token)
    {
        var jwt = token ?? Token;

        if (string.IsNullOrWhiteSpace(jwt))
            return Ok(new { ResultCode = 3, Message = "Missing token parameter" });

        var playerId = authService.ValidateToken(jwt);
        if (playerId is null)
            return Ok(new { ResultCode = 2, Message = "Invalid or expired token" });

        var player = await playerService.GetByIdAsync(playerId.Value);
        if (player is null)
            return Ok(new { ResultCode = 2, Message = "Player not found" });

        return Ok(new
        {
            ResultCode = 1,
            Message = "ok",
            UserId = player.Id.ToString(),
            Nickname = player.DisplayName ?? player.Username,
        });
    }
}
