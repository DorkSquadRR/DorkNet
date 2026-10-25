using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Admin;

/// <summary>
/// admin.rec.net/api/admin/v1/login — same-origin login endpoint for
/// the browser-based admin UI. Wraps a username+password verify
/// against PlayerEntity.PasswordHash and returns the JWT pair the UI
/// can stash in localStorage. Refuses non-admin accounts up front so a
/// regular player password can't be used to scope into the admin tools
/// even if the JWT is valid against api/admin/v1/* (the AdminOnly
/// filter would catch it anyway, but the early reject saves a
/// confused-looking 403).
///
/// Lives on admin.rec.net so the static admin page (also served from
/// admin.rec.net) can fetch with a same-origin request and skip CORS
/// entirely.
/// </summary>
[ApiController]
[Route("api/admin/v1")]
public class AdminLoginController(
    DorkNetDbContext db,
    AuthService authService) : ControllerBase
{
    public sealed record AdminLoginRequest(string Username, string Password);

    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] AdminLoginRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            return BadRequest(new { error = "missing_credentials" });

        var player = await db.Players.FirstOrDefaultAsync(p => p.Username == body.Username);
        if (player is null || player.PasswordHash is null)
            return Unauthorized(new { error = "invalid_credentials" });
        if (!BCrypt.Net.BCrypt.Verify(body.Password, player.PasswordHash))
            return Unauthorized(new { error = "invalid_credentials" });
        if (!player.IsAdmin)
            return StatusCode(403, new { error = "not_admin" });

        return TokenResponse(player);
    }

    /// <summary>GET api/admin/v1/sso — exchange a single sign-on session
    /// (<see cref="ExternalSignIn.WebScheme"/>, signed in on this admin host via
    /// the external provider) for the same token pair <c>login</c> returns.
    /// Admin rights still come from the account's <c>IsAdmin</c>.</summary>
    [HttpGet("sso")]
    public async Task<ActionResult> Sso()
    {
        var web = await HttpContext.AuthenticateAsync(ExternalSignIn.WebScheme);
        if (!web.Succeeded || ExternalSignIn.PlayerId(web.Principal) is not { } playerId)
            return Unauthorized(new { error = "not_signed_in" });
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player is null) return Unauthorized(new { error = "not_signed_in" });
        if (!player.IsAdmin) return StatusCode(403, new { error = "not_admin" });
        return TokenResponse(player);
    }

    private OkObjectResult TokenResponse(Data.Entities.PlayerEntity player)
    {
        var (access, refresh) = authService.GenerateTokenPair(player.Id);
        return Ok(new
        {
            access_token = access,
            refresh_token = refresh,
            account_id = player.Id,
            username = player.Username,
            display_name = player.DisplayName,
        });
    }
}
