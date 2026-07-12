using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.PlatformNotifications;

/// <summary>
/// platformnotifications.rec.net — the 2023 client's platform push-
/// notification preference service (RecNet.Runtime LELAJKMOMIA; the
/// service host enum value 15 resolves to the PlatformNotifications
/// entry of the service map, relative paths "preferences",
/// "config/categories", "badge", "device").
///
/// Wire contracts verified against the 2023.03.21 ISIL decompile:
///
///   GET preferences  → RecNet.PlatformNotificationPreferencesDTO — an
///     OBJECT whose only key is "MutedCategories" (a list of category
///     enum values). Generated reader JLHCGPANJCB.txt:151-185 registers
///     exactly that key. LELAJKMOMIA.txt:1102 (DCGHGFDPAMM) issues the
///     GET; an array/empty body throws the strict-reader error seen in
///     Player.log:1409-1711 ("expected:'{', actual:'['" /
///     "Malformed Response: '[]'" → "Failed to get notification
///     preferences").
///
///   PUT preferences  → LELAJKMOMIA.txt:1480 (KKPAKGOCPEA) sends the
///     same DTO ({"MutedCategories":[...]}) with HTTP verb 3 = PUT
///     (verb table HNLCIDLIIBO.txt:878-903). Fire-and-forget
///     (LDGADANDBIO) — response body ignored.
///
///   GET config/categories → parsed as envelope MKIFDCBPEAD
///     {"Results":[...],"TotalResults":N} (reader JBGKGCLPCHP.txt:203-238)
///     then converted via Func&lt;MKIFDCBPEAD, List&lt;PlatformNotification-
///     CategoryConfigDTO&gt;&gt; (LELAJKMOMIA.txt:1930 region). Each item is
///     {"CategoryId","Importance","Name","Description","IsMuteable"}
///     (reader JNFDPPCIPHG.txt:371-462).
/// </summary>
[ApiController]
[Authorize]
public class PlatformNotificationsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>The category catalogue we expose. CategoryId values are
    /// round-tripped between config/categories and MutedCategories, so
    /// they only need to be internally consistent.</summary>
    private static readonly (int CategoryId, string Name, string Description)[] CategoryCatalog =
    {
        (1, "Messages", "Direct messages from other players"),
        (2, "Friend Requests", "Incoming friend requests"),
        (3, "Event Invites", "Invitations to events"),
        (4, "Announcements", "News and announcements from Rec Room"),
    };

    [HttpGet("/preferences")]
    [HttpGet("/platformnotifications/preferences")]
    public async Task<IActionResult> Preferences()
    {
        var row = await db.NotificationPrefs.FirstOrDefaultAsync(p => p.PlayerId == Me);
        // Exact DTO shape: {"MutedCategories":[...]} — see class doc.
        return Ok(new { MutedCategories = MutedCategoriesFor(row) });
    }

    /// <summary>PUT preferences — body is the same DTO
    /// ({"MutedCategories":[1,4]}). Parsed manually so we tolerate any
    /// key casing and missing/null lists. Response body is ignored by
    /// the client (LDGADANDBIO), but we echo the stored DTO anyway.</summary>
    [HttpPut("/preferences")]
    [HttpPost("/preferences")]
    [HttpPut("/platformnotifications/preferences")]
    [HttpPost("/platformnotifications/preferences")]
    public async Task<IActionResult> SetPreferences()
    {
        List<int>? muted = null;
        string raw;
        using (var reader = new StreamReader(Request.Body))
            raw = await reader.ReadToEndAsync();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!prop.Name.Equals("MutedCategories", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                        muted = new List<int>();
                        foreach (var el in prop.Value.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v))
                                muted.Add(v);
                            else if (el.ValueKind == JsonValueKind.String
                                     && int.TryParse(el.GetString(), out var s))
                                muted.Add(s);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON body — treat as "no change".
            }
        }

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

        if (muted is not null)
        {
            row.AllowMessage = !muted.Contains(1);
            row.AllowFriendRequest = !muted.Contains(2);
            row.AllowEventInvite = !muted.Contains(3);
            row.AllowAnnouncements = !muted.Contains(4);
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(new { MutedCategories = MutedCategoriesFor(row) });
    }

    /// <summary>GET config/categories — {"Results":[...],"TotalResults":N}
    /// envelope of PlatformNotificationCategoryConfigDTO items.</summary>
    [HttpGet("/config/categories")]
    [HttpGet("/platformnotifications/config/categories")]
    [AllowAnonymous]
    public IActionResult Categories()
    {
        var categories = CategoryCatalog
            .Select(c => new
            {
                c.CategoryId,
                Importance = 0,
                c.Name,
                c.Description,
                IsMuteable = true,
            })
            .ToArray();

        return Ok(new
        {
            Results = categories,
            TotalResults = categories.Length,
        });
    }

    private static int[] MutedCategoriesFor(NotificationPrefsEntity? row)
    {
        if (row is null) return Array.Empty<int>();
        var muted = new List<int>(4);
        if (!row.AllowMessage) muted.Add(1);
        if (!row.AllowFriendRequest) muted.Add(2);
        if (!row.AllowEventInvite) muted.Add(3);
        if (!row.AllowAnnouncements) muted.Add(4);
        return muted.ToArray();
    }
}
