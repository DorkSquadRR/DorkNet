using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Account;

/// <summary>
/// api.rec.net/api/account/* — same endpoints as accounts.rec.net but routed
/// through the API service. The Accounts class in dump.cs doesn't expose its
/// base URL constant so the game may use either host; we cover both.
/// </summary>
[ApiController]
public class AccountApiController(
    PlayerService playerService,
    AuthService authService,
    DorkNetDbContext db,
    NotificationService notifications,
    ServerSettingsService settings,
    SignupCodeService signupCodes,
    ILogger<AccountApiController> logger) : ControllerBase
{
    [HttpPost("api/account/v1/create")]
    [HttpPost("api/account/v1")]
    [HttpPost("api/account/v2/create")]
    [HttpPost("api/createaccount")]
    public async Task<IActionResult> CreateAccount(
        [FromForm] int platform,
        [FromForm] string? platformId,
        [FromForm] string? deviceId,
        [FromForm] string? Username,
        [FromForm] string? DisplayName)
    {
        if (await settings.AreSignupsDisabledAsync())
        {
            logger.LogInformation(
                "[api-account] api/account/v1/create refused — signups disabled (device={Device} platform={Platform})",
                deviceId, platform);
            await signupCodes.RecordPendingDeviceAsync(
                deviceId, platform, platformId, SignupCodeService.ClientIp(HttpContext));
            return Ok(new CreateAccountResponse
            {
                Success = false,
                Error = "Account creation is currently disabled by the server admin.",
            });
        }

        var player = await playerService.GetOrCreateByDeviceAsync(
            deviceId: deviceId,
            platform: platform,
            platformId: platformId,
            displayName: DisplayName ?? Username);

        return Ok(new CreateAccountResponse
        {
            Success = true,
            Error = string.Empty,
            Account = new RecNetAccount
            {
                AccountId = (int)player.Id,
                RawUsername = player.Username,
                Username = player.Username,
                DisplayName = player.DisplayName ?? player.Username,
                ProfileImage = string.Empty,
                TreatAsJunior = false,
                HasBirthday = true,
                Platforms = 1,
            },
        });
    }

    [HttpGet("api/account/v1/me")]
    [HttpGet("api/account/v2/me")]
    public async Task<IActionResult> GetMe()
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        return Ok(new RecNetSelfAccount
        {
            AccountId = (int)player.Id,
            RawUsername = player.Username,
            Username = player.Username,
            DisplayName = player.DisplayName ?? player.Username,
            ProfileImage = player.ProfileImageName ?? string.Empty,
            TreatAsJunior = false,
            HasBirthday = true,
            Platforms = 1,
            Email = player.Email ?? string.Empty,
            Phone = string.Empty,
            Birthday = new DateTime(2000, 1, 1),
            JuniorState = 0,
            ParentAccountId = null,
        });
    }

    /// <summary>
    /// Resolves the player tied to the bearer token on the request. Returns
    /// null when no/invalid bearer is supplied — caller decides whether
    /// that's a 401 or a softer fallback. Anonymous-account creation was
    /// previously the fallback path; deleted in Phase 1c because every
    /// caller has gone through `/account/create` (deviceId-bound) before
    /// reaching us.
    /// </summary>
    private async Task<PlayerEntity?> GetCurrentPlayerAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = auth["Bearer ".Length..].Trim();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return null;
        return await playerService.GetByIdAsync(playerId);
    }

    // Modification endpoints — POSTs land on api.* host with form body
    // carrying the new value. AuthAccountController on auth.* handles
    // the same shape with real persistence; we mirror that here so
    // either host works.

    [HttpPost("api/account/v1/displayname")]
    public async Task<IActionResult> SetDisplayName([FromForm(Name = "displayName")] string? displayName,
                                                    [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        var name = (displayName ?? value ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new RecNetResult { Success = false, Error = "empty" });
        await playerService.UpdateDisplayNameAsync(p.Id, name);
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/bio")]
    public async Task<IActionResult> SetBio([FromForm(Name = "bio")] string? bio,
                                            [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        await playerService.UpdateBioAsync(p.Id, (bio ?? value ?? string.Empty));
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/username")]
    public async Task<IActionResult> SetUsername([FromForm(Name = "username")] string? username,
                                                 [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        var newName = (username ?? value ?? string.Empty).Trim();
        if (newName.Length == 0) return BadRequest(new RecNetResult { Success = false, Error = "empty" });
        var taken = await db.Players.AnyAsync(x => x.Username == newName && x.Id != p.Id);
        if (taken) return Ok(new RecNetResult { Success = false, Error = "username_taken" });
        p.Username = newName;
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/birthday")]
    public async Task<IActionResult> SetBirthday([FromForm(Name = "birthday")] string? birthday,
                                                 [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        var raw = birthday ?? value ?? string.Empty;
        if (DateTime.TryParse(raw, out var parsed))
        {
            p.Birthday = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/email")]
    public async Task<IActionResult> SetEmail([FromForm(Name = "email")] string? email,
                                              [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        var addr = (email ?? value ?? string.Empty).Trim();
        if (addr.Length > 0) p.Email = addr;
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/phone")]
    [HttpPost("api/account/v1/confirmphone")]
    public async Task<IActionResult> SetPhone([FromForm(Name = "phone")] string? phone,
                                              [FromForm(Name = "value")] string? value)
    {
        var p = await GetCurrentPlayerAsync();
        if (p is null) return Unauthorized();
        var num = (phone ?? value ?? string.Empty).Trim();
        if (num.Length > 0) p.Phone = num;
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("api/account/v1/profileimage")]
    [HttpPost("api/account/me/profileimage")]
    [HttpPost("account/me/profileimage")]
    [HttpPost("profileimage")]
    [HttpPut("api/account/v1/profileimage")]
    [HttpPut("api/account/me/profileimage")]
    [HttpPut("account/me/profileimage")]
    [HttpPut("profileimage")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetProfileImage(
        [FromForm(Name = "imageName")] string? imageName,
        [FromForm(Name = "fileName")] string? fileName,
        [FromForm(Name = "image")] string? imageAlt)
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();

        var name = imageName ?? fileName ?? imageAlt
                   ?? Request.Query["imageName"].ToString()
                   ?? Request.Query["fileName"].ToString();
        player.ProfileImageName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[api-account] profile image set: player={PlayerId} -> {Name}",
            player.Id,
            player.ProfileImageName ?? "<cleared>");
        await NotifyAccountImageChangedAsync(player);
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    private Task NotifyAccountImageChangedAsync(PlayerEntity player)
    {
        var account = new RecNetAccount
        {
            AccountId = (int)player.Id,
            RawUsername = player.Username,
            Username = player.Username,
            DisplayName = player.DisplayName ?? player.Username,
            ProfileImage = player.ProfileImageName ?? string.Empty,
            TreatAsJunior = player.IsJunior,
            HasBirthday = true,
            Platforms = 1,
        };
        var selfAccount = new RecNetSelfAccount
        {
            AccountId = account.AccountId,
            RawUsername = account.RawUsername,
            Username = account.Username,
            DisplayName = account.DisplayName,
            ProfileImage = account.ProfileImage,
            TreatAsJunior = account.TreatAsJunior,
            HasBirthday = account.HasBirthday,
            Platforms = account.Platforms,
            Email = player.Email ?? string.Empty,
            Phone = player.Phone ?? string.Empty,
            Birthday = player.Birthday,
            JuniorState = player.IsJunior ? 1 : 0,
            ParentAccountId = null,
        };
        return Task.WhenAll(
            notifications.NotifyTypedAsync(player.Id, "AccountUpdate", account),
            notifications.NotifyTypedAsync(player.Id, "SelfAccountUpdate", selfAccount));
    }

    [HttpGet("api/account/v1/namegen")]
    [HttpGet("api/account/v1/namegeneration")]
    public IActionResult NameGen() => Ok(new
    {
        Nouns = new[] { "Player", "Hero", "Star", "Fox", "Wolf" },
        Adjectives = new[] { "Brave", "Swift", "Clever", "Bold", "Bright" },
    });
}
