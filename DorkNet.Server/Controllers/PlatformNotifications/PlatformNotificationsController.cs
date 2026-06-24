using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.PlatformNotifications;

[ApiController]
[Authorize]
public class PlatformNotificationsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("/preferences")]
    [HttpGet("/platformnotifications/preferences")]
    public async Task<IActionResult> Preferences()
    {
        var row = await db.NotificationPrefs.FirstOrDefaultAsync(p => p.PlayerId == Me);
        return Ok(ToPreferencesWire(row));
    }

    [HttpPut("/preferences")]
    [HttpPost("/preferences")]
    [HttpPut("/platformnotifications/preferences")]
    [HttpPost("/platformnotifications/preferences")]
    public async Task<IActionResult> SetPreferences([FromBody] PlatformNotificationPrefsRequest? body)
    {
        var row = await db.NotificationPrefs.FirstOrDefaultAsync(p => p.PlayerId == Me);
        if (row is null)
        {
            row = new NotificationPrefsEntity
            {
                PlayerId = Me,
                Platform = "platform",
            };
            db.NotificationPrefs.Add(row);
        }

        if (body is not null)
        {
            row.AllowAll = body.AllowAll ?? body.Enabled ?? row.AllowAll;
            row.AllowMessage = body.AllowMessages ?? body.Messages ?? row.AllowMessage;
            row.AllowFriendRequest = body.AllowFriendRequests ?? body.FriendRequests ?? row.AllowFriendRequest;
            row.AllowEventInvite = body.AllowEventInvites ?? body.EventInvites ?? row.AllowEventInvite;
            row.AllowAnnouncements = body.AllowAnnouncements ?? body.Announcements ?? row.AllowAnnouncements;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(ToPreferencesWire(row));
    }

    [HttpGet("/config/categories")]
    [HttpGet("/platformnotifications/config/categories")]
    [AllowAnonymous]
    public IActionResult Categories()
    {
        var categories = new[]
        {
            Category(1, "messages", "Messages"),
            Category(2, "friendRequests", "Friend Requests"),
            Category(3, "eventInvites", "Event Invites"),
            Category(4, "announcements", "Announcements"),
        };

        return Ok(new
        {
            Categories = categories,
            Results = categories,
            TotalResults = categories.Length,
        });
    }

    private static object Category(int id, string key, string name) => new
    {
        Id = id,
        CategoryId = id,
        Key = key,
        Name = name,
        DisplayName = name,
        IsEnabled = true,
        Enabled = true,
        DefaultEnabled = true,
    };

    private static object ToPreferencesWire(NotificationPrefsEntity? row) => new
    {
        AllowAll = row?.AllowAll ?? true,
        Enabled = row?.AllowAll ?? true,
        AllowMessages = row?.AllowMessage ?? true,
        Messages = row?.AllowMessage ?? true,
        AllowFriendRequests = row?.AllowFriendRequest ?? true,
        FriendRequests = row?.AllowFriendRequest ?? true,
        AllowEventInvites = row?.AllowEventInvite ?? true,
        EventInvites = row?.AllowEventInvite ?? true,
        AllowAnnouncements = row?.AllowAnnouncements ?? true,
        Announcements = row?.AllowAnnouncements ?? true,
        Categories = new[]
        {
            new { CategoryId = 1, Key = "messages", Enabled = row?.AllowMessage ?? true },
            new { CategoryId = 2, Key = "friendRequests", Enabled = row?.AllowFriendRequest ?? true },
            new { CategoryId = 3, Key = "eventInvites", Enabled = row?.AllowEventInvite ?? true },
            new { CategoryId = 4, Key = "announcements", Enabled = row?.AllowAnnouncements ?? true },
        },
    };

    public sealed class PlatformNotificationPrefsRequest
    {
        public bool? AllowAll { get; set; }
        public bool? Enabled { get; set; }
        public bool? AllowMessages { get; set; }
        public bool? Messages { get; set; }
        public bool? AllowFriendRequests { get; set; }
        public bool? FriendRequests { get; set; }
        public bool? AllowEventInvites { get; set; }
        public bool? EventInvites { get; set; }
        public bool? AllowAnnouncements { get; set; }
        public bool? Announcements { get; set; }
    }
}
