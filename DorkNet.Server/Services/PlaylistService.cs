using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Backs the watch's /roomserver/roomsandplaylists/* + /api/curatedroomplaylists
/// surfaces. The 2020.12 client deserializes responses as
/// <c>List&lt;MKAMHOIHOJK&gt;</c>, an abstract union of Room
/// (<c>KLCOGEIGEBJ</c>, discriminator = presence of lowercase
/// <c>roomId</c>) and Playlist (<c>KMKPEOGJDFK</c>). This service owns the
/// playlist half of that union — Rooms come from <see cref="RoomService"/>.
/// </summary>
public class PlaylistService(DorkNetDbContext db)
{
    /// <summary>
    /// Hot playlists — drives <c>/roomserver/roomsandplaylists/hot</c>.
    /// Score is <c>CheerCount + VisitorCount * 0.25</c> (cheers weighted
    /// heavier than passive visits) so a freshly-popular list with a
    /// burst of cheers can outrank an evergreen list with high traffic.
    /// Tag filter mirrors <see cref="RoomService.HotAsync"/> — substring
    /// match against <see cref="PlaylistEntity.TagsCsv"/> after stripping
    /// a leading '#'.
    /// </summary>
    public async Task<List<PlaylistEntity>> HotAsync(string? tag, int take = 24)
    {
        IQueryable<PlaylistEntity> q = db.Playlists;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var bareTag = tag.TrimStart('#').Trim();
            if (bareTag.Length > 0)
            {
                var needle = $"%{bareTag}%";
                q = q.Where(p => EF.Functions.Like(p.TagsCsv, needle));
            }
        }
        // Score is computed client-side after pulling rows because
        // EF's SQLite provider can't translate ordering expressions
        // with a constant double multiplier predictably; the table
        // is tiny so the in-memory sort is fine.
        var rows = await q.ToListAsync();
        return rows
            .OrderByDescending(p => p.CheerCount + p.VisitorCount * 0.25)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Curated playlists for <c>/api/curatedroomplaylists</c>. Returns
    /// only seed-curated rows in <see cref="PlaylistEntity.OrderIndex"/>
    /// order so the editorial sort holds.
    /// </summary>
    public Task<List<PlaylistEntity>> CuratedAsync() =>
        db.Playlists
            .Where(p => p.IsCurated)
            .OrderBy(p => p.OrderIndex)
            .ToListAsync();

    /// <summary>
    /// Free-text search across playlists — drives the playlist half of
    /// <c>/roomserver/search_roomsandplaylists/{query}</c>. Matches Name
    /// or TagsCsv substrings (same predicate shape as
    /// <see cref="RoomService.SearchAsync"/>).
    /// </summary>
    public async Task<List<PlaylistEntity>> SearchAsync(string query, int take = 24)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var needle = $"%{query}%";
        return await db.Playlists
            .Where(p => EF.Functions.Like(p.Name, needle)
                     || EF.Functions.Like(p.TagsCsv, needle))
            .OrderByDescending(p => p.CheerCount)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>Member-room ids for a playlist, in display order.
    /// Callers fan-out per-id with <see cref="RoomService.GetByIdAsync"/>
    /// when they need the full Room rows (the watch's playlist detail
    /// screen does this).</summary>
    public Task<List<long>> RoomIdsAsync(long playlistId) =>
        db.PlaylistRooms
            .Where(pr => pr.PlaylistId == playlistId)
            .OrderBy(pr => pr.OrderIndex)
            .Select(pr => pr.RoomId)
            .ToListAsync();

