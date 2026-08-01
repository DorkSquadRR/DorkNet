using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Photos;

/// <summary>
/// api.rec.net/api/photos/v1/* — social-feed wrapper around the
/// in-game camera. The camera itself uses storage.rec.net/upload with
/// FileType=Image to push the PNG bytes; that returns a Filename. The
/// watch's "Share" panel then POSTs here with that Filename, a caption
/// and any tagged players to create the public-feed photo.
///
/// Public reads (feed, by-player, by-room) are anonymous so the
/// feed.rec.net frontend can browse without an admin token.
/// Mutations (post, cheer, delete) require auth.
/// </summary>
[ApiController]
// Two hosts: api.rec.net is what the in-game watch hits; feed.rec.net
// also serves these endpoints so the public frontend website can do
// same-origin fetches without CORS.
[Route("api/photos/v1")]
public class PhotosController(
    DorkNetDbContext db,
    PlayerPresenceService presence,
    LevelService level,
    DomainConfig domain) : ControllerBase
{
    public sealed record PostPhotoRequest(
        string ImageName,
        string? Caption,
        string? TaggedPlayerIds,
        long? RoomId,
        bool? IsPublic);

    /// <summary>POST api/photos/v1/post — promote an uploaded image
    /// blob to the photo feed. Body: ImageName (filename returned by
    /// storage.rec.net/upload), Caption, TaggedPlayerIds (CSV),
    /// optional RoomId (defaults to caller's current room from
    /// PlayerPresenceService), IsPublic (defaults true). Returns the
    /// created PhotoEntity.</summary>
    [HttpPost("post")]
    [Authorize]
    public async Task<ActionResult> PostPhoto([FromBody] PostPhotoRequest body)
    {
        var me = this.RequireCurrentPlayerId();
        if (string.IsNullOrWhiteSpace(body.ImageName))
            return BadRequest(new { error = "missing_image_name" });

        // Verify the blob actually exists and was uploaded by us. Without
        // this check anyone could cite another player's filename and
        // post-attribute the photo to themselves.
        var blob = await db.RoomDataBlobs
            .Where(b => b.BlobName == body.ImageName)
            .Select(b => new { b.UploadedByPlayerId })
            .FirstOrDefaultAsync();
        if (blob is null)
            return NotFound(new { error = "image_blob_not_found" });
        if (blob.UploadedByPlayerId != me)
            return Forbid();

        // Auto-resolve current room if the caller didn't supply one.
        var roomId = body.RoomId ?? presence.GetRoom(me)?.RoomId ?? 0;

        var photo = new PhotoEntity
        {
            UploaderPlayerId = me,
            BlobName = body.ImageName,
            Caption = (body.Caption ?? string.Empty).Trim(),
            TaggedPlayerIdsCsv = NormaliseTaggedIds(body.TaggedPlayerIds, exceptId: me),
            RoomId = roomId,
            IsPublic = body.IsPublic ?? true,
        };
        db.Photos.Add(photo);
        await db.SaveChangesAsync();

        // Posting a photo gives a small XP bump — encourages camera use.
        await level.AwardXpAsync(me, LevelService.InventionSavedXp, $"photo_posted:{photo.Id}");

        // No real-time push for photo tags: there is no matching client
        // message type, and the old SubscriptionUpdateProfile
        // {Reason:"TaggedInPhoto"} blob decoded to a blank account
        // ("Orphan player account (0)") without refreshing anything. The
        // "Photos of me" feed refreshes on the tagged player's next fetch.
        return Ok(ToDto(photo));
    }

    /// <summary>GET api/photos/v1/feed — public newest-first feed.
    /// Anonymous-safe (so the frontend website can render without a
    /// JWT). Soft-deleted and private photos are excluded.</summary>
    [HttpGet("feed")]
    [AllowAnonymous]
    public async Task<ActionResult> Feed([FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/by/{playerId} — photos posted by a
    /// specific player. Anonymous-safe. Private photos hidden unless
    /// the caller is the uploader (or an admin).</summary>
    [HttpGet("by/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> ByPlayer(long playerId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var me = this.CurrentPlayerId();
        var includePrivate = me == playerId || await IsAdminAsync(me);

        var q = db.Photos.Where(p => p.UploaderPlayerId == playerId && p.DeletedAt == null);
        if (!includePrivate) q = q.Where(p => p.IsPublic);
        var rows = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/of/{playerId} — photos the player
    /// is tagged in. Anonymous-safe.</summary>
    [HttpGet("of/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> OfPlayer(long playerId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var needle = $",{playerId},";
        // SQLite EF translates `string.Contains` to LIKE — wrapping the
        // CSV with leading/trailing commas avoids "12" matching "112".
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null &&
                ("," + p.TaggedPlayerIdsCsv + ",").Contains(needle))
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/in/{roomId} — photos taken in a
    /// specific room. Anonymous-safe.</summary>
    [HttpGet("in/{roomId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> InRoom(long roomId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.RoomId == roomId && p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/{id} — single photo detail.
    /// Increments ViewCount as a side effect (denormalised for the
    /// "trending" sort).</summary>
    [HttpGet("{id:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> Get(long id)
    {
        var p = await db.Photos.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null || p.DeletedAt is not null) return NotFound();
        if (!p.IsPublic)
        {
            var me = this.CurrentPlayerId();
            if (me != p.UploaderPlayerId && !await IsAdminAsync(me)) return NotFound();
        }
        p.ViewCount += 1;
        await db.SaveChangesAsync();
        var enriched = await EnrichAsync(new[] { p });
        return Ok(enriched.First());
    }

    /// <summary>POST api/photos/v1/{id}/cheer — like a photo. Idempotent
    /// per (caller, photo) pair. Pushes a notification to the
    /// uploader so their watch can refresh the count.</summary>
    [HttpPost("{id:long}/cheer")]
    [Authorize]
    public async Task<ActionResult> Cheer(long id)
    {
        var me = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null || photo.DeletedAt is not null) return NotFound();

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == me && c.TargetPhotoId == id &&
            c.TargetPlayerId == 0 && c.TargetRoomId == 0);
        if (existing is not null) return Ok(new { already_cheered = true });

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId = me,
            TargetPhotoId = id,
        });
        photo.CheerCount += 1;
        await db.SaveChangesAsync();

        if (photo.UploaderPlayerId != me)
        {
            await level.AwardXpAsync(photo.UploaderPlayerId,
                LevelService.CheerReceivedXp, $"photo_cheer:{id}");
            // No real-time push: no client message type for photo cheers, and
            // the old SubscriptionUpdateProfile blob only spawned a blank
            // "Orphan player account (0)". Count refreshes on next fetch.
        }
        return Ok(new { cheered = true, count = photo.CheerCount });
    }

    /// <summary>DELETE api/photos/v1/{id} — soft-delete. Allowed for
    /// the uploader or an admin. The blob bytes stay in
    /// RoomDataBlobs for audit; the row's DeletedAt timestamp hides
    /// it from feeds.</summary>
    [HttpDelete("{id:long}")]
    [Authorize]
    public async Task<ActionResult> Delete(long id)
    {
        var me = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null) return NotFound();
        if (photo.UploaderPlayerId != me && !await IsAdminAsync(me))
            return Forbid();
        photo.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    // ── 2023 home feed (Discovery host) ───────────────────────────────

    /// <summary>The only <c>FeedItemType</c> (client enum
    /// <c>DAAEHNDDCCH</c>) the 2023 feed list can render: an image.
    /// Two independent confirmations in the binary — the list spawner
    /// builds one <c>RRUI.Data.ImageFeedItemModel</c> per returned item
    /// (<c>Assembly-CSharp/RRUI/Data/FeedListModelController_NestedType_FeedListSpawnerImpl.txt:294</c>
    /// "044 Call ImageFeedItemModel.Set"), and the share deep-link
    /// handler maps the path segment "image" + an Int64 id onto an
    /// injected feed item constructed with the enum literal 0
    /// (<c>Assembly-CSharp/RecRoom/Sharing/LinkManager.txt:5646</c>
    /// Move rdx,"image" … :5665 Move rdx,0 … :5668 Call
    /// EKLOCIPFHII..ctor). FeedItemId is therefore an image id, which on
    /// DorkNet is <see cref="PhotoEntity.Id"/> — the same id space
    /// <c>api/images/v5/bulk</c> resolves.</summary>
    private const int FeedItemTypeImage = 0;

    /// <summary>Ranking revision stamped on the envelope and on every
    /// item. The client never interprets it: it copies the Int16 off the
    /// response into the list model and replays it in the impression
    /// telemetry it later posts back (<c>FeedListModel_NestedType_OLMJPGEMAKM.txt</c>
    /// "031 Move rax,[rdi+40] / 032 Move [rcx+280],rax" then
    /// "040 Call FeedListModel.LMDNOKJCBGE"). Bump it when the ordering
    /// below changes so the telemetry can tell the cohorts apart.</summary>
    private const short FeedAlgoVersion = 1;

    /// <summary>POST /feed/query — the 2023 client's personalised home
    /// feed. Host is Discovery (host enum 19) and the verb is POST:
    /// <c>RecNet.Runtime/KGLCPCEGCFF.txt:166-178</c> loads
    /// r9 = "feed/query", r8 = 19, rdx = 2 (BestHTTP POST) into the
    /// shared BNDIAONDFFF request ctor. The body is a RAW JSON document
    /// (the issuing method takes an already-serialised
    /// <c>System.String</c> and hands it to BNDIAONDFFF.FJLLPHFOOJJ),
    /// NOT form fields — so it is read straight off Request.Body.
    ///
    /// Request keys (reader BAEBDMPPKKE.txt:331-414, each registered in
    /// PascalCase/camelCase/lowercase): Take:Int32,
    /// ContinuationToken:String?, InjectedFeedItems:[{FeedItemType,
    /// FeedItemId}], FeedInstanceId:String?.
    ///
    /// Response is the generic result envelope AECMPGPHAII&lt;T&gt;
    /// {Success, Error, Value} (keys at AEGADMDIKNE.txt:243-286) wrapping
    /// BDIFPHKLFKD {Items, FeedInstanceId, FeedContext,
    /// FeedAlgorithmVersion} (keys at EHKEOCKBCDH.txt:331-414). Anything
    /// else — including a 404 — rejects the promise and the home tab
    /// renders its failure state.
    ///
    /// FeedContext is the continuation cursor, not a display string: the
    /// RefreshPage continuation reads field +32 (FeedContext) as the
    /// third element of its (count, items, nextToken) tuple and feeds it
    /// back as the next request's ContinuationToken
    /// (<c>FeedListModel_NestedType_OLMJPGEMAKM.txt</c> "062 Move r9,
    /// [rdi+32]"). An empty FeedContext therefore ends pagination.
    ///
    /// Anonymous-safe like the other feed reads; when the caller is
    /// authenticated the ordering is personalised (friends/subscriptions
    /// first) and blocked/ignored uploaders are filtered out.</summary>
    [HttpPost("/feed/query")]
    [AllowAnonymous]
    public async Task<ActionResult> FeedQuery()
    {
        var take = 20;
        string? continuationToken = null;
        string? requestedInstanceId = null;
        var requestedInjected = new List<long>();

        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;
                if (TryFeedProp(root, "Take", out var takeEl) &&
                    takeEl.ValueKind == JsonValueKind.Number && takeEl.TryGetInt32(out var t))
                    take = t;
                if (TryFeedProp(root, "ContinuationToken", out var ctEl) &&
                    ctEl.ValueKind == JsonValueKind.String)
                    continuationToken = ctEl.GetString();
                if (TryFeedProp(root, "FeedInstanceId", out var fiEl) &&
                    fiEl.ValueKind == JsonValueKind.String)
                    requestedInstanceId = fiEl.GetString();
                if (TryFeedProp(root, "InjectedFeedItems", out var injEl) &&
                    injEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in injEl.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object) continue;
                        // Non-image injected items can't be rendered by the
                        // 2023 list, so echoing them back would produce a
                        // blank card. Drop them.
                        var type = TryFeedProp(entry, "FeedItemType", out var tyEl) &&
                                   tyEl.ValueKind == JsonValueKind.Number &&
                                   tyEl.TryGetInt32(out var ty)
                            ? ty : FeedItemTypeImage;
                        if (type != FeedItemTypeImage) continue;
                        if (TryFeedProp(entry, "FeedItemId", out var idEl) &&
                            idEl.ValueKind == JsonValueKind.Number &&
                            idEl.TryGetInt64(out var id) && id > 0)
                            requestedInjected.Add(id);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Unparseable/empty body — treat as a default first-page pull
            // rather than 400, so a malformed retry still shows content.
        }

        take = Math.Clamp(take, 1, 50);
        var me = this.CurrentPlayerId();

        // Uploaders the caller can't see: anyone they blocked or ignored,
        // plus anyone who blocked them.
        var hidden = new List<long>();
        var affinity = new List<long>();
        if (me is long meId)
        {
            hidden = await db.Relationships
                .Where(r => (r.RequesterId == meId &&
                             (r.Status == RelationshipStatus.Blocked || r.Ignored)) ||
                            (r.TargetId == meId && r.Status == RelationshipStatus.Blocked))
                .Select(r => r.RequesterId == meId ? r.TargetId : r.RequesterId)
                .Distinct()
                .ToListAsync();

            var friends = await db.Relationships
                .Where(r => r.Status == RelationshipStatus.Friend &&
                            (r.RequesterId == meId || r.TargetId == meId))
                .Select(r => r.RequesterId == meId ? r.TargetId : r.RequesterId)
                .ToListAsync();
            var follows = await db.Subscriptions
                .Where(s => s.SubscriberPlayerId == meId)
                .Select(s => s.TargetPlayerId)
                .ToListAsync();
            affinity = friends.Concat(follows)
                .Where(id => id > 0 && !hidden.Contains(id))
                .Distinct()
                .ToList();
        }

        var resumed = TryDecodeFeedCursor(continuationToken,
            out var phase, out var cursorTicks, out var cursorId, out var carriedInjected);

        // Injected items are pinned to the head of the FIRST page only, in
        // the order the client asked for them (they come from a share
        // deep-link the player just followed). Their ids ride along in the
        // cursor so later pages don't repeat them organically.
        var injectedIds = resumed ? carriedInjected : requestedInjected.Distinct().ToArray();
        var itemIds = new List<long>();
        if (!resumed && injectedIds.Length > 0)
        {
            var pinned = await VisibleFeedPhotos(hidden)
                .Where(p => injectedIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();
            itemIds.AddRange(injectedIds.Where(pinned.Contains));
        }

        // Utc kind explicitly: the ticks came from a UtcNow-derived CreatedAt,
        // and Npgsql refuses to write an Unspecified DateTime to timestamptz.
        DateTime? cutoffAt = resumed && cursorTicks > 0 && cursorTicks <= DateTime.MaxValue.Ticks
            ? new DateTime(cursorTicks, DateTimeKind.Utc)
            : (DateTime?)null;
        var cutoffId = resumed ? cursorId : 0L;
        if (phase < 0) phase = 0;

        // Phase 0 = photos from people the caller follows or is friends
        // with; phase 1 = everyone else. Both are newest-first, and the
        // cursor is (CreatedAt, Id) so a photo posted mid-scroll can never
        // shift a row onto a page the client already consumed.
        while (itemIds.Count < take && phase <= 1)
        {
            if (phase == 0 && affinity.Count == 0)
            {
                phase = 1;
                cutoffAt = null;
                cutoffId = 0;
                continue;
            }

            var need = take - itemIds.Count;
            var q = VisibleFeedPhotos(hidden);
            if (injectedIds.Length > 0)
                q = q.Where(p => !injectedIds.Contains(p.Id));
            q = phase == 0
                ? q.Where(p => affinity.Contains(p.UploaderPlayerId))
                : q.Where(p => !affinity.Contains(p.UploaderPlayerId));
            if (cutoffAt is DateTime at)
            {
                var atId = cutoffId;
                q = q.Where(p => p.CreatedAt < at || (p.CreatedAt == at && p.Id < atId));
            }

            var batch = await q
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(need)
                .Select(p => new { p.Id, p.CreatedAt })
                .ToListAsync();
            itemIds.AddRange(batch.Select(b => b.Id));

            if (batch.Count < need)
            {
                phase++;
                cutoffAt = null;
                cutoffId = 0;
            }
            else
            {
                cutoffAt = batch[^1].CreatedAt;
                cutoffId = batch[^1].Id;
            }
        }

        // Empty FeedContext = end of the feed; the client stops paging.
        var nextContext = phase > 1
            ? string.Empty
            : EncodeFeedCursor(phase, cutoffAt?.Ticks ?? 0, cutoffId, injectedIds);

        return Ok(new
        {
            Success = true,
            Error = (string?)null,
            Value = new
            {
                Items = itemIds.Select(id => new
                {
                    FeedItemType = FeedItemTypeImage,
                    FeedItemId = id,
                    FeedAlgorithmVersion = FeedAlgoVersion,
                }).ToList(),
                // The client sends the instance id it generated for this
                // scroll session and stores whatever comes back; echoing it
                // keeps its impression telemetry stitched to one session.
                FeedInstanceId = string.IsNullOrWhiteSpace(requestedInstanceId)
                    ? Guid.NewGuid().ToString("N")
                    : requestedInstanceId,
                FeedContext = nextContext,
                FeedAlgorithmVersion = FeedAlgoVersion,
            },
        });
    }

    /// <summary>Photos eligible for the public home feed: live, public,
    /// and not from an uploader the caller blocked/ignored.</summary>
    private IQueryable<PhotoEntity> VisibleFeedPhotos(List<long> hidden)
    {
        var q = db.Photos.Where(p => p.IsPublic && p.DeletedAt == null);
        if (hidden.Count > 0)
            q = q.Where(p => !hidden.Contains(p.UploaderPlayerId));
        return q;
    }

    /// <summary>Case-insensitive property lookup — the 2023 generated
    /// writers emit PascalCase but their readers register all three
    /// casings, so tolerate whatever the client sends.</summary>
    private static bool TryFeedProp(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>Opaque base64url cursor handed to the client as
    /// FeedContext. Carries the phase, the (CreatedAt, Id) keyset
    /// position within that phase, and the pinned injected ids so they
    /// stay suppressed on later pages.</summary>
    private static string EncodeFeedCursor(int phase, long ticks, long lastId, IReadOnlyList<long> injected)
    {
        var raw = $"1|{phase}|{ticks}|{lastId}|{string.Join(',', injected)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeFeedCursor(string? token, out int phase,
        out long ticks, out long lastId, out long[] injected)
    {
        phase = 0;
        ticks = 0;
        lastId = 0;
        injected = Array.Empty<long>();
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');
            if (parts.Length != 5 || parts[0] != "1") return false;
            if (!int.TryParse(parts[1], out phase)) return false;
            if (!long.TryParse(parts[2], out ticks)) return false;
            if (!long.TryParse(parts[3], out lastId)) return false;
            injected = parts[4]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : 0L)
                .Where(v => v > 0)
                .ToArray();
            return true;
        }
        catch (FormatException)
        {
            // Garbage/stale cursor — restart the feed from the top instead
            // of failing the whole pull.
            return false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Trim/dedupe tagged ids and drop the uploader's own id.
    /// CSV in, CSV out.</summary>
    private static string NormaliseTaggedIds(string? csv, long exceptId)
    {
        if (string.IsNullOrWhiteSpace(csv)) return string.Empty;
        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0L)
            .Where(v => v > 0 && v != exceptId)
            .Distinct()
            .ToList();
        return string.Join(",", ids);
    }

    private static IEnumerable<long> ParseTagged(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Enumerable.Empty<long>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => long.TryParse(s, out var v) ? v : 0L)
                 .Where(v => v > 0);

    private async Task<bool> IsAdminAsync(long? playerId)
    {
        if (playerId is not long id) return false;
        return await db.Players.Where(p => p.Id == id).Select(p => p.IsAdmin).FirstOrDefaultAsync();
    }

    /// <summary>Materialise photo rows into the wire DTO shape. One
    /// extra DB hit fetches the uploader display names and room names
    /// in batch so the JSON has human-readable labels — saves the
    /// frontend from a per-row N+1.</summary>
    private async Task<List<object>> EnrichAsync(IEnumerable<PhotoEntity> photos)
    {
        var photoList = photos.ToList();
        if (photoList.Count == 0) return new();

        var uploaderIds = photoList.Select(p => p.UploaderPlayerId).Distinct().ToList();
        var roomIds = photoList.Select(p => p.RoomId).Where(id => id > 0).Distinct().ToList();

        var uploaderNames = await db.Players
            .Where(p => uploaderIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName, p.Username })
            .ToListAsync();
        var roomNames = await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        var uMap = uploaderNames.ToDictionary(u => u.Id);
        var rMap = roomNames.ToDictionary(r => r.Id);

        return photoList.Select(p =>
        {
            uMap.TryGetValue(p.UploaderPlayerId, out var u);
            rMap.TryGetValue(p.RoomId, out var r);
            return (object)new
            {
                p.Id,
                p.UploaderPlayerId,
                UploaderDisplayName = u?.DisplayName ?? $"Player_{p.UploaderPlayerId}",
                UploaderUsername = u?.Username ?? string.Empty,
                p.BlobName,
                ImageUrl = $"https://{domain.Sub("cdn")}/{p.BlobName}",
                p.Caption,
                p.RoomId,
                RoomName = r?.Name ?? string.Empty,
                TaggedPlayerIds = ParseTagged(p.TaggedPlayerIdsCsv).ToArray(),
                p.IsPublic,
                p.CheerCount,
                p.ViewCount,
                p.CreatedAt,
            };
        }).ToList();
    }

    private object ToDto(PhotoEntity p) => new
    {
        p.Id,
        p.UploaderPlayerId,
        p.BlobName,
        ImageUrl = $"https://{domain.Sub("cdn")}/{p.BlobName}",
        p.Caption,
        p.RoomId,
        TaggedPlayerIds = ParseTagged(p.TaggedPlayerIdsCsv).ToArray(),
        p.IsPublic,
        p.CheerCount,
        p.ViewCount,
        p.CreatedAt,
    };
}
