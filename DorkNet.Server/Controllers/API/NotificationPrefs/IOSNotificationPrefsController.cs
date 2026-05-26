using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.NotificationPrefs;

/// <summary>
/// api.rec.net/api/messages/v1/IOSGetNotificationPreferences +
/// IOSModifyNotificationPreferences. Wire DTO matches
/// <c>RecNet.IOSNotificationPreferences</c> (decompiled at
/// <c>Cpp2IL_CS/.../RecNet/IOSNotificationPreferences.cs</c>):
/// <c>ChatMessage(bool), FriendInvite(bool), FavoriteFriendOnline(bool)</c>.
///
/// Persists via <see cref="NotificationPrefsEntity"/> keyed by
/// (PlayerId, Platform). Defaults to all-on when no row exists yet.
/// </summary>
[ApiController]
[Authorize]
public class IOSNotificationPrefsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("api/messages/v1/IOSGetNotificationPreferences")]
    public async Task<IActionResult> Get()
    {
        var pid = Me;
        var row = await db.NotificationPrefs.FirstOrDefaultAsync(p => p.PlayerId == pid);
        return Ok(ToWire(row));
    }

    public sealed class IOSPrefsRequest
    {
        public bool ChatMessage { get; set; } = true;
        public bool FriendInvite { get; set; } = true;
        public bool FavoriteFriendOnline { get; set; } = true;
    }

    [HttpPost("api/messages/v1/IOSModifyNotificationPreferences")]
    public async Task<IActionResult> Modify([FromBody] IOSPrefsRequest body)
    {
        var pid = Me;
        var row = await db.NotificationPrefs.FirstOrDefaultAsync(p => p.PlayerId == pid);
        if (row is null)
        {
            row = new NotificationPrefsEntity
            {
                PlayerId = pid,
                Platform = "ios",
            };
            db.NotificationPrefs.Add(row);
        }
        row.AllowMessage = body.ChatMessage;
        row.AllowFriendRequest = body.FriendInvite;
        row.AllowAnnouncements = body.FavoriteFriendOnline;
        row.AllowAll = body.ChatMessage || body.FriendInvite || body.FavoriteFriendOnline;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(row));
    }

    /// <summary>POST <c>IOSResetNotificationPreferencesBadgeCount</c> —
    /// the watch sends this when the user opens the iOS app to clear
    /// the badge counter. No persistent state to mutate; ack.</summary>
    [HttpPost("api/messages/v1/IOSResetNotificationPreferencesBadgeCount")]
    public IActionResult ResetBadge() => Ok(new { Reset = true });

    public sealed class SaveTokenRequest
    {
        public string? Token { get; set; }
        public string? DeviceToken { get; set; }
        public string? Platform { get; set; }
    }

    /// <summary>POST <c>api/messages/v1/IOSSaveDeviceToken</c> —
    /// upserts the caller's APNS token to
    /// <see cref="PushTokenEntity"/> for a future APNS proxy to fan
    /// out push notifications.</summary>
    [HttpPost("api/messages/v1/IOSSaveDeviceToken")]
    public async Task<IActionResult> SaveDeviceToken(
        [FromBody] SaveTokenRequest? body,
        [FromForm(Name = "Token")] string? tokenForm,
        [FromForm(Name = "DeviceToken")] string? deviceTokenForm)
    {
        var pid = Me;
        var token = body?.Token ?? body?.DeviceToken ?? tokenForm ?? deviceTokenForm ?? string.Empty;
        var platform = body?.Platform ?? "ios";
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "missing token" });

        var row = await db.PushTokens.FirstOrDefaultAsync(t =>
            t.PlayerId == pid && t.Platform == platform);
        if (row is null)
        {
            row = new PushTokenEntity { PlayerId = pid, Platform = platform };
            db.PushTokens.Add(row);
        }
        row.Token = token;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { Saved = true });
    }

    /// <summary>POST <c>api/messages/v1/IOSClearDeviceToken</c> —
    /// drops every push-token row for the caller (e.g. on iOS
    /// logout).</summary>
    [HttpPost("api/messages/v1/IOSClearDeviceToken")]
    public async Task<IActionResult> ClearDeviceToken()
    {
        var pid = Me;
        var rows = await db.PushTokens.Where(t => t.PlayerId == pid).ToListAsync();
        if (rows.Count > 0)
        {
            db.PushTokens.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
        return Ok(new { Cleared = rows.Count });
    }

    private static object ToWire(NotificationPrefsEntity? r) => new
    {
        ChatMessage = r?.AllowMessage ?? true,
        FriendInvite = r?.AllowFriendRequest ?? true,
        FavoriteFriendOnline = r?.AllowAnnouncements ?? true,
    };
}