    /// <summary>Single-playlist lookup. Returns null when missing so
    /// the controller can 404 cleanly.</summary>
    public Task<PlaylistEntity?> GetByIdAsync(long playlistId) =>
        db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);

    /// <summary>Bulk-by-id lookup for <c>playlists/bulk</c>. Preserves
    /// caller order; missing rows are silently dropped.</summary>
    public async Task<List<PlaylistEntity>> BulkAsync(IEnumerable<long> playlistIds)
    {
        var ids = playlistIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.Playlists.Where(p => ids.Contains(p.Id)).ToListAsync();
        var byId = rows.ToDictionary(p => p.Id);
        return ids.Select(i => byId.TryGetValue(i, out var p) ? p : null)
            .Where(p => p is not null).Select(p => p!).ToList();
    }

    /// <summary>Playlists created by a player. Drives
    /// <c>playlists/createdby/me</c> and any future profile-card
    /// equivalent.</summary>
    public Task<List<PlaylistEntity>> CreatedByAsync(long playerId) =>
        db.Playlists
            .Where(p => p.CreatorPlayerId == playerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    /// <summary>Playlists the player has cheered. Drives
    /// <c>playlists/cheeredby/me</c>.</summary>
    public async Task<List<PlaylistEntity>> CheeredByAsync(long playerId)
    {
        var ids = await db.PlaylistInteractions
            .Where(i => i.PlayerId == playerId && i.Cheered)
            .Select(i => i.PlaylistId).ToListAsync();
        return await db.Playlists.Where(p => ids.Contains(p.Id)).ToListAsync();
    }

    /// <summary>Playlists the player has favorited. Drives
    /// <c>playlists/favoritedby/me</c>.</summary>
    public async Task<List<PlaylistEntity>> FavoritedByAsync(long playerId)
    {
        var ids = await db.PlaylistInteractions
            .Where(i => i.PlayerId == playerId && i.Favorited)
            .Select(i => i.PlaylistId).ToListAsync();
        return await db.Playlists.Where(p => ids.Contains(p.Id)).ToListAsync();
    }

    /// <summary>Read the caller's interaction row for a playlist —
    /// drives <c>playlists/{id}/interactionby/me</c>. Returns (false,
    /// false) sentinel when no row exists yet (no toggle has fired).</summary>
    public async Task<(bool Cheered, bool Favorited)> InteractionForAsync(long playlistId, long playerId)
    {
        var row = await db.PlaylistInteractions
            .FirstOrDefaultAsync(i => i.PlaylistId == playlistId && i.PlayerId == playerId);
        return row is null ? (false, false) : (row.Cheered, row.Favorited);
    }

    /// <summary>Toggle a single interaction flag for the caller. The
    /// playlist's denormalized counter is bumped/decremented in the
    /// same SaveChanges so the hot ranking + tile badges stay in sync.
    /// Returns the new row state.</summary>
    public async Task<PlaylistInteractionEntity> SetInteractionAsync(
        long playlistId, long playerId, bool? cheered = null, bool? favorited = null)
    {
        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId)
            ?? throw new InvalidOperationException($"playlist {playlistId} not found");
        var row = await db.PlaylistInteractions
            .FirstOrDefaultAsync(i => i.PlaylistId == playlistId && i.PlayerId == playerId);
        if (row is null)
        {
            row = new PlaylistInteractionEntity
            {
                PlaylistId = playlistId,
                PlayerId = playerId,
            };
            db.PlaylistInteractions.Add(row);
        }
        if (cheered is bool c && row.Cheered != c)
        {
            row.Cheered = c;
            playlist.CheerCount = Math.Max(0, playlist.CheerCount + (c ? 1 : -1));
        }
        if (favorited is bool f && row.Favorited != f)
        {
            row.Favorited = f;
            playlist.FavoriteCount = Math.Max(0, playlist.FavoriteCount + (f ? 1 : -1));
        }
        row.UpdatedAt = DateTime.UtcNow;
        playlist.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Create a new player-owned playlist. Curated lists are
    /// admin-only; user-created lists set IsCurated=false. Returns the
    /// new row; the controller projects it into the BMFAGMFKODA wire
    /// shape on the way out.</summary>
    public async Task<PlaylistEntity> CreateAsync(
        long creatorId, string name, string? description, string? imageName, string? tagsCsv)
    {
        var p = new PlaylistEntity
        {
            Name = (name ?? string.Empty).Trim(),
            Description = description ?? string.Empty,
            ImageName = imageName ?? string.Empty,
            CreatorPlayerId = creatorId,
            IsCurated = false,
            TagsCsv = tagsCsv ?? string.Empty,
        };
        db.Playlists.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Apply an arbitrary mutation to a playlist after
    /// checking ownership. Returns the row on success, null on missing
    /// playlist; throws <see cref="UnauthorizedAccessException"/> when
    /// the caller isn't the creator. Used by every modify endpoint.</summary>
    public async Task<PlaylistEntity?> ModifyAsync(long playlistId, long callerId, Action<PlaylistEntity> mutator)
    {
        var p = await db.Playlists.FirstOrDefaultAsync(x => x.Id == playlistId);
        if (p is null) return null;
        if (p.CreatorPlayerId != callerId) throw new UnauthorizedAccessException();
        mutator(p);
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>Delete a player-owned playlist + its member-room rows
    /// + every per-player interaction row. Curated playlists are
    /// admin-only — the controller still calls this method but only
    /// after gating on the caller's ownership.</summary>
    public async Task<bool> DeleteAsync(long playlistId, long callerId)
    {
        var p = await db.Playlists.FirstOrDefaultAsync(x => x.Id == playlistId);
        if (p is null) return false;
        if (p.CreatorPlayerId != callerId) throw new UnauthorizedAccessException();
        var memberRows = await db.PlaylistRooms.Where(pr => pr.PlaylistId == playlistId).ToListAsync();
        db.PlaylistRooms.RemoveRange(memberRows);
        var interactionRows = await db.PlaylistInteractions
            .Where(i => i.PlaylistId == playlistId).ToListAsync();
        db.PlaylistInteractions.RemoveRange(interactionRows);
        db.Playlists.Remove(p);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Add a room to a playlist at the end. Idempotent — a
    /// repeat add is a no-op. Throws <see cref="UnauthorizedAccessException"/>
    /// when the caller isn't the owner.</summary>
    public async Task<bool> AddRoomAsync(long playlistId, long roomId, long callerId)
    {
        var p = await db.Playlists.FirstOrDefaultAsync(x => x.Id == playlistId);
        if (p is null) return false;
        if (p.CreatorPlayerId != callerId) throw new UnauthorizedAccessException();
        var existing = await db.PlaylistRooms
            .AnyAsync(pr => pr.PlaylistId == playlistId && pr.RoomId == roomId);
        if (existing) return true;
        var nextOrder = await db.PlaylistRooms
            .Where(pr => pr.PlaylistId == playlistId)
            .Select(pr => (int?)pr.OrderIndex).MaxAsync() ?? -1;
        db.PlaylistRooms.Add(new PlaylistRoomEntity
        {
            PlaylistId = playlistId,
            RoomId = roomId,
            OrderIndex = nextOrder + 1,
        });
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Remove a room from a playlist. Idempotent — missing
    /// row is treated as success so the watch's repeat-delete path
    /// doesn't render a phantom error.</summary>
    public async Task<bool> RemoveRoomAsync(long playlistId, long roomId, long callerId)
    {
        var p = await db.Playlists.FirstOrDefaultAsync(x => x.Id == playlistId);
        if (p is null) return false;
        if (p.CreatorPlayerId != callerId) throw new UnauthorizedAccessException();
        var row = await db.PlaylistRooms
            .FirstOrDefaultAsync(pr => pr.PlaylistId == playlistId && pr.RoomId == roomId);
        if (row is null) return true;
        db.PlaylistRooms.Remove(row);
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Idempotent seed of the default curated playlists. Inserts three
    /// editorial lists (Featured, Newcomers, Sports) on first launch and
    /// populates each with up to five matching seeded rooms (looked up by
    /// the tag substrings each list curates). Re-running on a populated
    /// table is a no-op — once any playlist exists, this method bails.
    /// </summary>
    public async Task SeedCuratedAsync()
    {
        if (await db.Playlists.AnyAsync()) return;

        var curated = new (string Name, string Description, string Image, string Tags, string[] RoomLookupTags)[]
        {
            ("Featured", "Hand-picked rooms the team is loving this week — start here.",
                "featured.png", "featured",
                new[] { "featured" }),
            ("Newcomers", "Easy-to-pick-up rooms perfect for your first Rec Room sessions.",
                "newcomers.png", "newcomer,easy",
                new[] { "recroomoriginal" }),
            ("Sports", "Get sweaty — paintball, dodgeball, soccer, and the rest of the field.",
                "sports.png", "paintball,soccer,dodgeball",
                new[] { "sport" }),
        };

        long playlistId = 1;
        int orderIndex = 0;
        foreach (var entry in curated)
        {
            var p = new PlaylistEntity
            {
                Id = playlistId++,
                Name = entry.Name,
                Description = entry.Description,
                ImageName = entry.Image,
                CreatorPlayerId = 1,
                IsCurated = true,
                OrderIndex = orderIndex++,
                TagsCsv = entry.Tags,
                CheerCount = 50 + orderIndex * 10,
                FavoriteCount = 25 + orderIndex * 5,
                VisitorCount = 1000 + orderIndex * 250,
                VisitCount = 4000 + orderIndex * 750,
            };
            db.Playlists.Add(p);
            await db.SaveChangesAsync(); // need the Id for the join rows

            // Find 3-5 matching seeded rooms for this list. Search by
            // tag substring against RoomEntity.TagsCsv so the
            // playlists naturally pick up new seeded rooms that
            // carry the right tags without per-name hardcoding.
            var memberRooms = new List<long>();
            foreach (var lookupTag in entry.RoomLookupTags)
            {
                var needle = $"%{lookupTag}%";
                var ids = await db.Rooms
                    .Where(r => !r.IsDormRoom && !r.HiddenFromBrowse && EF.Functions.Like(r.TagsCsv, needle))
                    .OrderByDescending(r => r.HotScore)
                    .Select(r => r.Id)
                    .Take(5)
                    .ToListAsync();
                memberRooms.AddRange(ids);
            }
            // De-dupe in case two lookup tags catch the same room.
            memberRooms = memberRooms.Distinct().Take(5).ToList();

            int memberOrder = 0;
            foreach (var roomId in memberRooms)
            {
                db.PlaylistRooms.Add(new PlaylistRoomEntity
                {
                    PlaylistId = p.Id,
                    RoomId = roomId,
                    OrderIndex = memberOrder++,
                });
            }
            await db.SaveChangesAsync();
        }
    }
}
