using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.Auth;

/// <summary>
/// auth.rec.net/account/me/* — password and account-recovery endpoints
/// the client invokes from the in-game settings screen. Real persistence:
/// the password is stored as a BCrypt hash on
/// <see cref="DorkNet.Server.Data.Entities.PlayerEntity.PasswordHash"/>;
/// HasPassword reflects whether the column is non-null; ChangePassword
/// requires the old password to match (or for the column to be null —
/// first-time set); ResetPassword is admin-gated for now.
/// </summary>
[ApiController]
public class AuthAccountController(DorkNetDbContext db) : ControllerBase
{
    /// <summary>GET /account/me/haspassword — returns whether the
    /// caller has a password set. Client uses
    /// <c>ExpectPrimitiveResponse&lt;bool&gt;</c> so the body must be
    /// the literal JSON <c>true</c> or <c>false</c>.</summary>
    [HttpGet("/account/me/haspassword")]
    [HttpGet("/api/platformlogin/haspassword", Order = 10)]
    [Authorize]
    public async Task<IActionResult> HasPassword()
    {
        var pid = this.RequireCurrentPlayerId();
        var hasPwd = await db.Players
            .Where(p => p.Id == pid)
            .Select(p => p.PasswordHash != null)
            .FirstOrDefaultAsync();
        return Content(hasPwd ? "true" : "false", "application/json");
    }

    /// <summary>POST /account/me/changepassword (and aliases). When the
    /// account already has a password the caller must supply the old
    /// one; first-time creation skips that check. Form-encoded body.</summary>
    [HttpPost("/account/me/changepassword")]
    [HttpPost("/account/me/createpassword")]
    [HttpPost("/api/platformlogin/password", Order = 10)]
    [HttpPut("/api/platformlogin/password", Order = 10)]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromForm] string? oldPassword,
        [FromForm] string? newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            return Ok(new { success = false, error = "password_too_short" });

        var pid = this.RequireCurrentPlayerId();
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
        if (player is null) return Unauthorized();

        // First-time create: PasswordHash is null and oldPassword is
        // ignored. Subsequent changes require the existing password to
        // verify so a stolen JWT can't silently rotate the password.
        if (!string.IsNullOrEmpty(player.PasswordHash))
        {
            if (string.IsNullOrEmpty(oldPassword) ||
                !BCrypt.Net.BCrypt.Verify(oldPassword, player.PasswordHash))
                return Ok(new { success = false, error = "old_password_mismatch" });
        }

        player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 11);
        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }

    /// <summary>POST /account/me/resetpassword — sends a reset link.
    /// Without an SMTP integration we have nothing to send to, so this
    /// is admin-only: an admin can clear the password column on any
    /// account from the admin tab and the user re-creates it via
    /// changepassword on next login. Anonymous calls 401.</summary>
    [HttpPost("/account/me/resetpassword")]
    [HttpPost("/api/platformlogin/resetpassword", Order = 10)]
    [Authorize]
    [AdminOnly]
    public async Task<IActionResult> AdminResetPassword(
        [FromForm] long? targetPlayerId)
    {
        if (targetPlayerId is null)
            return Ok(new { success = false, error = "missing_target" });
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == targetPlayerId);
        if (player is null) return Ok(new { success = false, error = "unknown_player" });
        player.PasswordHash = null;
        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }
}
