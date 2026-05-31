using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Hubs;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Admin;

/// <summary>
/// api.rec.net/api/admin/v1/* — privileged moderation endpoints. Every
/// action is gated by <see cref="AdminOnlyAttribute"/>, which checks
/// <see cref="PlayerEntity.IsAdmin"/>; every state mutation is logged
/// to <see cref="AdminActionEntity"/> for audit replay.
///
/// The first account created on a fresh DB gets <c>IsAdmin = true</c>
/// (see <see cref="PlayerService.GetOrCreateByDeviceAsync"/>) so a
/// freshly-bootstrapped private server always has at least one root
/// admin without manual SQL.
/// </summary>
[ApiController]
[Route("api/admin/v1")]
[Authorize]
[AdminOnly]
public class AdminController(
    DorkNetDbContext db,
    NotificationService notifications,
    LevelService level,
    CommunityBoardService communityBoard,
    RoomBlobNormalizerService roomBlobNormalizer,
    HtrAssetMirrorService htrMirror,
    OnlinePresenceService onlinePresence,
    PlayerPresenceService playerPresence,
    PlayerLogService playerLog,
    ServerSettingsService serverSettings,
    SignupCodeService signupCodes,
    IObjectStorage storage,
    DomainConfig domain,
    ILogger<AdminController> adminLogger) : ControllerBase
{
    private long CurrentAdminId => this.RequireCurrentPlayerId();

    // ── Player request logs ──────────────────────────────────────────────

    /// <summary>Most recent HTTP requests made by a single player, newest
    /// first. Backed by <see cref="PlayerLogService"/>'s Redis ring buffer
    /// (in-process fallback for dev) — capped at
    /// <see cref="PlayerLogService.MaxEntriesPerPlayer"/> entries per
    /// player with a 7-day key TTL. Used by the admin UI's "Player logs"
    /// tab to spot what API call a player was making when they hit a bug.</summary>
    [HttpGet("players/{id:long}/logs")]
    public ActionResult PlayerLogs(long id, [FromQuery] int take = 200)
    {
        var entries = playerLog.GetRecent(id, take);
        return Ok(entries);
    }

    // ── Players ──────────────────────────────────────────────────────────

    /// <summary>List players with online status, paginated. Used by the
    /// admin watch tab. Default page size 50, max 200 to keep responses
    /// reasonable.</summary>
    [HttpGet("players")]
    public async Task<ActionResult> ListPlayers(
        [FromQuery] string? query, [FromQuery] int take = 50, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        IQueryable<PlayerEntity> q = db.Players.OrderByDescending(p => p.LastSeenAt);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(p => p.Username.Contains(query) || p.DisplayName.Contains(query));

        var page = await q.Skip(skip).Take(take).ToListAsync();
        var onlineSet = onlinePresence.OnlinePlayerIds().ToHashSet();
        return Ok(page.Select(p => new
        {
            p.Id,
            p.Username,
            p.DisplayName,
            p.Email,
            p.IsAdmin,
            p.IsDeveloper,
            p.IsCommunityTeam,
            p.IsVerified,
            p.IsJunior,
            p.BannedUntil,
            p.LastIp,
            p.LastSeenAt,
            p.CreatedAt,
            p.Level,
            p.XP,
            p.ProfileImageName,
            Online = onlineSet.Contains(p.Id),
        }));
    }

    /// <summary>Single-player detail — bundles the player record plus
    /// their currency balances so the admin UI can render an edit page
    /// in one round trip.</summary>
    [HttpGet("players/{id:long}")]
    public async Task<ActionResult> GetPlayer(long id)
    {
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        var balances = await db.CurrencyBalances
            .Where(c => c.PlayerId == id)
            .Select(c => new { c.CurrencyType, c.Balance })
            .ToListAsync();
        var avatar = await db.Avatars
            .Where(a => a.PlayerId == id)
            .Select(a => new { a.OutfitSelections, a.HairColor, a.SkinColor, a.FaceFeatures })
            .FirstOrDefaultAsync();
        return Ok(new
        {
            p.Id,
            p.Username,
            p.DisplayName,
            p.Bio,
            p.Email,
            p.IsAdmin,
            p.IsDeveloper,
            p.IsCommunityTeam,
            p.IsVerified,
            p.IsJunior,
            p.BannedUntil,
            p.LastIp,
            p.LastSeenAt,
            p.CreatedAt,
            p.Level,
            p.XP,
            p.ProfileImageName,
            Online = onlinePresence.OnlinePlayerIds().Contains(p.Id),
            Balances = balances,
            Avatar = avatar,
        });
    }

    public sealed record BanRequest(int DurationDays, string? Reason);

    /// <summary>Ban a player for the given number of days. The
    /// <see cref="BanCheckMiddleware"/> 401s any subsequent request
    /// from the player until <c>BannedUntil</c> elapses; we also push
    /// a <c>ModerationKick</c> notification so the watch sees the ban
    /// immediately.</summary>
    [HttpPost("players/{id:long}/ban")]
    public async Task<ActionResult> BanPlayer(long id, [FromBody] BanRequest body)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();
        if (player.IsAdmin) return BadRequest("Cannot ban an admin. Demote first.");

        var days = Math.Clamp(body.DurationDays, 1, 3650);
        player.BannedUntil = DateTime.UtcNow.AddDays(days);

        await LogAsync("ban_player", "player", id, body.Reason ?? "");
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(id, PushNotificationId.ModerationKick,
            new { Reason = body.Reason ?? "", Until = player.BannedUntil });

        return Ok(new { player.Id, player.BannedUntil });
    }

    [HttpPost("players/{id:long}/unban")]
    public async Task<ActionResult> UnbanPlayer(long id, [FromBody] BanRequest? body)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();

        player.BannedUntil = null;
        await LogAsync("unban_player", "player", id, body?.Reason ?? "");
        await db.SaveChangesAsync();
        return Ok(new { player.Id, player.BannedUntil });
    }

    [HttpPost("players/{id:long}/promote")]
    public async Task<ActionResult> Promote(long id)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();
        player.IsAdmin = true;
        await LogAsync("promote_admin", "player", id, "");
        await db.SaveChangesAsync();
        return Ok(new { player.Id, player.IsAdmin });
    }

    [HttpPost("players/{id:long}/demote")]
    public async Task<ActionResult> Demote(long id)
    {
        // Refuse to demote yourself — guards against the last admin
        // accidentally dropping privileges and being unable to recover.
        if (id == CurrentAdminId)
            return BadRequest("Cannot demote yourself. Have another admin do it.");

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();
        player.IsAdmin = false;
        await LogAsync("demote_admin", "player", id, "");
        await db.SaveChangesAsync();
        return Ok(new { player.Id, player.IsAdmin });
    }

    // ── Rooms ────────────────────────────────────────────────────────────

    public sealed record DeleteRoomRequest(string? Reason);

    /// <summary>Soft-delete a room by setting <c>State = 1</c>
    /// (Archived). The room stops appearing in browse/search results;
    /// its data is still retrievable for an undelete if needed.</summary>
    /// <summary>GET <c>api/admin/v1/diagnostics/room-images</c> —
    /// emergency diagnostic for "room tile shows smiley" symptoms.
    /// Returns one row per seeded room with: the DB ImageName, whether
    /// the corresponding file exists in <c>data/images/</c>, and the
    /// fully-qualified URL the client would fetch. Lets us tell at a
    /// glance whether the bug is (a) DB never got real CDN names from
    /// the backfill, (b) PNG missing on disk, or (c) wrong URL host
    /// being served by the config endpoint.</summary>
    [HttpGet("diagnostics/room-images")]
    public async Task<IActionResult> DiagnosticsRoomImages()
    {
        var imgDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
        var rooms = await db.Rooms
            .Where(r => r.Id >= 100 && r.Id < 1000)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name, r.ImageName })
            .ToListAsync();
        var rows = rooms.Select(r =>
        {
            var path = Path.Combine(imgDir, r.ImageName ?? string.Empty);
            return new
            {
                r.Id,
                r.Name,
                r.ImageName,
                FileExists = !string.IsNullOrEmpty(r.ImageName) && System.IO.File.Exists(path),
                FullPath = path,
                FetchUrl = $"https://{domain.Sub("img")}/{r.ImageName}",
            };
        }).ToList();
        return Ok(new
        {
            ImageDirOnDisk = imgDir,
            ImageDirExists = Directory.Exists(imgDir),
            ImageFileCount = Directory.Exists(imgDir) ? Directory.GetFiles(imgDir).Length : 0,
            RoomImagesJsonExists = System.IO.File.Exists(
                Path.Combine(AppContext.BaseDirectory, "data", "room_images.json")),
            Rooms = rows,
        });
    }

    /// <summary>GET <c>api/admin/v1/players/{id}/clothing-debug</c> —
    /// inspect a single player's wardrobe state. Shows the raw
    /// AvatarEntity columns the watch reads (OutfitSelections,
    /// InventoryJson, HairColor, SkinColor), the parsed-and-filtered
    /// list of items the player will see in their wardrobe drawer
    /// (intersection of InventoryJson with the bundled safelist),
    /// and any GUIDs in InventoryJson that AREN'T in the safelist
    /// (those would crash the watch's parser if shipped — silently
    /// dropped by AvatarItemsController.Get(), but worth knowing
    /// they're sitting in the inventory).
    ///
    /// Mostly here as a one-call sanity check after the avatar
    /// pipeline overhaul: hit it against the player ID, eyeball the
    /// drawer-count, confirm the ratio matches what's in the DB.</summary>
    [HttpGet("players/{id:long}/clothing-debug")]
    public async Task<ActionResult> ClothingDebug(long id)
    {
        var avatar = await db.Avatars
            .Where(a => a.PlayerId == id)
            .Select(a => new
            {
                a.OutfitSelections,
                a.InventoryJson,
                a.HairColor,
                a.SkinColor,
                a.FaceFeatures,
                a.UpdatedAt,
            })
            .FirstOrDefaultAsync();
        if (avatar is null) return NotFound(new { Message = $"No AvatarEntity for player {id}" });

        var safe = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.SafeGuids;
        var catalog = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.Catalog;

        // Parse the player's inventory + intersect against the bundled
        // safelist. Items that pass through end up in
        // /api/avatar/v4/items as renderable wardrobe entries; items
        // that fail are silently dropped at request time.
        List<string> inventoryGuids;
        try
        {
            inventoryGuids = System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(avatar.InventoryJson ?? "[]") ?? new();
        }
        catch { inventoryGuids = new(); }

        var owned = inventoryGuids.Where(g => safe.Contains(g)).ToList();
        var unsafeGuids = inventoryGuids.Where(g => !safe.Contains(g)).ToList();

        // Parse OutfitSelections (the equipped loadout) the same way
        // the watch does — comma-separated, each segment a GUID. Show
        // which slot each one resolves to + whether it's in the safe
        // catalog.
        var equipped = (avatar.OutfitSelections ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim())
            .Where(g => g.Length > 0)
            .Select(g => new
            {
                Guid = g,
                InSafelist = safe.Contains(g),
                FriendlyName = catalog.TryGetValue(g, out var c) ? c.FriendlyName : null,
            })
            .ToList();

        return Ok(new
        {
            PlayerId = id,
            avatar.UpdatedAt,
            avatar.HairColor,
            avatar.SkinColor,
            avatar.FaceFeatures,
            OutfitSelections = avatar.OutfitSelections,
            EquippedItems = equipped,
            InventoryRaw = avatar.InventoryJson,
            InventoryCount = inventoryGuids.Count,
            OwnedAndRenderable = owned.Count,
            OwnedAndRenderableGuids = owned,
            UnsafeOwnedGuids = unsafeGuids,
            CatalogTotal = catalog.Count,
            SafelistTotal = safe.Count,
        });
    }

    /// <summary>GET <c>api/admin/v1/rooms</c> — flat list of every
    /// room in the DB (id + name + creator + blob count) for the admin
    /// UI's room-pickers. Custom (AG) rooms first, then RR Originals;
    /// each ordered by name. The blob count helps the .htr mirror
    /// dropdown skip rooms that have no blobs to scan.</summary>
    [HttpGet("rooms")]
    public async Task<ActionResult> ListAllRooms()
    {
        var rooms = await db.Rooms
            .OrderByDescending(r => r.IsAGRoom)
            .ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.IsAGRoom,
                r.IsDormRoom,
                r.CreatorPlayerId,
                BlobCount = db.RoomDataBlobs.Count(b => b.RoomId == r.Id),
            })
            .ToListAsync();
        return Ok(rooms);
    }

    /// <summary>GET <c>api/admin/v1/rooms/{id}/visitors</c> — most
    /// recent unique visitors of a room. Joins
    /// <see cref="RoomVisitEntity"/> against <see cref="PlayerEntity"/>
    /// so the admin UI can show "who's been here lately" with
    /// usernames + per-player visit counts.</summary>
    [HttpGet("rooms/{id:long}/visitors")]
    public async Task<ActionResult> RoomVisitors(long id, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await (from v in db.RoomVisits
                          join p in db.Players on v.PlayerId equals p.Id
                          where v.RoomId == id
                          orderby v.LastVisitAt descending
                          select new
                          {
                              PlayerId = v.PlayerId,
                              p.Username,
                              p.DisplayName,
                              v.VisitCount,
                              v.FirstVisitAt,
                              v.LastVisitAt,
                          })
                          .Take(take)
                          .ToListAsync();
        return Ok(rows);
    }

    [HttpDelete("rooms/{id:long}")]
    public async Task<ActionResult> DeleteRoom(long id, [FromBody] DeleteRoomRequest? body)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();
        room.State = 1; // Archived
        await LogAsync("delete_room", "room", id, body?.Reason ?? "");
        await db.SaveChangesAsync();
        return Ok(new { room.Id, room.State });
    }

    // The bulk "purge every custom room" endpoint was removed: a single
    // wrong click could wipe every imported map in one shot. Per-room
    // purge (below) is the only hard-delete path, gated by two
    // independent room-name typed confirmations to make accidental
    // wipes effectively impossible.

    public sealed record HardDeleteRoomRequest(
        string? Reason,
        // The admin UI prompts for the room name TWICE in separate
        // inputs — both have to match the room's actual Name (exactly,
        // case-sensitive) before the request will fire. Same pattern
        // GitHub uses for repository deletion.
        string? ConfirmName1,
        string? ConfirmName2);

    /// <summary>POST <c>api/admin/v1/rooms/{id}/purge</c> — hard-delete a
    /// single custom room. Refuses to act on TRUE RR Originals (owned
    /// by the system seed account) or dorms; for those, the soft-delete
    /// via DELETE /rooms/{id} is the only supported path so a fat
    /// finger doesn't wipe canonical content.
    ///
    /// A user-cloned room often inherits <c>IsAGRoom=true</c> from its
    /// source — that doesn't make it canonical, it just means the
    /// matchmaker uses the AG-room join path. Gate purely on
    /// <c>CreatorPlayerId == SystemAccountId</c>, which is the only
    /// reliable "is this actually a seeded RR Original?" signal.
    ///
    /// Requires BOTH <c>ConfirmName1</c> and <c>ConfirmName2</c> in the
    /// body to exactly match the room's current Name. Both must match
    /// independently — the SPA renders two separate inputs and the
    /// admin types the name twice. Returns 400 with
    /// <c>confirm_name_mismatch</c> if either is missing/wrong so the
    /// SPA can surface the error inline without leaking which one
    /// failed.</summary>
    [HttpPost("rooms/{id:long}/purge")]
    public async Task<ActionResult> HardDeleteRoom(long id, [FromBody] HardDeleteRoomRequest? body)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();
        if (room.IsDormRoom)
            return BadRequest(new { error = "refuses_to_hard_delete_dorm", note = "Dorms are per-player state; never purge." });
        if (room.IsAGRoom && room.CreatorPlayerId == PlayerService.SystemAccountId)
            return BadRequest(new { error = "refuses_to_hard_delete_seeded_room", note = "This row is owned by the system seed account — soft-archive via DELETE /rooms/{id} instead." });

        var confirm1 = body?.ConfirmName1 ?? string.Empty;
        var confirm2 = body?.ConfirmName2 ?? string.Empty;
        if (!string.Equals(confirm1, room.Name, StringComparison.Ordinal)
            || !string.Equals(confirm2, room.Name, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                error = "confirm_name_mismatch",
                note = "Both ConfirmName1 and ConfirmName2 must exactly equal the room's current Name (case-sensitive).",
            });
        }

        var scenes = await db.RoomScenes.Where(s => s.RoomId == id).ToListAsync();
        var roomBlobs = await db.RoomDataBlobs.Where(b => b.RoomId == id).ToListAsync();
        RoomDataBlobEntity? imageBlob = null;
        if (!string.IsNullOrEmpty(room.ImageName))
        {
            imageBlob = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.RoomId == 0 && b.BlobName == room.ImageName);
        }

        db.RoomScenes.RemoveRange(scenes);
        db.RoomDataBlobs.RemoveRange(roomBlobs);
        if (imageBlob is not null) db.RoomDataBlobs.Remove(imageBlob);
        db.Rooms.Remove(room);

        await LogAsync("hard_delete_room", "room", id,
            $"name={room.Name} reason={body?.Reason ?? ""} blobs={roomBlobs.Count} scenes={scenes.Count}");
        await db.SaveChangesAsync();

        return Ok(new
        {
            deleted = id,
            name = room.Name,
            blobs = roomBlobs.Count,
            scenes = scenes.Count,
            thumbnail = imageBlob is not null,
        });
    }

    // ── Leaderboards ─────────────────────────────────────────────────────

    /// <summary>Verified stat-channel metadata. The 2020 watch's
    /// <c>ConditionalSetStat_ReturningNewValue</c> handles the
    /// high-vs-low-is-better decision locally, so the server doesn't
    /// enforce direction — this is purely for the admin SPA to render
    /// channels sensibly.
    ///
    /// Only entries cross-referenced against decompilation live here.
    /// Channel 7 is verified by
    /// <c>StuntRunnerManager.get_TotalTimeStatChannelForLeaderboard</c>
    /// (StuntRunnerManager.txt:220-228 returns the literal 7); the
    /// stored value is milliseconds, hence <c>ValueFormat = "time-ms"</c>.
    /// Other channels intentionally stay un-named — they used to ship
    /// with best-effort guesses ("Paintball wins", "Dodgeball wins",
    /// etc.) that were almost certainly wrong, so the admin UI now
    /// renders unknown channels as "Channel N" rather than misleading
    /// labels. Add new entries here only when you've verified the
    /// channel mapping in the decompiled client.</summary>
    private static readonly Dictionary<int, StatChannelMeta> KnownStatChannels = new()
    {
        [7] = new("Stunt Runner total time", true, "time-ms"),
    };

    private sealed record StatChannelMeta(string Name, bool LowerIsBetter, string ValueFormat);

    /// <summary>GET <c>api/admin/v1/leaderboards/channels</c> — summary
    /// of every stat channel that has at least one row. Used by the
    /// Leaderboards SPA tab to populate the channel picker.</summary>
    [HttpGet("leaderboards/channels")]
    public async Task<ActionResult> ListChannels()
    {
        var rows = await db.LeaderboardStats
            .GroupBy(s => s.StatChannel)
            .Select(g => new
            {
                Channel = g.Key,
                EntryCount = g.Count(),
                MinValue = g.Min(x => x.Value),
                MaxValue = g.Max(x => x.Value),
            })
            .OrderBy(r => r.Channel)
            .ToListAsync();
        // DB-defined meta beats hardcoded KnownStatChannels so admins
        // can rename / reassign channels live without a code change.
        var dbMeta = await db.LeaderboardChannelMeta.ToDictionaryAsync(c => c.Channel);
        var labelled = rows.Select(r =>
        {
            dbMeta.TryGetValue(r.Channel, out var live);
            KnownStatChannels.TryGetValue(r.Channel, out var fallback);
            return new
            {
                r.Channel,
                Name = live?.Name is { Length: > 0 } n ? n : (fallback?.Name ?? $"Channel {r.Channel}"),
                RoomId = live?.RoomId ?? 0,
                LowerIsBetter = live?.LowerIsBetter ?? fallback?.LowerIsBetter ?? false,
                ValueFormat = live?.ValueFormat ?? fallback?.ValueFormat ?? "count",
                Verified = live is not null || fallback is not null,
                r.EntryCount,
                r.MinValue,
                r.MaxValue,
            };
        }).ToList();
        return Ok(new
        {
            Channels = labelled,
            // Surface the known set as well so the SPA can offer
            // "create a row in channel X" even when the channel is
            // currently empty.
            Known = KnownStatChannels.Select(kv => new
            {
                Channel = kv.Key,
                kv.Value.Name,
                kv.Value.LowerIsBetter,
                kv.Value.ValueFormat,
            }),
        });
    }

    /// <summary>GET <c>api/admin/v1/leaderboards/{channel}</c> — every
    /// stat row in a channel with the player's username, sorted by
    /// the channel's natural direction (lower-is-better channels
    /// sorted ascending, everything else descending).</summary>
    [HttpGet("leaderboards/{channel:int}")]
    public async Task<ActionResult> ChannelDetail(int channel, [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 1000);
        var liveMeta = await db.LeaderboardChannelMeta.FirstOrDefaultAsync(c => c.Channel == channel);
        KnownStatChannels.TryGetValue(channel, out var fallback);
        var name = liveMeta?.Name is { Length: > 0 } ln ? ln : (fallback?.Name ?? $"Channel {channel}");
        var lowerIsBetter = liveMeta?.LowerIsBetter ?? fallback?.LowerIsBetter ?? false;
        var valueFormat = liveMeta?.ValueFormat ?? fallback?.ValueFormat ?? "count";
        var query = db.LeaderboardStats
            .Where(s => s.StatChannel == channel);
        query = lowerIsBetter
            ? query.OrderBy(s => s.Value)
            : query.OrderByDescending(s => s.Value);

        var rows = await (from s in query.Take(take)
                          join p in db.Players on s.PlayerId equals p.Id into pj
                          from p in pj.DefaultIfEmpty()
                          select new
                          {
                              s.Id,
                              s.PlayerId,
                              Username = p == null ? null : p.Username,
                              DisplayName = p == null ? null : p.DisplayName,
                              s.Value,
                              s.UpdatedAt,
                          }).ToListAsync();
        return Ok(new
        {
            Channel = channel,
            Name = name,
            RoomId = liveMeta?.RoomId ?? 0,
            LowerIsBetter = lowerIsBetter,
            ValueFormat = valueFormat,
            Verified = liveMeta is not null || fallback is not null,
            Entries = rows.Select((r, i) => new
            {
                Rank = i + 1,
                r.Id,
                r.PlayerId,
                r.Username,
                r.DisplayName,
                r.Value,
                r.UpdatedAt,
            }),
        });
    }

    public sealed record SetLeaderboardScoreRequest(long PlayerId, int Value);

    /// <summary>POST <c>api/admin/v1/leaderboards/{channel}/score</c>
    /// — upsert a single player's score in a channel.</summary>
    [HttpPost("leaderboards/{channel:int}/score")]
    public async Task<ActionResult> SetScore(int channel, [FromBody] SetLeaderboardScoreRequest body)
    {
        var row = await db.LeaderboardStats.FirstOrDefaultAsync(s =>
            s.PlayerId == body.PlayerId && s.StatChannel == channel);
        if (row is null)
        {
            row = new LeaderboardStatEntity
            {
                PlayerId = body.PlayerId,
                StatChannel = channel,
                Value = body.Value,
            };
            db.LeaderboardStats.Add(row);
        }
        else
        {
            row.Value = body.Value;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await LogAsync("set_leaderboard_score", "player", body.PlayerId, $"channel={channel} value={body.Value}");
        await db.SaveChangesAsync();
        return Ok(new { row.PlayerId, row.StatChannel, row.Value });
    }

    [HttpDelete("leaderboards/{channel:int}/score/{playerId:long}")]
    public async Task<ActionResult> DeleteScore(int channel, long playerId)
    {
        var row = await db.LeaderboardStats.FirstOrDefaultAsync(s =>
            s.PlayerId == playerId && s.StatChannel == channel);
        if (row is null) return NotFound();
        db.LeaderboardStats.Remove(row);
        await LogAsync("delete_leaderboard_score", "player", playerId, $"channel={channel}");
        await db.SaveChangesAsync();
        return Ok(new { deleted = playerId, channel });
    }

    /// <summary>DELETE <c>api/admin/v1/leaderboards/{channel}</c> —
    /// wipe every entry in a stat channel. Used for season resets or
    /// to clear test data.</summary>
    [HttpDelete("leaderboards/{channel:int}")]
    public async Task<ActionResult> WipeChannel(int channel)
    {
        var rows = await db.LeaderboardStats.Where(s => s.StatChannel == channel).ToListAsync();
        if (rows.Count == 0) return Ok(new { wiped = 0, channel });
        db.LeaderboardStats.RemoveRange(rows);
        await LogAsync("wipe_leaderboard_channel", "system", channel, $"entries={rows.Count}");
        await db.SaveChangesAsync();
        return Ok(new { wiped = rows.Count, channel });
    }

    // ── Rec Room Originals (RROs) ────────────────────────────────────────

    /// <summary>GET <c>api/admin/v1/rooms/originals</c> — the canonical
    /// Rec Room Originals (rooms whose <c>CreatorPlayerId ==
    /// PlayerService.SystemAccountId</c>). Backs the RRO admin tab
    /// which lets admins flip per-room properties (cloning, max
    /// players, description) without scrolling through the global
    /// rooms list.</summary>
    [HttpGet("rooms/originals")]
    public async Task<ActionResult> ListOriginals()
    {
        var rooms = await db.Rooms
            .Where(r => r.CreatorPlayerId == PlayerService.SystemAccountId)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.ImageName,
                r.Accessibility,
                r.CloningAllowed,
                r.IsAGRoom,
                r.IsDormRoom,
                r.State,
                r.TagsCsv,
                r.HotScore,
                r.VisitCount,
                r.VisitorCount,
                r.CheerCount,
                r.CurrentDataBlobName,
                SceneCount = db.RoomScenes.Count(s => s.RoomId == r.Id),
                BlobCount = db.RoomDataBlobs.Count(b => b.RoomId == r.Id),
            })
            .ToListAsync();
        return Ok(rooms);
    }

    public sealed record UpdateRoomPropsRequest(
        string? Description,
        bool? CloningAllowed,
        int? Accessibility,
        string? TagsCsv,
        double? HotScore,
        int? State,
        string? ImageName);

    /// <summary>POST <c>api/admin/v1/rooms/{id}/props</c> — partial
    /// update of room properties. Only the non-null fields in the
    /// body get written; the rest are left untouched. Backs the
    /// RRO edit form and is also usable for regular custom rooms.</summary>
    [HttpPost("rooms/{id:long}/props")]
    public async Task<ActionResult> UpdateRoomProps(long id, [FromBody] UpdateRoomPropsRequest body)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();
        var changes = new List<string>();
        if (body.Description is string desc)
        {
            room.Description = desc;
            changes.Add($"description={desc.Length} chars");
        }
        if (body.CloningAllowed is bool c) { room.CloningAllowed = c; changes.Add($"cloning={c}"); }
        if (body.Accessibility is int a)   { room.Accessibility = a;   changes.Add($"accessibility={a}"); }
        if (body.TagsCsv is string tags)   { room.TagsCsv = tags;       changes.Add($"tags={tags}"); }
        if (body.HotScore is double h)     { room.HotScore = h;         changes.Add($"hot={h}"); }
        if (body.State is int st)          { room.State = st;           changes.Add($"state={st}"); }
        if (body.ImageName is string img)  { room.ImageName = img;      changes.Add($"image={img}"); }

        if (changes.Count == 0) return Ok(new { room.Id, unchanged = true });

        await LogAsync("update_room_props", "room", id, string.Join(", ", changes));
        await db.SaveChangesAsync();
        return Ok(new { room.Id, room.Name, applied = changes });
    }

    // ── Room detail (unified per-room view backing the admin SPA) ─────────

    /// <summary>GET <c>api/admin/v1/rooms/{id}</c> — every column on the
    /// room plus owner/co-owner/mod/host display info, blob count, scene
    /// count, current data blob name, and the latest CheerCount. The
    /// admin SPA's per-room detail page consumes this in one shot rather
    /// than fanning out to the legacy <c>/rooms</c>, <c>/rooms/originals</c>,
    /// <c>/playerReputation</c> endpoints separately.</summary>
    [HttpGet("rooms/{id:long}")]
    public async Task<ActionResult> GetRoomDetail(long id)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();

        var sceneCount = await db.RoomScenes.CountAsync(s => s.RoomId == id);
        var blobCount = await db.RoomDataBlobs.CountAsync(b => b.RoomId == id);
        var roles = await db.RoomRoles
            .Where(r => r.RoomId == id)
            .OrderBy(r => r.Role).ThenBy(r => r.GrantedAt)
            .ToListAsync();

        // Hydrate display names for creator + every role grant in one
        // hit so the UI doesn't have to follow up with /players/{id}.
        var playerIds = roles.Select(r => r.PlayerId)
            .Append(room.CreatorPlayerId)
            .Distinct()
            .ToList();
        var players = await db.Players
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Username, p.DisplayName, p.ProfileImageName })
            .ToListAsync();

        object PlayerInfo(long pid) =>
            players.FirstOrDefault(p => p.Id == pid) is { } p
                ? new { id = p.Id, username = p.Username, displayName = p.DisplayName, profileImageName = p.ProfileImageName }
                : new { id = pid, username = $"#{pid}", displayName = "", profileImageName = (string?)null };

        return Ok(new
        {
            room.Id,
            room.Name,
            room.Description,
            room.ImageName,
            room.State,
            room.Accessibility,
            room.IsAGRoom,
            room.IsDormRoom,
            room.CloningAllowed,
            room.SupportsLevelVoting,
            room.SupportsVRLow,
            room.SupportsMobile,
            room.SupportsScreens,
            room.SupportsWalkVR,
            room.SupportsTeleportVR,
            room.AllowsJuniors,
            room.DisableMicAutoMute,
            room.RoomWarningMask,
            room.CustomRoomWarning,
            room.TagsCsv,
            room.CheerCount,
            room.FavoriteCount,
            room.VisitCount,
            room.VisitorCount,
            room.HotScore,
            room.LocationReplicationId,
            room.CurrentDataBlobName,
            room.CreatedAt,
            room.UpdatedAt,
            sceneCount,
            blobCount,
            owner = PlayerInfo(room.CreatorPlayerId),
            roles = roles.Select(r => new
            {
                r.Id,
                r.PlayerId,
                r.Role,
                r.Accepted,
                r.GrantedByPlayerId,
                r.GrantedAt,
                player = PlayerInfo(r.PlayerId),
            }).ToList(),
        });
    }

    public sealed record AddRoleRequest(long PlayerId, int Role, bool Accepted = true);

    /// <summary>POST <c>api/admin/v1/rooms/{id}/roles</c> — grant a
    /// (PlayerId, Role) pair on the room. Role: 0=CoOwner, 1=Moderator,
    /// 2=Host. Idempotent — re-granting the same role flips Accepted
    /// only.</summary>
    [HttpPost("rooms/{id:long}/roles")]
    public async Task<ActionResult> AddRoomRole(long id, [FromBody] AddRoleRequest body)
    {
        if (body is null) return BadRequest("missing body");
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();
        if (body.PlayerId <= 0) return BadRequest("missing player");
        if (body.Role is < 0 or > 2) return BadRequest("invalid role");

        var existing = await db.RoomRoles.FirstOrDefaultAsync(r =>
            r.RoomId == id && r.PlayerId == body.PlayerId && r.Role == body.Role);
        if (existing is null)
        {
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = id,
                PlayerId = body.PlayerId,
                Role = body.Role,
                Accepted = body.Accepted,
                GrantedByPlayerId = CurrentAdminId,
            });
        }
        else
        {
            existing.Accepted = body.Accepted;
        }
        await LogAsync("room_role_add", "room", id, $"player={body.PlayerId} role={body.Role}");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>DELETE <c>api/admin/v1/rooms/{id}/roles/{playerId}/{role}</c>
    /// — revoke a specific role grant. No-op if the row doesn't exist.</summary>
    [HttpDelete("rooms/{id:long}/roles/{playerId:long}/{role:int}")]
    public async Task<ActionResult> RemoveRoomRole(long id, long playerId, int role)
    {
        var row = await db.RoomRoles.FirstOrDefaultAsync(r =>
            r.RoomId == id && r.PlayerId == playerId && r.Role == role);
        if (row is null) return Ok(new { ok = true, deleted = 0 });
        db.RoomRoles.Remove(row);
        await LogAsync("room_role_remove", "room", id, $"player={playerId} role={role}");
        await db.SaveChangesAsync();
        return Ok(new { ok = true, deleted = 1 });
    }

    public sealed record TransferOwnerRequest(long NewCreatorPlayerId);

    /// <summary>POST <c>api/admin/v1/rooms/{id}/owner</c> — reassign the
    /// room's CreatorPlayerId. The previous owner is automatically added
    /// as a CoOwner so the original creator keeps a foothold on their
    /// own room. Reject self-transfers.</summary>
    [HttpPost("rooms/{id:long}/owner")]
    public async Task<ActionResult> TransferOwner(long id, [FromBody] TransferOwnerRequest body)
    {
        if (body is null || body.NewCreatorPlayerId <= 0) return BadRequest("missing new owner");
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId == body.NewCreatorPlayerId) return Ok(new { ok = true, unchanged = true });

        var previousOwner = room.CreatorPlayerId;
        room.CreatorPlayerId = body.NewCreatorPlayerId;
        room.UpdatedAt = DateTime.UtcNow;

        // Auto-grant the former owner CoOwner (id=0) so they don't lose
        // access to a room they built. Idempotent — re-running this in
        // a fast undo-redo flow won't accumulate dupes.
        if (previousOwner > 0 && !await db.RoomRoles.AnyAsync(r =>
                r.RoomId == id && r.PlayerId == previousOwner && r.Role == 0))
        {
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = id,
                PlayerId = previousOwner,
                Role = 0,
                Accepted = true,
                GrantedByPlayerId = CurrentAdminId,
            });
        }

        await LogAsync("room_transfer_owner", "room", id, $"from={previousOwner} to={body.NewCreatorPlayerId}");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>GET <c>api/admin/v1/rooms/{id}/instances</c> — active
    /// Photon instances for one specific room. Subset of the global
    /// <c>/instances</c> endpoint pre-filtered to one room; the admin
    /// SPA's room detail page embeds these so admins don't have to
    /// scroll a global instance list looking for matches.
    ///
    /// The <c>masterPlayerId</c> field is best-effort: Photon's master
    /// is whoever has the lowest <c>ActorNumber</c> in the room (i.e.
    /// the first to join). The server doesn't see ActorNumbers, but
    /// the player with the lowest DorkNet player id among current
    /// participants is a reasonable proxy on dorms (creator joined
    /// first) and pretty good on user rooms. For non-dorm rooms with
    /// the creator absent, the value just identifies a stable "lead"
    /// participant — the room won't fall apart if it's not the actual
    /// Photon master, and admins primarily use the badge to know
    /// which row to click "Pull in" against to land in the right
    /// shard.</summary>
    [HttpGet("rooms/{id:long}/instances")]
    public async Task<ActionResult> GetRoomInstances(long id)
    {
        var room = await db.Rooms.Where(r => r.Id == id)
            .Select(r => new { r.Id, r.CreatorPlayerId, r.IsDormRoom })
            .FirstOrDefaultAsync();

        var onlinePids = onlinePresence.OnlinePlayerIds().ToList();
        var byInstance = new Dictionary<long, (long roomInstanceId, long roomId, long subRoomId, string roomName, string photonRoomId, string photonRegionId, string location, int maxCapacity, bool isPrivate, List<long> pids)>();
        foreach (var pid in onlinePids)
        {
            var r = playerPresence.GetRoom(pid);
            if (r is null || r.RoomId != id) continue;
            if (!byInstance.TryGetValue(r.RoomInstanceId, out var slot))
            {
                slot = (r.RoomInstanceId, r.RoomId, r.SubRoomId, r.Name ?? "", r.PhotonRoomId ?? "", r.PhotonRegionId ?? "", r.Location ?? "", r.MaxCapacity, r.IsPrivate, new List<long>());
            }
            slot.pids.Add(pid);
            byInstance[r.RoomInstanceId] = slot;
        }
        if (byInstance.Count == 0) return Ok(Array.Empty<object>());

        var allPids = byInstance.Values.SelectMany(v => v.pids).Distinct().ToList();
        var players = await db.Players
            .Where(p => allPids.Contains(p.Id))
            .Select(p => new { p.Id, p.Username, p.DisplayName })
            .ToDictionaryAsync(p => p.Id);

        return Ok(byInstance.Values.Select(i =>
        {
            // Master heuristic: dorm owner if present, else lowest pid.
            long master;
            if (room is { IsDormRoom: true } && i.pids.Contains(room.CreatorPlayerId))
                master = room.CreatorPlayerId;
            else
                master = i.pids.OrderBy(p => p).FirstOrDefault();
            return new
            {
                roomInstanceId = i.roomInstanceId,
                roomId = i.roomId,
                subRoomId = i.subRoomId,
                roomName = i.roomName,
                photonRoomId = i.photonRoomId,
                photonRegionId = i.photonRegionId,
                location = i.location,
                maxCapacity = i.maxCapacity,
                isPrivate = i.isPrivate,
                masterPlayerId = master,
                participants = i.pids.Select(pid => players.TryGetValue(pid, out var p)
                    ? new { id = p.Id, username = p.Username, displayName = p.DisplayName, isMaster = pid == master }
                    : new { id = pid, username = $"#{pid}", displayName = "", isMaster = pid == master }).ToList(),
            };
        }));
    }

    public sealed record PullIntoInstanceRequest(long PlayerId);

    /// <summary>POST <c>api/admin/v1/rooms/{id}/instances/{instanceId}/pull</c>
    /// — pull a specific player into one already-active instance of this
    /// room. Wraps the existing player force-join flow but pre-fills the
    /// instance and validates that the target instance is actually live
    /// in this room. Useful for "drop me into the shard the streamer is
    /// already in" without having to dig the instance id out by hand.</summary>
    [HttpPost("rooms/{id:long}/instances/{instanceId:long}/pull")]
    public async Task<ActionResult> PullIntoInstance(long id, long instanceId, [FromBody] PullIntoInstanceRequest body)
    {
        if (body is null || body.PlayerId <= 0) return BadRequest("missing player");

        // Find any participant currently in the target instance — we
        // need their RoomInstanceDto to know which Photon shard to
        // route the pulled player into. If nobody's online in the
        // instance anymore the instance has effectively died; reject.
        DorkNet.Server.Controllers.Match.RoomInstanceDto? sample = null;
        foreach (var pid in onlinePresence.OnlinePlayerIds())
        {
            var r = playerPresence.GetRoom(pid);
            if (r is not null && r.RoomId == id && r.RoomInstanceId == instanceId)
            {
                sample = r;
                break;
            }
        }
        if (sample is null) return NotFound(new { error = "instance_not_active" });

        // Re-stamp presence on the target so their next heartbeat /
        // goto sees us pointing at this instance. Push a SignalR
        // SubscriptionUpdateGameSession so the watch's matchmaking
        // updates LocalRoomInstance immediately.
        playerPresence.SetRoom(body.PlayerId, sample);
        await notifications.NotifyAsync(body.PlayerId,
            PushNotificationId.SubscriptionUpdateGameSession,
            sample);
        await LogAsync("instance_pull", "instance", instanceId, $"player={body.PlayerId} room={id}");
        await db.SaveChangesAsync();
        return Ok(new { ok = true, instanceId, roomInstanceId = sample.RoomInstanceId });
    }

    /// <summary>POST <c>api/admin/v1/rooms/{id}/instances/{instanceId}/close</c>
    /// — kick every player currently in the instance via ModerationKick
    /// pushes and clear their presence so the server-side view matches.
    /// The Photon room itself isn't directly destroyed (we don't have
    /// Photon admin credentials); it dies on its own once the last
    /// participant disconnects. In practice every kick fires within
    /// the same second, so the room is empty seconds later.</summary>
    [HttpPost("rooms/{id:long}/instances/{instanceId:long}/close")]
    public async Task<ActionResult> CloseInstance(long id, long instanceId, [FromBody] KickRequest? body)
    {
        var reason = body?.Reason ?? "Instance closed by admin";
        var kicked = new List<long>();
        foreach (var pid in onlinePresence.OnlinePlayerIds().ToList())
        {
            var r = playerPresence.GetRoom(pid);
            if (r is null || r.RoomId != id || r.RoomInstanceId != instanceId) continue;
            await notifications.NotifyAsync(pid, PushNotificationId.ModerationKick, new { Reason = reason });
            playerPresence.Clear(pid);
            kicked.Add(pid);
        }
        if (kicked.Count > 0)
        {
            await LogAsync("instance_close", "instance", instanceId, $"room={id} kicked={kicked.Count}");
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true, kicked });
    }

    /// <summary>GET <c>api/admin/v1/rooms/{id}/leaderboards</c> — every
    /// stat channel mapped to this room via
    /// <see cref="LeaderboardChannelMetaEntity"/>, plus the row count
    /// per channel and the top scorer. Channels that report scores but
    /// haven't been mapped yet show up in
    /// <see cref="LeaderboardOrphans"/> instead.</summary>
    [HttpGet("rooms/{id:long}/leaderboards")]
    public async Task<ActionResult> GetRoomLeaderboards(long id)
    {
        var metas = await db.LeaderboardChannelMeta
            .Where(c => c.RoomId == id)
            .OrderBy(c => c.Channel)
            .ToListAsync();
        if (metas.Count == 0) return Ok(Array.Empty<object>());

        var channels = metas.Select(m => m.Channel).ToList();
        var counts = await db.LeaderboardStats
            .Where(s => channels.Contains(s.StatChannel))
            .GroupBy(s => s.StatChannel)
            .Select(g => new { Channel = g.Key, Count = g.Count(), Best = g.Max(x => x.Value), Worst = g.Min(x => x.Value) })
            .ToListAsync();

        return Ok(metas.Select(m =>
        {
            var stat = counts.FirstOrDefault(c => c.Channel == m.Channel);
            return new
            {
                m.Channel,
                m.Name,
                m.LowerIsBetter,
                m.ValueFormat,
                EntryCount = stat?.Count ?? 0,
                BestValue = stat is null ? (long?)null : (m.LowerIsBetter ? stat.Worst : stat.Best),
            };
        }));
    }

    public sealed record UpsertChannelMetaRequest(int Channel, long RoomId, string Name, bool LowerIsBetter, string? ValueFormat);

    /// <summary>POST <c>api/admin/v1/leaderboards/meta</c> — register or
    /// rename a stat channel + assign it to a room. Idempotent; updates
    /// the existing row when the channel already has metadata. Does NOT
    /// touch the value rows themselves — admins can register a channel
    /// before any scores are reported.</summary>
    [HttpPost("leaderboards/meta")]
    public async Task<ActionResult> UpsertChannelMeta([FromBody] UpsertChannelMetaRequest body)
    {
        if (body is null) return BadRequest("missing body");
        var row = await db.LeaderboardChannelMeta.FirstOrDefaultAsync(c => c.Channel == body.Channel);
        if (row is null)
        {
            row = new LeaderboardChannelMetaEntity { Channel = body.Channel };
            db.LeaderboardChannelMeta.Add(row);
        }
        row.RoomId = body.RoomId;
        row.Name = body.Name ?? string.Empty;
        row.LowerIsBetter = body.LowerIsBetter;
        row.ValueFormat = string.IsNullOrWhiteSpace(body.ValueFormat) ? "count" : body.ValueFormat;
        row.UpdatedAt = DateTime.UtcNow;
        await LogAsync("leaderboard_meta_upsert", "channel", body.Channel, $"room={body.RoomId} name={row.Name}");
        await db.SaveChangesAsync();
        return Ok(new { row.Channel, row.RoomId, row.Name, row.LowerIsBetter, row.ValueFormat });
    }

    /// <summary>DELETE <c>api/admin/v1/leaderboards/meta/{channel}</c> —
    /// remove the metadata row. The channel goes back to showing as
    /// "Channel N" / unscoped. Existing score rows are kept.</summary>
    [HttpDelete("leaderboards/meta/{channel:int}")]
    public async Task<ActionResult> DeleteChannelMeta(int channel)
    {
        var row = await db.LeaderboardChannelMeta.FirstOrDefaultAsync(c => c.Channel == channel);
        if (row is null) return Ok(new { ok = true, deleted = 0 });
        db.LeaderboardChannelMeta.Remove(row);
        await LogAsync("leaderboard_meta_remove", "channel", channel, "");
        await db.SaveChangesAsync();
        return Ok(new { ok = true, deleted = 1 });
    }

    /// <summary>GET <c>api/admin/v1/leaderboards/orphans</c> — every
    /// stat channel id that has at least one score row but no meta row.
    /// Used by the admin Leaderboards UI to surface "you have data for
    /// channel 11 — want to give it a name and assign it to a room?".
    /// Critical for figuring out which channels Stunt Runner courses
    /// actually use: load a course, drive it, then orphans surfaces
    /// the channel id the game just reported.</summary>
    [HttpGet("leaderboards/orphans")]
    public async Task<ActionResult> LeaderboardOrphans()
    {
        var mapped = await db.LeaderboardChannelMeta.Select(c => c.Channel).ToListAsync();
        var rows = await db.LeaderboardStats
            .Where(s => !mapped.Contains(s.StatChannel))
            .GroupBy(s => s.StatChannel)
            .Select(g => new { Channel = g.Key, EntryCount = g.Count(), LastSeen = g.Max(s => s.UpdatedAt) })
            .OrderByDescending(r => r.LastSeen)
            .ToListAsync();
        return Ok(rows);
    }

    // ── Server settings (runtime toggles) ────────────────────────────────

    /// <summary>GET <c>api/admin/v1/settings</c> — current server-wide
    /// toggle state. Single row, single round trip.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult> GetServerSettings()
    {
        var row = await serverSettings.GetAsync();
        return Ok(new
        {
            row.SignupsDisabled,
            row.WeeklyChallengesCompletedRequired,
            row.UpdatedAt,
        });
    }

    public sealed record SignupsToggleRequest(bool Disabled);

    /// <summary>POST <c>api/admin/v1/settings/signups</c> — flip the
    /// server-wide account-creation kill switch. While disabled, both
    /// <c>POST /account/create</c> and <c>POST /api/account/v1/create</c>
    /// reply with <c>Success=false</c> + a user-facing error string and
    /// the watch surfaces that to whoever tried to sign up. Existing
    /// logins are untouched.</summary>
    [HttpPost("settings/signups")]
    public async Task<ActionResult> SetSignupsDisabled([FromBody] SignupsToggleRequest body)
    {
        var row = await serverSettings.SetSignupsDisabledAsync(body.Disabled);
        await LogAsync(body.Disabled ? "signups_disabled" : "signups_enabled", "system", 0, "");
        await db.SaveChangesAsync();
        return Ok(new
        {
            row.SignupsDisabled,
            row.WeeklyChallengesCompletedRequired,
            row.UpdatedAt,
        });
    }

    // ── Weekly challenges ────────────────────────────────────────────────

    /// <summary>GET <c>api/admin/v1/settings/weekly-challenges</c> — the
    /// current weekly slate + reward the watch's challenge map is built
    /// from (<see cref="ProgressionApi.ProgressionController"/>).</summary>
    [HttpGet("settings/weekly-challenges")]
    public async Task<ActionResult> GetWeeklyChallenges()
    {
        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        return Ok(weekly);
    }

    /// <summary>GET
    /// <c>api/admin/v1/settings/weekly-challenges/reward-options</c> —
    /// the store items that can be assigned as the weekly gift. Only
    /// avatar outfits and consumables are offered: the 2020.03
    /// <see cref="StoreService"/> exposes
    /// <c>TryGetAvatarItemPayload</c> / <c>TryGetConsumableItemDesc</c>
    /// but has no equipment payload helper, so equipment gifts aren't
    /// assignable on this build. Each option carries the descriptors the
    /// watch's <c>ChallengeGift</c> render needs plus the
    /// <c>Slug</c> the server grants on completion.</summary>
    [HttpGet("settings/weekly-challenges/reward-options")]
    public async Task<ActionResult> GetWeeklyChallengeRewardOptions()
    {
        var rows = await db.StoreItems
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayName)
            .ToListAsync();

        static object Option(
            string kind,
            StoreItemEntity item,
            string avatarItemDesc = "",
            string consumableItemDesc = "") => new
        {
            Kind = kind,
            Label = $"{item.DisplayName} ({item.Slug})",
            Slug = item.Slug,
            GiftDropId = (int)(item.Id & 0x7fffffff),
            AvatarItemDesc = avatarItemDesc,
            ConsumableItemDesc = consumableItemDesc,
        };

        var avatarItems = rows
            .Select(i => StoreService.TryGetAvatarItemPayload(i.Slug, out _, out var desc)
                ? Option("avatar", i, avatarItemDesc: desc)
                : null)
            .Where(o => o is not null)
            .ToArray();

        var consumables = rows
            .Select(i => StoreService.TryGetConsumableItemDesc(i.Slug, out var desc)
                ? Option("consumable", i, consumableItemDesc: desc)
                : null)
            .Where(o => o is not null)
            .ToArray();

        return Ok(new
        {
            AvatarItems = avatarItems,
            Consumables = consumables,
        });
    }

    public sealed record WeeklyChallengeSettingsRequest(
        bool? CompletedRequired,
        List<WeeklyChallengeTemplate>? Challenges,
        WeeklyChallengeReward? Reward);

    /// <summary>POST <c>api/admin/v1/settings/weekly-challenges</c> —
    /// replace the weekly slate + reward. Normalisation (indexing,
    /// trimming, the Take(10) cap) happens in
    /// <see cref="ServerSettingsService.SetWeeklyChallengesAsync"/>.</summary>
    [HttpPost("settings/weekly-challenges")]
    public async Task<ActionResult> SetWeeklyChallenges([FromBody] WeeklyChallengeSettingsRequest body)
    {
        var completedRequired = body.CompletedRequired ?? true;
        var challenges = body.Challenges ?? ServerSettingsService.DefaultWeeklyChallenges();
        var weekly = await serverSettings.SetWeeklyChallengesAsync(completedRequired, challenges, body.Reward);
        await LogAsync("weekly_challenges_updated", "system", 0, "");
        await db.SaveChangesAsync();
        return Ok(weekly);
    }

    // ── Signup codes ─────────────────────────────────────────────────────

    /// <summary>GET <c>api/admin/v1/signup-codes</c> — every issued code
    /// with its status (unused / redeemed-by-whom / revoked / expired)
    /// for the admin invite tracker.</summary>
    [HttpGet("signup-codes")]
    public async Task<ActionResult> GetSignupCodes([FromQuery] int take = 200)
    {
        var codes = await signupCodes.ListAsync(take);
        var redeemerIds = codes.Where(c => c.RedeemedByPlayerId is not null)
            .Select(c => c.RedeemedByPlayerId!.Value).Distinct().ToList();
        var redeemers = redeemerIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Players.Where(p => redeemerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Username);

        var now = DateTime.UtcNow;
        return Ok(codes.Select(c => new
        {
            c.Id,
            c.Code,
            c.Descriptor,
            c.CreatedAt,
            c.ExpiresAt,
            c.Revoked,
            c.RedeemedAt,
            c.RedeemedByPlayerId,
            RedeemedByUsername = c.RedeemedByPlayerId is { } rid && redeemers.TryGetValue(rid, out var u) ? u : null,
            Status = c.RedeemedByPlayerId is not null ? "redeemed"
                : c.Revoked ? "revoked"
                : c.ExpiresAt is { } e && e <= now ? "expired"
                : "active",
        }));
    }

    public sealed record GenerateSignupCodeRequest(string? Descriptor, DateTime? ExpiresAt);

    /// <summary>POST <c>api/admin/v1/signup-codes</c> — mint a new
    /// single-use code with an optional descriptor + expiry.</summary>
    [HttpPost("signup-codes")]
    public async Task<ActionResult> GenerateSignupCode([FromBody] GenerateSignupCodeRequest body)
    {
        var expires = body.ExpiresAt is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : (DateTime?)null;
        var code = await signupCodes.GenerateAsync(body.Descriptor ?? string.Empty, expires, CurrentAdminId);
        await LogAsync("signup_code_created", "signup_code", code.Id, code.Descriptor);
        await db.SaveChangesAsync();
        return Ok(new { code.Id, code.Code, code.Descriptor, code.CreatedAt, code.ExpiresAt });
    }

    /// <summary>POST <c>api/admin/v1/signup-codes/{id}/revoke</c> — kill an
    /// unused code. Already-redeemed codes can't be revoked.</summary>
    [HttpPost("signup-codes/{id:long}/revoke")]
    public async Task<ActionResult> RevokeSignupCode(long id)
    {
        var ok = await signupCodes.RevokeAsync(id);
        if (!ok) return BadRequest(new { Error = "cannot_revoke" });
        await LogAsync("signup_code_revoked", "signup_code", id, "");
        await db.SaveChangesAsync();
        return Ok(new { Revoked = true });
    }

    // ── Audit log ────────────────────────────────────────────────────────

    [HttpGet("audit")]
    public async Task<ActionResult> GetAuditLog([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await db.AdminActions
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .ToListAsync();
        return Ok(rows);
    }

    // ── Reports moderation ───────────────────────────────────────────────

    /// <summary>List unresolved player reports, oldest-first.
    /// Admins triage from this queue.</summary>
    [HttpGet("reports")]
    public async Task<ActionResult> ListReports([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await db.Reports
            .Where(r => r.ResolvedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows);
    }

    public sealed record ResolveReportRequest(string? Note);

    /// <summary>Mark a report as resolved with an optional note.
    /// The actual moderation action (ban, warn, dismiss) is the
    /// admin's call; this endpoint only closes the report row.</summary>
    [HttpPost("reports/{id:long}/resolve")]
    public async Task<ActionResult> ResolveReport(long id, [FromBody] ResolveReportRequest? body)
    {
        var rep = await db.Reports.FirstOrDefaultAsync(r => r.Id == id);
        if (rep is null) return NotFound();
        if (rep.ResolvedAt is not null) return Ok(new { already_resolved = true });

        rep.ResolvedAt = DateTime.UtcNow;
        rep.ResolverAdminId = CurrentAdminId;
        rep.ResolutionNote = (body?.Note ?? string.Empty).Trim();
        await LogAsync("resolve_report", "report", id, rep.ResolutionNote);
        await db.SaveChangesAsync();
        return Ok(new { rep.Id, rep.ResolvedAt });
    }

    // ── Kick / IP ban ────────────────────────────────────────────────────

    public sealed record KickRequest(string? Reason);

    /// <summary>Force-disconnect a player from the SignalR hub and
    /// signal them to reload via a ModerationKick push. Doesn't ban
    /// — the player can immediately reconnect — but boots them out
    /// of whatever room/session they're in. For temporary
    /// moderation actions where a full ban is too heavy.</summary>
    [HttpPost("players/{id:long}/kick")]
    public async Task<ActionResult> KickPlayer(long id, [FromBody] KickRequest? body)
    {
        await notifications.NotifyAsync(id, PushNotificationId.ModerationKick,
            new { Reason = body?.Reason ?? "" });
        await LogAsync("kick_player", "player", id, body?.Reason ?? "");
        await db.SaveChangesAsync();
        return Ok(new { kicked = id });
    }

    public sealed record IpBanRequest(string Cidr, string? Reason, int? DurationDays);

    /// <summary>Add an IP-level ban. Optional duration in days
    /// (null = permanent until removed). Format: dotted IPv4 or
    /// CIDR like <c>1.2.3.0/24</c>.</summary>
    [HttpPost("ipbans")]
    public async Task<ActionResult> AddIpBan([FromBody] IpBanRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Cidr))
            return BadRequest("missing cidr");

        var until = body.DurationDays is int days
            ? DateTime.UtcNow.AddDays(Math.Clamp(days, 1, 3650))
            : (DateTime?)null;

        var entry = new IpBanEntity
        {
            Cidr = body.Cidr.Trim(),
            Reason = (body.Reason ?? string.Empty).Trim(),
            BannedByAdminId = CurrentAdminId,
            Until = until,
        };
        db.IpBans.Add(entry);
        await LogAsync("ipban_add", "ip", 0, $"{body.Cidr} until {until:o}");
        await db.SaveChangesAsync();
        return Ok(entry);
    }

    [HttpDelete("ipbans/{id:long}")]
    public async Task<ActionResult> RemoveIpBan(long id)
    {
        var ban = await db.IpBans.FirstOrDefaultAsync(b => b.Id == id);
        if (ban is null) return NotFound();
        db.IpBans.Remove(ban);
        await LogAsync("ipban_remove", "ip", id, ban.Cidr);
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("ipbans")]
    public async Task<ActionResult> ListIpBans()
    {
        var rows = await db.IpBans
            .OrderByDescending(b => b.BannedAt)
            .ToListAsync();
        return Ok(rows);
    }

    // ── Stats ────────────────────────────────────────────────────────────

    /// <summary>Real-time server snapshot for the admin dashboard.
    /// Every aggregate is one cheap query — designed to be hit by
    /// the admin watch tab on a 5-second timer without melting the
    /// SQLite write path.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult> Stats()
    {
        var totalPlayers = await db.Players.CountAsync();
        var totalRooms = await db.Rooms.CountAsync();
        var totalInventions = await db.Inventions.CountAsync();
        var openReports = await db.Reports.CountAsync(r => r.ResolvedAt == null);
        var bannedNow = await db.Players.CountAsync(p =>
            p.BannedUntil != null && p.BannedUntil > DateTime.UtcNow);
        var activeIpBans = await db.IpBans.CountAsync(b =>
            b.Until == null || b.Until > DateTime.UtcNow);

        var online = onlinePresence.OnlinePlayerIds().ToArray();

        var topRooms = await db.Rooms
            .OrderByDescending(r => r.VisitCount)
            .Take(10)
            .Select(r => new { r.Id, r.Name, r.VisitCount, r.VisitorCount, r.CheerCount })
            .ToListAsync();

        var recentJoins = await db.Players
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new { p.Id, p.Username, p.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            Players = new
            {
                Total = totalPlayers,
                OnlineNow = online.Length,
                BannedNow = bannedNow,
            },
            Rooms = new { Total = totalRooms, TopByVisits = topRooms },
            Inventions = totalInventions,
            Moderation = new
            {
                OpenReports = openReports,
                ActiveIpBans = activeIpBans,
            },
            RecentJoins = recentJoins,
            ServerTime = DateTime.UtcNow,
        });
    }

    // ── Avatar / outfit / inventory grants ──────────────────────────────

    public sealed record OutfitRequest(string? OutfitSelections, string? FaceFeatures, string? HairColor, string? SkinColor);

    /// <summary>Get a player's current avatar so the admin UI can
    /// pre-fill its edit form. Returns the same DTO shape as
    /// <c>GET api/avatar/v2/equipped/{id}</c>.</summary>
    [HttpGet("players/{id:long}/avatar")]
    public async Task<ActionResult> GetAvatar(long id)
    {
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == id);
        if (avatar is null) return NotFound();
        return Ok(new
        {
            avatar.PlayerId,
            avatar.OutfitSelections,
            avatar.FaceFeatures,
            avatar.HairColor,
            avatar.SkinColor,
            avatar.UpdatedAt,
        });
    }

    /// <summary>POST <c>api/admin/v1/players/{id}/avatar/reset</c> —
    /// wipe a player's equipped outfit + inventory back to the canonical
    /// 2020 starter values. Recovery path for the
    /// "watch crashes after ~10 minutes" symptom where a player has an
    /// item GUID (e.g. <c>Eye_MirrorShades_PinkClear</c>) whose
    /// outfits_assets bundle isn't shipped with this build, so
    /// <see cref="OutfitManager"/>.SpawnOutfitImposter throws a
    /// Dependency Exception from Addressables and the engine eventually
    /// tears down the local player. Resetting clears those entries
    /// from <see cref="AvatarEntity.OutfitSelections"/> and
    /// <see cref="AvatarEntity.InventoryJson"/> so the next avatar
    /// load only references items the local build has on disk.</summary>
    [HttpPost("players/{id:long}/avatar/reset")]
    public async Task<ActionResult> ResetAvatar(long id)
    {
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == id);
        if (avatar is null)
        {
            avatar = new AvatarEntity { PlayerId = id };
            db.Avatars.Add(avatar);
        }
        avatar.OutfitSelections = PlayerService.StarterOutfitSelections;
        avatar.InventoryJson = PlayerService.StarterInventoryJson;
        avatar.HairColor = PlayerService.StarterHairColor;
        avatar.SkinColor = PlayerService.StarterSkinColor;
        avatar.UpdatedAt = DateTime.UtcNow;

        await LogAsync("reset_avatar", "player", id, "");
        await db.SaveChangesAsync();

        // Push a profile update so the affected watch re-fetches its
        // avatar without waiting for the next scene load.
        await notifications.NotifyAsync(id,
            PushNotificationId.SubscriptionUpdateProfile,
            new { Reason = "AdminAvatarReset" });

        return Ok(new
        {
            avatar.PlayerId,
            avatar.OutfitSelections,
            avatar.HairColor,
            avatar.SkinColor,
        });
    }

    /// <summary>Replace another player's outfit / hair / skin colour.
    /// Pushes <c>SubscriptionUpdateProfile</c> so the affected watch
    /// re-fetches its avatar and the new clothes propagate to other
    /// players in the same room.</summary>
    [HttpPost("players/{id:long}/avatar")]
    public async Task<ActionResult> SetAvatar(long id, [FromBody] OutfitRequest body)
    {
        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == id);
        if (avatar is null)
        {
            avatar = new AvatarEntity { PlayerId = id };
            db.Avatars.Add(avatar);
        }
        if (body.OutfitSelections is not null) avatar.OutfitSelections = body.OutfitSelections;
        if (body.FaceFeatures is not null) avatar.FaceFeatures = body.FaceFeatures;
        if (body.HairColor is not null) avatar.HairColor = body.HairColor;
        if (body.SkinColor is not null) avatar.SkinColor = body.SkinColor;
        avatar.UpdatedAt = DateTime.UtcNow;

        await LogAsync("set_avatar", "player", id, body.OutfitSelections ?? "");
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(id,
            PushNotificationId.SubscriptionUpdateProfile,
            new { Reason = "AdminAvatarUpdate" });

        return Ok(new { avatar.PlayerId, avatar.OutfitSelections, avatar.HairColor, avatar.SkinColor });
    }

    public sealed record GrantItemRequest(string ItemId, int Quantity);

    /// <summary>Append an item GUID to the player's inventory JSON list.
    /// The client treats <c>InventoryJson</c> as the truth source for
    /// what they own; once granted, the next avatar fetch shows the
    /// item available in the customisation menu.</summary>
    [HttpPost("players/{id:long}/inventory/grant")]
    public async Task<ActionResult> GrantItem(long id, [FromBody] GrantItemRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.ItemId)) return BadRequest("missing itemId");
        var qty = Math.Clamp(body.Quantity, 1, 1000);

        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == id);
        if (avatar is null)
        {
            avatar = new AvatarEntity { PlayerId = id };
            db.Avatars.Add(avatar);
        }

        // Inventory wire format: JSON array of { itemId, quantity }. Merge
        // by itemId so repeat grants don't duplicate rows.
        var list = ParseInventory(avatar.InventoryJson);
        var existing = list.FirstOrDefault(e => e.ItemId == body.ItemId);
        if (existing is null)
            list.Add(new InventoryEntry { ItemId = body.ItemId, Quantity = qty });
        else
            existing.Quantity += qty;
        avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(list);
        avatar.UpdatedAt = DateTime.UtcNow;

        await LogAsync("grant_item", "player", id, $"{body.ItemId} x{qty}");
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(id,
            PushNotificationId.SubscriptionUpdateProfile,
            new { Reason = "InventoryGrant", body.ItemId, Quantity = qty });

        return Ok(new { granted = body.ItemId, quantity = qty });
    }

    public sealed record CurrencyRequest(int CurrencyType, long Amount, string? Reason);

    /// <summary>Adjust a player's currency wallet. Positive amounts
    /// grant; negative deduct (clamped to zero by
    /// <see cref="LevelService.GrantCurrencyAsync"/>). The client's
    /// next storefronts/balance fetch returns the new value.</summary>
    [HttpPost("players/{id:long}/currency")]
    public async Task<ActionResult> GrantCurrency(long id, [FromBody] CurrencyRequest body)
    {
        var newBalance = await level.GrantCurrencyAsync(
            id, body.CurrencyType, body.Amount, body.Reason ?? "admin_grant");
        await LogAsync("grant_currency", "player", id,
            $"type={body.CurrencyType} amount={body.Amount}");
        await db.SaveChangesAsync();
        return Ok(new { id, body.CurrencyType, balance = newBalance });
    }

    public sealed record XpRequest(int Amount, string? Reason);

    /// <summary>Grant XP to a player. Triggers level-up rewards via
    /// <see cref="LevelService.AwardXpAsync"/> if the new XP crosses a
    /// level threshold.</summary>
    [HttpPost("players/{id:long}/xp")]
    public async Task<ActionResult> GrantXp(long id, [FromBody] XpRequest body)
    {
        var (lvl, xp) = await level.AwardXpAsync(id, body.Amount, body.Reason ?? "admin_grant");
        await LogAsync("grant_xp", "player", id, $"amount={body.Amount}");
        await db.SaveChangesAsync();
        return Ok(new { id, level = lvl, xp });
    }

    public sealed record ProfileFlagsRequest(
        bool? IsVerified,
        bool? IsDeveloper,
        bool? IsJunior,
        bool? IsCommunityTeam);

    /// <summary>Toggle the verified / developer / community-team /
    /// junior flags on a player profile. Verified shows up as the
    /// blue-checkmark badge on the watch's profile card; IsDeveloper
    /// and IsCommunityTeam both unlock the watch's overhead-badge
    /// slider (see <see cref="DorkNet.Server.Controllers.API.Role.RoleController.IsDeveloper"/>)
    /// — once unlocked, the player picks "Community Team" or
    /// "Developer" themselves from the in-watch settings menu and the
    /// label renders above their head for every other player in the
    /// room. We expose both as separate admin flags so the audit log
    /// preserves the distinction even though the 2020 watch can't
    /// enforce one badge over the other.</summary>
    [HttpPost("players/{id:long}/flags")]
    public async Task<ActionResult> SetFlags(long id, [FromBody] ProfileFlagsRequest body)
    {
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        if (body.IsVerified is bool v) p.IsVerified = v;
        if (body.IsDeveloper is bool d) p.IsDeveloper = d;
        if (body.IsJunior is bool j) p.IsJunior = j;
        if (body.IsCommunityTeam is bool ct) p.IsCommunityTeam = ct;
        await LogAsync("set_flags", "player", id,
            $"verified={p.IsVerified} dev={p.IsDeveloper} communityteam={p.IsCommunityTeam} junior={p.IsJunior}");
        await db.SaveChangesAsync();
        return Ok(new { p.Id, p.IsVerified, p.IsDeveloper, p.IsCommunityTeam, p.IsJunior });
    }

    public sealed record DisplayNameRequest(string DisplayName);

    /// <summary>Force-set a player's display name (admin override of
    /// the normal username-uniqueness flow).</summary>
    [HttpPost("players/{id:long}/displayName")]
    public async Task<ActionResult> SetDisplayName(long id, [FromBody] DisplayNameRequest body)
    {
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        if (string.IsNullOrWhiteSpace(body.DisplayName)) return BadRequest("empty");
        p.DisplayName = body.DisplayName.Trim();
        await LogAsync("set_display_name", "player", id, p.DisplayName);
        await db.SaveChangesAsync();
        return Ok(new { p.Id, p.DisplayName });
    }

    public sealed record UsernameRequest(string Username);

    /// <summary>Force-set a player's username (the unique @handle).
    /// Unlike the normal account flow this changes ONLY the username, not
    /// the display name, and rejects a collision outright rather than
    /// auto-suffixing — the admin types the exact handle they want.
    /// Username rules mirror the account API: 2–24 chars, letters /
    /// digits / underscore / hyphen.</summary>
    [HttpPost("players/{id:long}/username")]
    public async Task<ActionResult> SetUsername(long id, [FromBody] UsernameRequest body)
    {
        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        var name = (body.Username ?? string.Empty).Trim();
        if (name.Length is < 2 or > 24
            || !name.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'))
            return BadRequest(new { Error = "Username must be 2–24 chars: letters, numbers, _ or -." });

        if (await db.Players.AnyAsync(x => x.Username == name && x.Id != id))
            return BadRequest(new { Error = "That username is already taken." });

        var previous = p.Username;
        p.Username = name;
        await LogAsync("set_username", "player", id, $"{previous} -> {name}");
        await db.SaveChangesAsync();
        return Ok(new { p.Id, p.Username });
    }

    private sealed class InventoryEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("itemId")]
        public string ItemId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }

    private static List<InventoryEntry> ParseInventory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<InventoryEntry>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    // ── Avatar catalog (gifting picker source) ───────────────────────────

    /// <summary>GET <c>api/admin/v1/avatar-items</c> — flat list of every
    /// renderable avatar item, keyed by the GUID the inventory grant
    /// endpoint expects. Backs the Gift page's item picker so admins can
    /// browse by friendly name instead of pasting GUIDs by hand. Read-only.
    /// Catalog source is <c>data/avatar_item_lookup.json</c>, parsed once
    /// at startup by <see cref="DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController"/>.</summary>
    [HttpGet("avatar-items")]
    public ActionResult ListAvatarItems()
    {
        var catalog = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.Catalog;
        var safe = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.SafeGuids;
        var rows = catalog
            .Select(kv => new
            {
                Guid = kv.Key,
                Slot = kv.Value.AvatarItemType,
                FriendlyName = kv.Value.FriendlyName,
                Tooltip = kv.Value.Tooltip,
                Rarity = kv.Value.Rarity,
                Safe = safe.Contains(kv.Key),
            })
            .Where(r => r.Safe) // hide GUIDs that would crash the watch's parser
            .OrderBy(r => r.Slot)
            .ThenBy(r => r.FriendlyName)
            .ToList();
        return Ok(rows);
    }

    // ── Send gift package (in-game gift box popup) ────────────────────────

    public sealed record SendGiftRequest(
        string? AvatarItemGuid,
        int? AvatarItemType,    // 0 = Outfit, 1 = HairDye (per RecNet.Avatars+AvatarItemType)
        int? CurrencyType,      // 1 = Tokens, 2 = Coins (0 = none)
        int? Currency,
        int? Xp,
        int? Level,
        string? Message,
        int? Rarity);           // 0 Common, 10 Uncommon, 20 Rare, 30 Epic, 50 Legendary

    /// <summary>POST <c>api/admin/v1/players/{id}/gift</c> — drop a
    /// <see cref="GiftPackageEntity"/> into the recipient's gift inbox.
    /// The watch's <c>RecNet.Avatars.DowloadGiftPackages</c> loop picks
    /// it up on the next poll and shows the standard gift-box popup;
    /// the player taps "open" to fire <c>LocalConsumeGiftPackage</c>,
    /// which hits <see cref="DorkNet.Server.Controllers.API.Avatar.V2.AvatarGiftsController.ConsumeViaBody"/>
    /// and finally writes the avatar item into <c>InventoryJson</c>.
    /// We DO NOT touch InventoryJson here — that bypasses the gift
    /// flow and the player never sees the popup.
    ///
    /// Also pushes a <see cref="PushNotificationId.GiftPackageReceived"/>
    /// over SignalR so an online player sees the toast immediately
    /// without waiting for the next /gifts poll.</summary>
    [HttpPost("players/{id:long}/gift")]
    public async Task<ActionResult> SendGift(long id, [FromBody] SendGiftRequest body)
    {
        var recipient = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (recipient is null) return NotFound();

        var hasItem     = !string.IsNullOrWhiteSpace(body.AvatarItemGuid);
        var hasCurrency = body.CurrencyType is int ct && ct > 0 && body.Currency is int c && c > 0;
        var hasXp       = body.Xp is int xp && xp > 0;
        var hasLevel    = body.Level is int lvl && lvl > 0;
        if (!hasItem && !hasCurrency && !hasXp && !hasLevel)
            return BadRequest(new { error = "empty_gift", message = "Provide at least one of: avatar item, currency, XP, level." });

        // Wire shape MUST match the working /api/avatar/v2/gifts/generate
        // flow exactly. The watch's ParseGiftPackageReceived
        // (Avatars.txt:616) gates on IsValid + SupportsCurrentPlatform,
        // then runs the gift through GiftManager. Two deviations from
        // /generate were breaking us:
        //   1. FromPlayerId set to the admin's id — the watch's P2P-gift
        //      rendering path looks up sender info; if that lookup
        //      doesn't return what the popup renderer expects, the toast
        //      still fires from the SignalR push but the box never
        //      materializes. /generate uses null; we match.
        //   2. IsGifted = true tagged it as a player-to-player gift,
        //      which routes through a different code path entirely
        //      (separate inbox / different popup). /generate leaves it
        //      at the default false; we match.
        // CRITICAL invariants for AvatarItemDescOrHairDyeDesc, both
        // verified against the in-game crash trace:
        //
        //   1. AvatarItemType MUST be null when no avatar item is in the
        //      gift. Setting it to 0 (Outfit) with an empty desc routes
        //      GiftManager.DequeueGift past the HasAvatarItemOrHairDye
        //      gate into OutfitManager.MarkGiftPackageItemAvailableOnCurrentPlatform("")
        //      → AvatarItem.FromRecNetString("") → "empty RecNet string"
        //      AvatarParseException.
        //
        //   2. When there IS an item, AvatarItemDescOrHairDyeDesc must
        //      have at least 4 comma-separated parts. The client's
        //      FromRecNetString(string) splits on `,` then constructs
        //      ArraySegment(parts, 1, 3) — parts.Length < 4 triggers
        //      ArgumentException at ArraySegmentEnumerator.get_Current
        //      (output_log.txt:1567). Bare GUIDs crash; we suffix `,,,`
        //      for three empty default customisations. See
        //      AvatarItemsController.cs:31-40 for the canonical comment.
        var gift = new GiftPackageEntity
        {
            RecipientPlayerId = id,
            FromPlayerId = null,
            AvatarItemType = hasItem ? (body.AvatarItemType ?? 0) : (int?)null,
            AvatarItemDescOrHairDyeDesc = hasItem
                ? NormalizeAvatarItemDesc(body.AvatarItemGuid!.Trim())
                : string.Empty,
            CurrencyType = hasCurrency ? body.CurrencyType!.Value : 0,
            Currency = hasCurrency ? body.Currency!.Value : 0,
            Xp = hasXp ? body.Xp!.Value : 0,
            Level = hasLevel ? body.Level!.Value : 0,
            GiftContext = 0,
            GiftRarity = body.Rarity ?? 0,
            Message = string.IsNullOrWhiteSpace(body.Message)
                ? "A gift from the admins."
                : body.Message.Trim(),
            Platform = -1,
            PackageVariant = "Standard",
            PackageMaterial = string.Empty,
            IsValid = true,
            SupportsCurrentPlatform = true,
        };
        db.GiftPackages.Add(gift);
        await LogAsync("send_gift", "player", id,
            $"item={(hasItem ? gift.AvatarItemDescOrHairDyeDesc : "-")} " +
            $"currency={(hasCurrency ? $"{gift.CurrencyType}×{gift.Currency}" : "-")} " +
            $"xp={gift.Xp} level={gift.Level}");
        await db.SaveChangesAsync();

        // GiftPackageReceivedImmediate (31), NOT GiftPackageReceived (30).
        // GiftManager.OnGiftPackageReceivedEvent (GiftManager.txt:501) takes
        // a `showImmediately` bool — true for id 31, false for id 30. When
        // false the handler returns *without* calling ReceiveGifts, so no
        // box pops. Admin gifts want the box now, not "next time you open
        // the gift inbox", so push 31. The watch's RecNet.Avatars handler
        // registers BOTH ids (Avatars.txt:177 + 192), and 31 is the only
        // one that ends up invoking the GiftManager popup path.
        await notifications.NotifyAsync(id,
            PushNotificationId.GiftPackageReceivedImmediate,
            new
            {
                gift.Id,
                gift.FromPlayerId,
                gift.AvatarItemType,
                AvatarItemDesc = gift.AvatarItemDescOrHairDyeDesc,
                gift.AvatarItemDescOrHairDyeDesc,
                gift.EquipmentPrefabName,
                gift.EquipmentModificationGuid,
                gift.CurrencyType,
                gift.Currency,
                gift.Xp,
                gift.Level,
                gift.GiftContext,
                gift.GiftRarity,
                gift.Message,
                gift.Platform,
                gift.PackageMaterial,
                gift.PackageVariant,
                gift.Consumed,
                gift.IsValid,
                gift.ErrorMessage,
                gift.SupportsCurrentPlatform,
                gift.IsGifted,
            });

        return Ok(new { gift.Id, gift.RecipientPlayerId });
    }

    /// <summary>Pad a bare avatar item GUID with three empty default
    /// customisations so the watch's <c>AvatarItem.FromRecNetString</c>
    /// doesn't crash on <c>ArraySegment(parts, 1, 3)</c> when the
    /// string only contains the GUID. Idempotent — descs that already
    /// have at least 4 comma-separated parts are returned untouched.</summary>
    private static string NormalizeAvatarItemDesc(string descOrGuid)
    {
        if (string.IsNullOrWhiteSpace(descOrGuid)) return string.Empty;
        var commaCount = 0;
        foreach (var ch in descOrGuid) if (ch == ',') commaCount++;
        return commaCount >= 3 ? descOrGuid : descOrGuid + new string(',', 3 - commaCount);
    }

    // ── Pending gift inbox (clean-up) ────────────────────────────────────

    /// <summary>GET <c>api/admin/v1/players/{id}/gifts</c> — list the
    /// recipient's pending (unconsumed) gift packages. Used by the admin
    /// Gift page to surface broken / abandoned gifts that the watch
    /// can't render so we can delete them. Includes consumed ones too
    /// in case we want to audit what's been opened.</summary>
    [HttpGet("players/{id:long}/gifts")]
    public async Task<ActionResult> ListPlayerGifts(long id, [FromQuery] bool includeConsumed = false)
    {
        IQueryable<GiftPackageEntity> q = db.GiftPackages.Where(g => g.RecipientPlayerId == id);
        if (!includeConsumed) q = q.Where(g => !g.Consumed);
        var rows = await q
            .OrderByDescending(g => g.CreatedAt)
            .Take(200)
            .Select(g => new
            {
                g.Id,
                g.RecipientPlayerId,
                g.FromPlayerId,
                g.AvatarItemType,
                g.AvatarItemDescOrHairDyeDesc,
                g.CurrencyType,
                g.Currency,
                g.Xp,
                g.Level,
                g.GiftContext,
                g.GiftRarity,
                g.Message,
                g.Consumed,
                g.ConsumedAt,
                g.IsValid,
                g.IsGifted,
                g.CreatedAt,
            })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>DELETE <c>api/admin/v1/gifts/{id}</c> — hard-delete a
    /// gift package row. Use when a gift is broken (wrong shape, won't
    /// render its box in the watch) or was a mis-fire from a test send.
    /// Does not affect the player's actual inventory if they'd already
    /// consumed it — that write happened during consume and is final.</summary>
    [HttpDelete("gifts/{id:long}")]
    public async Task<ActionResult> DeleteGift(long id)
    {
        var gift = await db.GiftPackages.FirstOrDefaultAsync(g => g.Id == id);
        if (gift is null) return NotFound();
        var recipient = gift.RecipientPlayerId;
        db.GiftPackages.Remove(gift);
        await LogAsync("delete_gift", "gift", id, $"recipient={recipient} consumed={gift.Consumed}");
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    /// <summary>POST <c>api/admin/v1/players/{id}/gifts/clear</c> —
    /// nuke every unconsumed gift for a player. Useful after a burst of
    /// broken admin-test gifts to reset the inbox to a clean state.
    /// Consumed gifts are left alone (they're history, not state).</summary>
    [HttpPost("players/{id:long}/gifts/clear")]
    public async Task<ActionResult> ClearPlayerGifts(long id)
    {
        var pending = await db.GiftPackages
            .Where(g => g.RecipientPlayerId == id && !g.Consumed)
            .ToListAsync();
        db.GiftPackages.RemoveRange(pending);
        await LogAsync("clear_gifts", "player", id, $"removed={pending.Count}");
        await db.SaveChangesAsync();
        return Ok(new { removed = pending.Count });
    }

    // ── Password reset (admin override) ──────────────────────────────────

    public sealed record SetPasswordRequest(string NewPassword);

    /// <summary>Admin-set a new password for any account. Hashes with
    /// BCrypt and persists onto <see cref="PlayerEntity.PasswordHash"/>.
    /// The audit row records the action but never the plaintext.
    /// Refuses passwords shorter than 8 chars — same floor as the
    /// self-service flow.</summary>
    [HttpPost("players/{id:long}/password")]
    public async Task<ActionResult> SetPassword(long id, [FromBody] SetPasswordRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < 8)
            return BadRequest(new { error = "password_too_short", min = 8 });
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();
        player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.NewPassword);
        await LogAsync("set_password", "player", id, "");
        await db.SaveChangesAsync();
        // Push a logout so any active sessions on the old password get
        // dropped — the watch re-authenticates with the new password.
        await notifications.NotifyAsync(id, PushNotificationId.Logout,
            new { Reason = "AdminPasswordReset" });
        return Ok(new { id });
    }

    // ── Live instance viewer ─────────────────────────────────────────────

    /// <summary>GET <c>api/admin/v1/instances</c> — list every active room
    /// instance an online player is currently sitting in. Joins the live
    /// online-player set from <see cref="OnlinePresenceService"/> against
    /// <see cref="PlayerPresenceService"/> (which holds the last-known
    /// <see cref="RoomInstanceDto"/> per player, TTL'd to 45 seconds) and
    /// groups by <c>RoomInstanceId</c>. Empty list when no one is online.</summary>
    [HttpGet("instances")]
    public async Task<ActionResult> ListInstances()
    {
        var onlineIds = onlinePresence.OnlinePlayerIds().ToHashSet();
        if (onlineIds.Count == 0) return Ok(Array.Empty<object>());

        // Bulk-pull names so we can render usernames per participant.
        var nameMap = await db.Players
            .Where(p => onlineIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Username, p.DisplayName })
            .ToDictionaryAsync(p => p.Id, p => new { p.Username, p.DisplayName });

        var roomNameMap = await db.Rooms
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name);

        var grouped = new Dictionary<long, InstanceGroup>();
        foreach (var pid in onlineIds)
        {
            var room = playerPresence.GetRoom(pid);
            if (room is null) continue;
            if (!grouped.TryGetValue(room.RoomInstanceId, out var g))
            {
                g = new InstanceGroup
                {
                    RoomInstanceId = room.RoomInstanceId,
                    RoomId = room.RoomId,
                    SubRoomId = room.SubRoomId,
                    RoomName = roomNameMap.GetValueOrDefault(room.RoomId, $"#{room.RoomId}"),
                    PhotonRoomId = room.PhotonRoomId,
                    PhotonRegionId = room.PhotonRegionId,
                    Location = room.Location,
                    MaxCapacity = room.MaxCapacity,
                    IsPrivate = room.IsPrivate,
                    Participants = new(),
                };
                grouped[room.RoomInstanceId] = g;
            }
            nameMap.TryGetValue(pid, out var nm);
            g.Participants.Add(new InstanceParticipant
            {
                Id = pid,
                Username = nm?.Username ?? $"#{pid}",
                DisplayName = nm?.DisplayName ?? $"#{pid}",
            });
        }

        return Ok(grouped.Values
            .OrderByDescending(g => g.Participants.Count)
            .ThenBy(g => g.RoomName));
    }

    private sealed class InstanceGroup
    {
        public long RoomInstanceId { get; set; }
        public long RoomId { get; set; }
        public long SubRoomId { get; set; }
        public string RoomName { get; set; } = "";
        public string PhotonRoomId { get; set; } = "";
        public string PhotonRegionId { get; set; } = "";
        public string Location { get; set; } = "";
        public int MaxCapacity { get; set; }
        public bool IsPrivate { get; set; }
        public List<InstanceParticipant> Participants { get; set; } = new();
    }

    private sealed class InstanceParticipant
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    // ── Force-join (experimental) ────────────────────────────────────────

    /// <summary>
    /// Force-join body. <c>RoomInstanceId</c> + <c>PhotonRoomId</c> are
    /// optional — when omitted, the watch lands in a fresh matchmade
    /// instance of the room. When provided (from the Instances browser
    /// → "send into this match" flow), we target that exact Photon room
    /// so the admin can drop a player into a specific public or private
    /// instance other players are already in.
    /// </summary>
    public sealed record ForceJoinRequest(
        long RoomId,
        long? SubRoomId,
        long? RoomInstanceId,
        string? PhotonRoomId,
        string? PhotonRegionId);

    /// <summary>POST <c>api/admin/v1/players/{id}/forcejoin</c> — rewrite
    /// the target player's <see cref="PlayerPresenceService"/> entry to
    /// the destination room (and optionally a specific Photon instance),
    /// then push <c>SubscriptionUpdateGameSession</c> + <c>SubscriptionUpdateRoom</c>
    /// so the watch's cached RoomInstance + RoomDetails match the new
    /// server-side state. The watch's
    /// <c>OnPresenceHeartbeatResponse</c> detects the change on its
    /// next heartbeat and refreshes its cache; pair with
    /// <c>POST players/{id}/kick</c> to also drop the player out of
    /// their current Photon match so they re-matchmake into the new
    /// target on reconnect.
    ///
    /// When <c>RoomInstanceId</c> + <c>PhotonRoomId</c> are supplied,
    /// the player lands in that EXACT instance (public or private). Use
    /// the Instances browser to enumerate available Photon rooms and
    /// pick one. With both fields omitted, a fresh deterministic
    /// instance id is used so the player matchmakes normally.</summary>
    [HttpPost("players/{id:long}/forcejoin")]
    public async Task<ActionResult> ForceJoin(long id, [FromBody] ForceJoinRequest body)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id);
        if (player is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == body.RoomId);
        if (room is null) return NotFound(new { error = "room_not_found" });

        // Build the destination RoomInstance. When the admin picked a
        // specific instance from the Instances browser, RoomInstanceId
        // + PhotonRoomId arrive together — use them verbatim so the
        // player lands in THAT match, not a fresh one. Otherwise mint
        // deterministic defaults that match what /goto/room would
        // produce for a first join (roomId*100k + subId base; bare
        // ^roomname_id photon room name).
        var subRoomId = body.SubRoomId ?? 0;
        var photonRoomId = !string.IsNullOrEmpty(body.PhotonRoomId)
            ? body.PhotonRoomId
            : $"^{room.Name.ToLowerInvariant()}_{room.Id}";
        var roomInstanceId = body.RoomInstanceId
            ?? (room.Id * 100_000L + subRoomId);
        var photonRegionId = !string.IsNullOrEmpty(body.PhotonRegionId)
            ? body.PhotonRegionId
            : "us";

        var roomInstance = new DorkNet.Server.Controllers.Match.RoomInstanceDto
        {
            RoomInstanceId = roomInstanceId,
            RoomId = room.Id,
            SubRoomId = subRoomId,
            Location = room.LocationReplicationId,
            PhotonRegionId = photonRegionId,
            PhotonRoomId = photonRoomId,
            Name = room.Name,
            MaxCapacity = 8,
            IsFull = false,
            IsPrivate = false,
            IsInProgress = true,
        };

        playerPresence.SetRoom(id, roomInstance);

        // Two pushes back-to-back: GameSession update changes the
        // watch's cached RoomInstance (OnSubscriptionUpdateRoomInstance
        // → UpdateCachedRoomInstance), and SubscriptionUpdateRoom
        // refreshes the watch's RoomDetails cache so the room-info
        // panel doesn't show stale data after the warp.
        await notifications.NotifyAsync(id,
            PushNotificationId.SubscriptionUpdateGameSession, roomInstance);
        await notifications.NotifyAsync(id,
            PushNotificationId.SubscriptionUpdateRoom,
            new
            {
                Room = new { Id = room.Id, Name = room.Name },
                RoomId = room.Id,
                SubRoomId = subRoomId,
                Reason = "AdminForceJoin",
            });

        await LogAsync("force_join", "player", id,
            $"room={body.RoomId} sub={subRoomId} instance={roomInstanceId} photon={photonRoomId}");
        await db.SaveChangesAsync();
        return Ok(new
        {
            id,
            roomId = body.RoomId,
            subRoomId,
            roomInstanceId,
            photonRoomId,
        });
    }

    // ── System ───────────────────────────────────────────────────────────

    public sealed record SetMotdRequest(string Message);

    /// <summary>Push a server-maintenance broadcast to every connected
    /// player. The MOTD config value is changed via appsettings.json
    /// (server restart required); this is for ad-hoc announcements.</summary>
    [HttpPost("broadcast")]
    public async Task<ActionResult> Broadcast([FromBody] SetMotdRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Message)) return BadRequest("empty message");
        await notifications.BroadcastAsync(PushNotificationId.ServerMaintenance,
            new { Message = body.Message });
        await LogAsync("broadcast", "system", 0, body.Message);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ── Store catalog management ─────────────────────────────────────────

    /// <summary>Canonical storefront keys the admin UI can assign to
    /// store items. Exact <c>giftdrop:N</c> keys target a single
    /// in-room shelf; <c>rro</c> and <c>all</c> are shared groups.</summary>
    [HttpGet("storefronts")]
    public ActionResult ListStorefronts() =>
        Ok(StoreService.GetStorefrontDefinitions().Select(s => new
        {
            s.Key,
            s.StorefrontType,
            s.DisplayName,
            s.Scope,
        }));

    /// <summary>List every item in the catalog (including inactive
    /// rows) so the admin UI can render the full catalog and let an
    /// admin flip flags / edit prices.</summary>
    [HttpGet("storeitems")]
    public async Task<ActionResult> ListStoreItems(
        [FromQuery] string? storefront, [FromQuery] string? category,
        [FromQuery] int take = 200, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        IQueryable<StoreItemEntity> q = db.StoreItems;
        if (!string.IsNullOrWhiteSpace(storefront)) q = q.Where(i => i.Storefront == storefront);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(i => i.Category == category);
        var rows = await q.OrderBy(i => i.Storefront).ThenBy(i => i.Category).ThenBy(i => i.Price)
            .Skip(skip).Take(take).ToListAsync();
        return Ok(rows);
    }

    public sealed record StoreItemUpsertRequest(
        string? Slug, string? DisplayName, string? Description,
        string? Category, string? ImageName, int? CurrencyType,
        long? Price, bool? IsActive, bool? IsLimitedTime,
        DateTime? AvailableUntil, string? Storefront);

    /// <summary>Create a new catalog item.</summary>
    [HttpPost("storeitems")]
    public async Task<ActionResult> CreateStoreItem([FromBody] StoreItemUpsertRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Slug)) return BadRequest("missing_slug");
        if (string.IsNullOrWhiteSpace(body.DisplayName)) return BadRequest("missing_display_name");
        var existing = await db.StoreItems.AnyAsync(i => i.Slug == body.Slug);
        if (existing) return Conflict(new { error = "slug_taken" });
        var item = new StoreItemEntity
        {
            Slug = body.Slug.Trim(),
            DisplayName = body.DisplayName.Trim(),
            Description = (body.Description ?? string.Empty).Trim(),
            Category = (body.Category ?? "accessory").Trim().ToLowerInvariant(),
            ImageName = body.ImageName ?? string.Empty,
            CurrencyType = body.CurrencyType ?? 2,
            Price = Math.Max(0, body.Price ?? 0),
            IsActive = body.IsActive ?? true,
            IsLimitedTime = body.IsLimitedTime ?? false,
            AvailableUntil = body.AvailableUntil,
            Storefront = (body.Storefront ?? "main").Trim(),
        };
        db.StoreItems.Add(item);
        await LogAsync("create_store_item", "storeitem", 0, item.Slug);
        await db.SaveChangesAsync();
        return Ok(item);
    }

    /// <summary>Patch an existing catalog item — any null field is
    /// left untouched so the admin UI can do partial edits.</summary>
    [HttpPost("storeitems/{id:long}")]
    [HttpPut("storeitems/{id:long}")]
    public async Task<ActionResult> UpdateStoreItem(long id, [FromBody] StoreItemUpsertRequest body)
    {
        var item = await db.StoreItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound();
        if (body.DisplayName is not null) item.DisplayName = body.DisplayName.Trim();
        if (body.Description is not null) item.Description = body.Description.Trim();
        if (body.Category is not null) item.Category = body.Category.Trim().ToLowerInvariant();
        if (body.ImageName is not null) item.ImageName = body.ImageName;
        if (body.CurrencyType is int ct) item.CurrencyType = ct;
        if (body.Price is long p) item.Price = Math.Max(0, p);
        if (body.IsActive is bool a) item.IsActive = a;
        if (body.IsLimitedTime is bool lt) item.IsLimitedTime = lt;
        if (body.AvailableUntil.HasValue) item.AvailableUntil = body.AvailableUntil;
        if (body.Storefront is not null) item.Storefront = body.Storefront.Trim();
        item.UpdatedAt = DateTime.UtcNow;
        await LogAsync("update_store_item", "storeitem", id, item.Slug);
        await db.SaveChangesAsync();
        return Ok(item);
    }

    /// <summary>Delete a catalog item. Soft-flag is recommended via
    /// IsActive=false; hard delete is here for cleanup of test
    /// items.</summary>
    [HttpDelete("storeitems/{id:long}")]
    public async Task<ActionResult> DeleteStoreItem(long id)
    {
        var item = await db.StoreItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound();
        db.StoreItems.Remove(item);
        await LogAsync("delete_store_item", "storeitem", id, item.Slug);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Task LogAsync(string action, string targetType, long targetId, string reason)
    {
        db.AdminActions.Add(new AdminActionEntity
        {
            AdminPlayerId = CurrentAdminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Reason = reason,
        });
        return Task.CompletedTask;
    }

    // ── Community Board ──────────────────────────────────────────────
    // Backs the dorm community board panel. The watch hits
    // /api/communityboard/v1/current; admins edit here.

    /// <summary>GET the current board state. Same shape the watch
    /// receives — handy for the admin UI to populate its form.</summary>
    [HttpGet("communityboard")]
    public async Task<ActionResult<CommunityBoardState>> GetCommunityBoard()
        => Ok(await communityBoard.GetAsync());

    /// <summary>POST a full replacement state. Any null nested objects
    /// (FeaturedPlayer / FeaturedRoomGroup / CurrentAnnouncement)
    /// clear that section. Lists default to empty if omitted.</summary>
    [HttpPost("communityboard")]
    public async Task<ActionResult<CommunityBoardState>> SetCommunityBoard(
        [FromBody] CommunityBoardState body)
    {
        body.InstagramImages ??= new();
        body.Videos ??= new();
        var saved = await communityBoard.UpdateAsync(body);
        await LogAsync("set_community_board", "global", 0,
            $"announcement=\"{saved.CurrentAnnouncement?.Message ?? ""}\" " +
            $"featuredPlayer={saved.FeaturedPlayer?.Id} " +
            $"videos={saved.Videos.Count} instagram={saved.InstagramImages.Count}");
        return Ok(saved);
    }

    /// <summary>POST <c>api/admin/v1/communityboard/instagram/upload</c>
    /// — accept a single image file, persist it as a global
    /// <see cref="RoomDataBlobEntity"/> at <c>RoomId=0</c> under a
    /// generated hash filename, return the filename + a ready-to-paste
    /// img.* URL. The Community Board SPA section pipes a file picker
    /// into this so admins don't have to know what an
    /// <c>ImageName</c> is — they just drop a JPG/PNG and we wire it up.
    ///
    /// Cap: 25 MB. Anything larger is almost certainly a mistake for an
    /// Instagram-strip tile and would also push us close to the CF edge
    /// limit on a non-chunked upload.</summary>
    [HttpPost("communityboard/instagram/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<ActionResult> UploadInstagramImage([FromForm(Name = "file")] IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "missing_file" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return BadRequest(new { error = "unsupported_extension", allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" } });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        // Hashed filename so re-uploads of the same content don't
        // proliferate and so the URL is content-addressed. Prefix
        // `cb_insta_` to keep these distinguishable from room-image
        // blobs at a glance when grepping the table.
        using var sha = System.Security.Cryptography.SHA1.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        var imageName = $"cb_insta_{hash[..24]}{ext}";

        var existing = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.BlobName == imageName);
        if (existing is null)
        {
            var (bucket, key) = BlobRouter.Route(imageName);
            await storage.PutAsync(bucket, key, bytes,
                ext == ".png" ? "image/png" : "image/jpeg");
            db.RoomDataBlobs.Add(new RoomDataBlobEntity
            {
                RoomId = 0,
                BlobName = imageName,
                UploadedByPlayerId = CurrentAdminId,
                UploadedAt = DateTime.UtcNow,
            });
            await LogAsync("cb_insta_upload", "community_board", 0, $"image={imageName} bytes={bytes.Length}");
            await db.SaveChangesAsync();
        }

        // Use the configured apex (DomainConfig) so the returned URL
        // works whether the admin is on localhost, rec.net, or a future
        // deployment apex — driven solely by the DORKNET_DOMAIN env var.
        return Ok(new
        {
            imageName,
            imageUrl = $"https://{domain.Sub("img")}/{imageName}",
            bytes = bytes.Length,
            reused = existing is not null,
        });
    }

    /// <summary>POST <c>api/admin/v1/communityboard/video/upload</c> —
    /// accept a single video file and persist it as a global
    /// <see cref="RoomDataBlobEntity"/> at <c>RoomId=0</c> under a hashed
    /// <c>cb_video_*</c> name. The board's Videos section binds the
    /// returned <c>blobName</c> straight into <c>VideoState.BlobName</c>
    /// so the watch's <c>CommunityBoardVideoData</c> resolves it to
    /// <c>cdn.{apex}/&lt;blobName&gt;</c>.
    ///
    /// Cap: 100 MB — picks the same ceiling Cloudflare enforces at the
    /// edge so a non-chunked POST never sees a 413. Anything bigger
    /// should be hosted externally and pasted into <c>SourceUrl</c>.</summary>
    // [RequestSizeLimit] alone is enough — pairing it with
    // [DisableRequestSizeLimit] was undefined-behaviour territory
    // (Microsoft docs explicitly say "don't combine them"). Setting
    // 100 MB at the action keeps Kestrel from clipping the body at its
    // 30 MB default while still capping uploads under Cloudflare's
    // free-tier edge limit.
    [HttpPost("communityboard/video/upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000, ValueLengthLimit = 100_000_000)]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult> UploadCommunityBoardVideo([FromForm(Name = "file")] IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "missing_file" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".mp4" or ".webm" or ".mov" or ".m4v"))
            return BadRequest(new {
                error = "unsupported_extension",
                allowed = new[] { ".mp4", ".webm", ".mov", ".m4v" },
            });

        var declaredSize = file.Length;
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        // Catch the "Kestrel/Cloudflare clipped the body" case BEFORE
        // we hash + write — IFormFile.Length reports what the multipart
        // parser saw in the part header, so a mismatch with how many
        // bytes the stream actually produced is a transport-level
        // truncation and the resulting blob would be the corrupted file
        // the user saw on download.
        if (bytes.LongLength != declaredSize)
        {
            adminLogger.LogError(
                "[cb-video-upload] size mismatch — multipart says {Declared:N0} bytes, stream produced {Read:N0} bytes (file={Name})",
                declaredSize, bytes.LongLength, file.FileName);
            return BadRequest(new {
                error = "upload_truncated",
                declaredBytes = declaredSize,
                receivedBytes = bytes.LongLength,
            });
        }

        // Content-addressed name keeps re-uploads from proliferating —
        // identical bytes resolve to the same row + S3 object regardless
        // of original filename. `cb_video_` prefix keeps it greppable.
        using var sha = System.Security.Cryptography.SHA1.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        var blobName = $"cb_video_{hash[..24]}{ext}";
        var (bucket, key) = BlobRouter.Route(blobName);

        // We were previously skipping S3 PUT when the DB row already
        // existed. That created a zombie state: if the first upload's
        // PUT silently failed (Garage outage, bad creds) the row was
        // committed but the bytes weren't, and every subsequent
        // upload of the same content returned `reused: true` without
        // ever fixing the missing object. Verify S3 has the object;
        // re-PUT if it doesn't.
        var existing = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.BlobName == blobName);
        var alreadyOnS3 = existing is not null && await storage.ExistsAsync(bucket, key);

        if (!alreadyOnS3)
        {
            await storage.PutAsync(bucket, key, bytes, MimeFromExt(ext));
            // Post-PUT HEAD: Garage occasionally accepts a PutObject
            // and returns 200 without persisting (auth misconfig,
            // bucket policy reject). HeadObject is the cheapest way to
            // confirm the bytes are actually queryable before we
            // pretend the upload succeeded.
            if (!await storage.ExistsAsync(bucket, key))
            {
                adminLogger.LogError(
                    "[cb-video-upload] PUT returned OK but HEAD says {Bucket}/{Key} missing — Garage may be silently dropping",
                    bucket, key);
                return StatusCode(500, new { error = "s3_put_lost", bucket, key });
            }
            adminLogger.LogInformation(
                "[cb-video-upload] stored {Bucket}/{Key} ({Bytes:N0} bytes, ext={Ext})",
                bucket, key, bytes.LongLength, ext);
        }
        if (existing is null)
        {
            db.RoomDataBlobs.Add(new RoomDataBlobEntity
            {
                RoomId = 0,
                BlobName = blobName,
                UploadedByPlayerId = CurrentAdminId,
                UploadedAt = DateTime.UtcNow,
            });
            await LogAsync("cb_video_upload", "community_board", 0, $"video={blobName} bytes={bytes.Length}");
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            blobName,
            videoUrl = $"https://{domain.Sub("cdn")}/video/{blobName}",
            bytes = bytes.Length,
            reused = existing is not null && alreadyOnS3,
            repaired = existing is not null && !alreadyOnS3,
        });
    }

    /// <summary>POST <c>api/admin/v1/communityboard/videothumb/upload</c>
    /// — accept a single image and persist it under a hashed
    /// <c>cb_thumb_*</c> name for use as <see cref="VideoState"/>'s
    /// <c>ThumbnailBlobName</c>. Mechanically identical to the Instagram
    /// upload endpoint, just with a different prefix so thumbnail vs.
    /// gallery images are distinguishable at a glance in the blob table.
    ///
    /// Cap: 25 MB. The watch renders thumbnails at small sizes; bigger
    /// is wasted bandwidth.</summary>
    [HttpPost("communityboard/videothumb/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<ActionResult> UploadCommunityBoardVideoThumbnail(
        [FromForm(Name = "file")] IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "missing_file" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return BadRequest(new {
                error = "unsupported_extension",
                allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" },
            });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        using var sha = System.Security.Cryptography.SHA1.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        var blobName = $"cb_thumb_{hash[..24]}{ext}";

        var existing = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.BlobName == blobName);
        if (existing is null)
        {
            var (bucket, key) = BlobRouter.Route(blobName);
            await storage.PutAsync(bucket, key, bytes,
                ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg");
            db.RoomDataBlobs.Add(new RoomDataBlobEntity
            {
                RoomId = 0,
                BlobName = blobName,
                UploadedByPlayerId = CurrentAdminId,
                UploadedAt = DateTime.UtcNow,
            });
            await LogAsync("cb_videothumb_upload", "community_board", 0, $"thumb={blobName} bytes={bytes.Length}");
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            blobName,
            imageUrl = $"https://{domain.Sub("img")}/{blobName}",
            bytes = bytes.Length,
            reused = existing is not null,
        });
    }

    // MIME mapping for the video upload — the CDN serves whatever
    // Content-Type we wrote to S3, so getting this right makes the
    // watch's HTTPVideoStreamPlayer accept the response without
    // sniffing. Default to mp4 since that's what most modern exporters
    // produce; unknown extensions fall back to octet-stream which the
    // watch usually still handles but is worth flagging.
    private static string MimeFromExt(string ext) => ext switch
    {
        ".mp4"  => "video/mp4",
        ".m4v"  => "video/mp4",
        ".webm" => "video/webm",
        ".mov"  => "video/quicktime",
        _        => "application/octet-stream",
    };

    // ── Loading screen tips ──────────────────────────────────────────────

    public sealed class LoadingScreenTipDto
    {
        public long? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public int Context { get; set; }
        public int PlatformMask { get; set; } = -1;
        public string RoomNamesCsv { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>GET <c>api/admin/v1/loadingscreentips</c> — list every
    /// tip (including inactive ones) for the admin SPA.</summary>
    [HttpGet("loadingscreentips")]
    public async Task<IActionResult> ListLoadingScreenTips()
    {
        var rows = await db.LoadingScreenTips
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .AsNoTracking()
            .ToListAsync();
        return Ok(rows.Select(ToLoadingScreenTipDto));
    }

    /// <summary>POST <c>api/admin/v1/loadingscreentips</c> — create
    /// a new tip. Returns the persisted row including its assigned
    /// Id so the SPA can update its local state.</summary>
    [HttpPost("loadingscreentips")]
    public async Task<IActionResult> CreateLoadingScreenTip([FromBody] LoadingScreenTipDto body)
    {
        var entity = new Data.Entities.LoadingScreenTipEntity
        {
            Title = (body.Title ?? string.Empty).Trim(),
            Message = (body.Message ?? string.Empty).Trim(),
            ImageName = (body.ImageName ?? string.Empty).Trim(),
            Context = body.Context,
            PlatformMask = body.PlatformMask,
            RoomNamesCsv = NormaliseCsv(body.RoomNamesCsv),
            SortOrder = body.SortOrder,
            IsActive = body.IsActive,
        };
        db.LoadingScreenTips.Add(entity);
        await db.SaveChangesAsync();
        await LogAsync("create_loading_tip", "global", entity.Id, entity.Title);
        return Ok(ToLoadingScreenTipDto(entity));
    }

    /// <summary>PUT <c>api/admin/v1/loadingscreentips/{id}</c> — update
    /// an existing tip.</summary>
    [HttpPut("loadingscreentips/{id:long}")]
    public async Task<IActionResult> UpdateLoadingScreenTip(long id, [FromBody] LoadingScreenTipDto body)
    {
        var row = await db.LoadingScreenTips.FirstOrDefaultAsync(t => t.Id == id);
        if (row is null) return NotFound();
        row.Title = (body.Title ?? string.Empty).Trim();
        row.Message = (body.Message ?? string.Empty).Trim();
        row.ImageName = (body.ImageName ?? string.Empty).Trim();
        row.Context = body.Context;
        row.PlatformMask = body.PlatformMask;
        row.RoomNamesCsv = NormaliseCsv(body.RoomNamesCsv);
        row.SortOrder = body.SortOrder;
        row.IsActive = body.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await LogAsync("update_loading_tip", "global", id, row.Title);
        return Ok(ToLoadingScreenTipDto(row));
    }

    /// <summary>DELETE <c>api/admin/v1/loadingscreentips/{id}</c> — hard
    /// delete a tip. (Toggling <c>IsActive=false</c> via the update
    /// endpoint is the soft-delete path.)</summary>
    [HttpDelete("loadingscreentips/{id:long}")]
    public async Task<IActionResult> DeleteLoadingScreenTip(long id)
    {
        var row = await db.LoadingScreenTips.FirstOrDefaultAsync(t => t.Id == id);
        if (row is null) return NotFound();
        db.LoadingScreenTips.Remove(row);
        await db.SaveChangesAsync();
        await LogAsync("delete_loading_tip", "global", id, row.Title);
        return Ok(new { deleted = id });
    }

    /// <summary>POST <c>api/admin/v1/loadingscreentips/upload</c> —
    /// accept an image file (jpg/png/webp), persist it via the same
    /// content-addressed flow as the Community Board uploader, and
    /// return the BlobName + img.* URL. The SPA pipes this into the
    /// tip's <c>ImageName</c> field.</summary>
    [HttpPost("loadingscreentips/upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<IActionResult> UploadLoadingScreenTipImage([FromForm(Name = "file")] IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "missing_file" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            return BadRequest(new { error = "unsupported_extension", allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" } });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        using var sha = System.Security.Cryptography.SHA1.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        var imageName = $"loadingtip_{hash[..24]}{ext}";

        var existing = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.BlobName == imageName);
        if (existing is null)
        {
            var (bucket, key) = BlobRouter.Route(imageName);
            await storage.PutAsync(bucket, key, bytes,
                ext == ".png" ? "image/png" : "image/jpeg");
            db.RoomDataBlobs.Add(new RoomDataBlobEntity
            {
                RoomId = 0,
                BlobName = imageName,
                UploadedByPlayerId = CurrentAdminId,
                UploadedAt = DateTime.UtcNow,
            });
            await LogAsync("loading_tip_upload", "global", 0, $"image={imageName} bytes={bytes.Length}");
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            imageName,
            imageUrl = $"https://{domain.Sub("img")}/{imageName}",
            bytes = bytes.Length,
            reused = existing is not null,
        });
    }

    private static LoadingScreenTipDto ToLoadingScreenTipDto(Data.Entities.LoadingScreenTipEntity t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Message = t.Message,
        ImageName = t.ImageName,
        Context = t.Context,
        PlatformMask = t.PlatformMask,
        RoomNamesCsv = t.RoomNamesCsv,
        SortOrder = t.SortOrder,
        IsActive = t.IsActive,
    };

    private static string NormaliseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return string.Empty;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(",", parts);
    }

    // ── Room import ──────────────────────────────────────────────────────

    /// <summary>POST <c>api/admin/v1/rooms/import</c> — multipart upload
    /// of an archived multi-scene room. Each .room file (one per scene)
    /// is uploaded with its filename including the scene folder, e.g.
    /// <c>Lobby/abc.room</c> or <c>Ch1_Headfirst/def.room</c>. The form
    /// can either send the per-file folder via <c>webkitRelativePath</c>
    /// (browser folder picker preserves it) or via the filename itself.
    ///
    /// Server inserts:
    ///   1. A <see cref="RoomEntity"/> row for the new room (id 1000+).
    ///   2. One <see cref="RoomDataBlobEntity"/> per uploaded file,
    ///      named <c>{roomname}_{scenefolder}.room</c> (lowercased).
    ///   3. One <see cref="RoomSceneEntity"/> per scene with the entry
    ///      scene at OrderIndex=0 and the rest in folder order.
    ///
    /// Default <c>RoomSceneLocationId</c> for every scene is the
    /// MakerRoom (Basement) GUID — the canonical 2020 custom-room
    /// canvas. If the import was a 2020-era export of an apartment
    /// scene, edit <see cref="RoomSceneEntity.RoomSceneLocationId"/>
    /// per-row after import.
    ///
    /// Returns 400 if a room with this name already exists or no
    /// files were uploaded.</summary>
    [HttpPost("rooms/import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
    public async Task<IActionResult> ImportRoom(
        [FromForm(Name = "name")] string? name,
        [FromForm(Name = "description")] string? description,
        [FromForm(Name = "entryScene")] string? entryScene,
        [FromForm(Name = "creatorPlayerId")] long? creatorPlayerId,
        [FromForm(Name = "scenePaths")] List<string>? scenePaths,
        [FromForm(Name = "files")] List<IFormFile>? files,
        [FromForm(Name = "normalizeBlobs")] bool? normalizeBlobs)
    {
        // Per-import "run blob bytes through the 2020 protobuf normaliser"
        // toggle, mirroring RoomZipImportController. Default OFF — see
        // that file's notes for why the round-trip currently crashes the
        // watch on load. Diagnostic Normalize() call still runs so its
        // parse-OK / parse-FAIL log line remains in the import telemetry.
        var normalize = normalizeBlobs ?? false;
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "name_required" });
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "no_files" });

        var roomName = name.Trim();
        if (await db.Rooms.AnyAsync(r => r.Name == roomName))
            return BadRequest(new { error = "duplicate_name", message = $"A room named '{roomName}' already exists." });

        // Pair each uploaded file with its scene folder name. Browsers
        // submit folder uploads as files with FileName = "Lobby/blob.room"
        // when webkitdirectory is used; fall back to a parallel
        // scenePaths[] form field when the client supplies it explicitly
        // (e.g. uploading individual files via JS FormData with the
        // scene name set per entry).
        //
        // Each blob is also passed through RoomBlobNormalizerService
        // (round-trip via the full 2020 PersistedRoomData schema). That
        // strips redundant default-value encodings the modern client
        // emits — without this, the watch's stricter 2020 parser logs
        // "Error attempting to parse room data stream" and boots the
        // player to dorm. Normalize() falls back to the original bytes
        // if parse fails, so a corrupted upload doesn't drop content.
        var perScene = new List<(string SceneName, string OriginalName, byte[] Bytes, RoomBlobNormalizerService.Result Norm)>();
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            // FormFile.FileName is "scenefolder/blob.room" when uploaded
            // via webkitdirectory. ScenePaths (if provided) overrides.
            var sceneName = scenePaths is { } sp && i < sp.Count && !string.IsNullOrWhiteSpace(sp[i])
                ? sp[i].Trim()
                : ExtractSceneFolder(f.FileName);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return BadRequest(new {
                    error = "missing_scene_folder",
                    message = $"File '{f.FileName}' has no scene folder. Either upload via folder picker, or supply scenePaths[] alongside files[]."
                });
            }
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            var raw = ms.ToArray();
            var norm = roomBlobNormalizer.Normalize(raw);
            var persistedBytes = normalize ? norm.Bytes : raw;
            adminLogger.LogInformation(
                "[import] scene={Scene} file={File} raw={Raw:N0} → normalized={Out:N0} ({Status}) persistedFrom={Source}",
                sceneName, f.FileName, raw.Length, norm.Bytes.Length,
                norm.Normalized ? "ok" : $"fail: {norm.Error}",
                normalize ? "normaliser" : "raw");
            perScene.Add((sceneName, f.FileName, persistedBytes, norm));
        }

        // Pick entry scene: explicit override → "Lobby" if present →
        // alphabetically-first folder.
        var sceneNames = perScene.Select(s => s.SceneName).ToList();
        var entry = !string.IsNullOrWhiteSpace(entryScene)
            ? perScene.FirstOrDefault(s => string.Equals(s.SceneName, entryScene, StringComparison.OrdinalIgnoreCase))
            : default;
        if (entry == default)
        {
            entry = perScene.FirstOrDefault(s => string.Equals(s.SceneName, "Lobby", StringComparison.OrdinalIgnoreCase));
        }
        if (entry == default) entry = perScene.OrderBy(s => s.SceneName).First();

        // Allocate a new id in the user-room range (>=1000).
        var newRoomId = (await db.Rooms
            .Where(r => r.Id >= 1000)
            .Select(r => (long?)r.Id)
            .MaxAsync() ?? 999) + 1;
        var creator = creatorPlayerId ?? CurrentAdminId;
        const string makerRoomLocation = "a75f7547-79eb-47c6-8986-6767abcb4f92";

        // Build BlobName per scene.
        var slug = roomName.ToLowerInvariant().Replace(' ', '_');
        string MakeBlobName(string sceneName) => $"{slug}_{sceneName.ToLowerInvariant()}.room";

        // Reorder so entry scene is OrderIndex=0.
        var ordered = new List<(string SceneName, string OriginalName, byte[] Bytes, RoomBlobNormalizerService.Result Norm)> { entry };
        ordered.AddRange(perScene.Where(s => s.SceneName != entry.SceneName));

        using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Insert blobs — bytes go to S3, DB only stores metadata.
            foreach (var s in ordered)
            {
                var blobName = MakeBlobName(s.SceneName);
                var (bucket, key) = BlobRouter.Route(blobName);
                await storage.PutAsync(bucket, key, s.Bytes, "application/octet-stream");
                db.RoomDataBlobs.Add(new RoomDataBlobEntity
                {
                    RoomId = newRoomId,
                    BlobName = blobName,
                    UploadedByPlayerId = creator,
                    UploadedAt = DateTime.UtcNow,
                });
            }

            // Insert Room row pointing at entry scene's blob.
            var entryBlob = MakeBlobName(entry.SceneName);
            db.Rooms.Add(new RoomEntity
            {
                Id = newRoomId,
                Name = roomName,
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"Imported via admin upload ({ordered.Count} scenes)"
                    : description.Trim(),
                CreatorPlayerId = creator,
                ImageName = string.Empty,
                State = 0,
                Accessibility = 1,
                IsAGRoom = false,
                IsDormRoom = false,
                CloningAllowed = true,
                LocationReplicationId = makerRoomLocation,
                TagsCsv = "community",
                CheerCount = 0,
                FavoriteCount = 0,
                VisitCount = 0,
                VisitorCount = 0,
                HotScore = 5.0,
                CurrentDataBlobName = entryBlob,
            });

            // Insert RoomScenes rows.
            int idx = 0;
            foreach (var s in ordered)
            {
                db.RoomScenes.Add(new RoomSceneEntity
                {
                    RoomId = newRoomId,
                    OrderIndex = idx++,
                    Name = s.SceneName,
                    RoomSceneLocationId = makerRoomLocation,
                    DataBlobName = MakeBlobName(s.SceneName),
                    MaxPlayers = 8,
                    IsSandbox = false,
                    CanMatchmakeInto = true,
                    DataModifiedAt = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await LogAsync("import_room", "room", newRoomId,
            $"name={roomName} scenes={ordered.Count} entry={entry.SceneName}");

        // Fire-and-forget: scan the just-imported scene blobs for
        // Holotar/AudioSampler .htr references and download each from
        // cdn.rec.net into RoomDataBlobs (BlobName="{hash}.htr",
        // RoomId=0). The watch's holotar projectors and audio
        // samplers can then pull their content from cdn.localhost
        // instead of failing on the public CDN. Doing this in the
        // background keeps the upload response snappy (the .htr
        // download set for a YarrHarrHeist-sized room is ~25 MB).
        htrMirror.EnqueueAsync(
            ordered.Select(s => s.Bytes).ToList(),
            contextLabel: $"room={roomName}");

        return Ok(new
        {
            roomId = newRoomId,
            roomName,
            sceneCount = ordered.Count,
            entryScene = entry.SceneName,
            normalizedSceneCount = ordered.Count(s => s.Norm.Normalized),
            normalizationFailures = ordered.Where(s => !s.Norm.Normalized)
                .Select(s => new { scene = s.SceneName, error = s.Norm.Error }),
            htrMirrorStarted = true,
            scenes = ordered.Select((s, i) => new
            {
                orderIndex = i,
                name = s.SceneName,
                blobName = MakeBlobName(s.SceneName),
                rawBytes = s.Norm.InputBytes,
                bytes = s.Bytes.Length,
                normalized = s.Norm.Normalized,
            }),
        });
    }

    /// <summary>POST <c>api/admin/v1/rooms/{id}/mirror-htr</c> —
    /// re-run the HoloTar / AudioSampler .htr mirror for an existing
    /// room's blobs. Useful to (a) backfill assets for rooms imported
    /// before the auto-mirror existed, (b) debug whether
    /// HtrAssetMirrorService can actually extract refs from the C#
    /// generated 2020 PersistedRoomData schema. Synchronous (awaits the
    /// download stream) so the response carries the result counts —
    /// the import-time path uses fire-and-forget instead.</summary>
    [HttpPost("rooms/{roomId:long}/mirror-htr")]
    public async Task<IActionResult> MirrorHtrForRoom(long roomId)
    {
        // Stream bytes back from S3 — DB carries only metadata now.
        var blobNames = await db.RoomDataBlobs
            .Where(b => b.RoomId == roomId)
            .Select(b => b.BlobName)
            .ToListAsync();
        if (blobNames.Count == 0) return NotFound(new { error = "no_blobs", roomId });
        var blobs = new List<byte[]>(blobNames.Count);
        foreach (var blobName in blobNames)
        {
            var (bucket, key) = BlobRouter.Route(blobName);
            var bytes = await storage.GetAsync(bucket, key);
            if (bytes is { Length: > 0 }) blobs.Add(bytes);
        }
        if (blobs.Count == 0) return NotFound(new { error = "blobs_missing_in_s3", roomId, expected = blobNames.Count });
        var result = await htrMirror.MirrorAsync(blobs, contextLabel: $"manual room={roomId}");
        await LogAsync("mirror_htr", "room", roomId,
            $"refs={result.TotalRefs} skipped={result.AlreadyMirrored} inserted={result.Inserted} parseFails={result.RoomBlobParseFailures}");
        return Ok(new
        {
            roomId,
            scannedBlobs = blobs.Count,
            uniqueRefs = result.TotalRefs,
            alreadyMirrored = result.AlreadyMirrored,
            downloaded = result.Inserted,
            roomParseFailures = result.RoomBlobParseFailures,
            assetDownloadFailures = result.AssetDownloadFailures,
        });
    }

    /// <summary>Pull the immediate-parent folder out of a multi-segment
    /// filename like "Lobby/abc.room" or "YarrHarrHeist/Lobby/abc.room".
    /// Returns the second-to-last segment (the file's parent folder).
    /// Returns null if the filename has no folder component.</summary>
    private static string? ExtractSceneFolder(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        // Browsers may use either separator depending on platform.
        var parts = fileName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        // The .room file's parent folder is the scene name.
        return parts[^2];
    }
}
