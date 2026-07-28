using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Keepsakes;

[ApiController]
[Route("api/keepsakes")]
[Authorize]
public class KeepsakesController(DorkNetDbContext db, DomainConfig domain) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>Marker category for collection rows; never surfaced as a keepsake instance.</summary>
    private const string CollectionCategory = "collection";

    /// <summary>
    /// Mirrors <c>KeepsakeGlobalConfigDTO.SocialXpBoostEnabled</c> below. The collect handler has
    /// to agree with what <c>globalconfig</c> advertises, so the flag lives in one place.
    /// </summary>
    private const bool SocialXpBoostEnabled = false;

    private const int KeepsakeRoomLimit = 64;

    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        await EnsureLoginKeepsakeAsync(Me);
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == Me && k.Category != CollectionCategory)
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    // KeepsakeCategoryConfigDTO = {KeepsakeCategoryId(enum), VisualId, LimitPerRoom, XpValue,
    // IconOutlineImageName, IconFilledImageName} - dump.cs:1234842-1234878; the accepted wire key
    // names are enumerated by its Utf8Json formatter (IsilDump/RecNet.Runtime/HIKOJENMDKE.txt:66-160
    // - "KeepsakeCategoryId"/"VisualId"/"LimitPerRoom"/"XpValue" plus camel/lower aliases).
    // KeepsakeCategoryId is the PIHCLHIKEPH enum (dump.cs:1199524: Explore=0, GreenPowerCore=1,
    // Present=2, PurplePowerCore=3, UnnamedKeepsakeNumber1..5 = 4..8), and the client folds this
    // list into a Dictionary<PIHCLHIKEPH, KeepsakeCategoryConfigDTO> (NCCLEJPIABA.DHMFBMMAGCL,
    // NCCLEJPIABA.txt:4204) that every placed instance is looked up in. The previous three-entry
    // list keyed by our internal account/event/room buckets covered ids 0-2 only, so six of the
    // nine placeable categories had no config - and, since collect prices the reward from XpValue,
    // no reward either.
    private static readonly (int Id, string VisualId)[] CategoryConfigs =
    [
        (0, "Explore"),
        (1, "GreenPowerCore"),
        (2, "Present"),
        (3, "PurplePowerCore"),
        (4, "UnnamedKeepsakeNumber1"),
        (5, "UnnamedKeepsakeNumber2"),
        (6, "UnnamedKeepsakeNumber3"),
        (7, "UnnamedKeepsakeNumber4"),
        (8, "UnnamedKeepsakeNumber5"),
    ];

    /// <summary>
    /// XP a single keepsake collection is worth. This is the one number both
    /// <c>api/keepsakes/categories</c> (as <c>XpValue</c>) and
    /// <c>api/keepsakes/{id}/collect</c> (as <c>TotalXp</c>) are derived from, so the toast the
    /// client renders after a collect matches the config it already downloaded.
    /// </summary>
    private const int KeepsakeXpValue = 25;

    [HttpGet("categories")]
    [AllowAnonymous]
    public IActionResult Categories()
    {
        var results = CategoryConfigs
            .Select(c => new
            {
                KeepsakeCategoryId = c.Id,
                c.VisualId,
                LimitPerRoom = KeepsakeRoomLimit,
                XpValue = KeepsakeXpValue,
                IconOutlineImageName = string.Empty,
                IconFilledImageName = string.Empty,
            })
            .ToArray();
        return Ok(new
        {
            Results = results,
            TotalResults = results.Length,
        });
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events()
    {
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == Me && k.Category == "event")
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("events/{eventId:long}")]
    public async Task<IActionResult> EventInstances(long eventId)
    {
        var eventKey = eventId.ToString(CultureInfo.InvariantCulture);
        // NOT filtered by PlayerId: Instances is the whole event's placement set (the hunt map),
        // CollectionRecords is only what the caller has picked up.
        var rows = await db.Keepsakes
            .Where(k => k.Category == "event"
                && (k.EventKey == eventKey
                    || k.EventKey == $"event:{eventKey}"
                    || k.EventKey == $"event/{eventKey}"
                    || EF.Functions.Like(k.EventKey, $"event:{eventKey}:%")
                    || EF.Functions.Like(k.EventKey, $"event/{eventKey}/%")))
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();

        // RecNet.KeepsakeProgressionEventInstancesDTO has exactly two members, both
        // List<KeepsakeRoomInstanceIdsDTO> (= {Int64, List<Guid>}) - dump.cs:1234798-1234810,
        // ctor(List<KeepsakeRoomInstanceIdsDTO>, List<KeepsakeRoomInstanceIdsDTO>) in
        // IsilDump/RecNet.Runtime/RecNet/KeepsakeProgressionEventInstancesDTO.txt:14.
        // Emitting the flat keepsake rows here left RoomId=0 / KeepsakeInstanceIds=null per entry.
        // Key names follow the same RecNet naming the room endpoint below already uses; the literal
        // JSON names live in Newtonsoft attribute metadata the dumps do not render, so they are
        // unverified - Json.NET matching is case-insensitive and ignores unknown members.
        //
        // CollectionRecords is what the CALLER has picked up, which is a different set from what the
        // caller PLACED: it is fed by the collect handler below (Category=CollectionCategory rows).
        var eventInstanceIds = rows
            .Select(r => TryParseInstanceKey(r.EventKey, out _, out _, out _, out var id) ? id : StableKeepsakeInstanceId(r.Id))
            .ToHashSet();
        var collected = (await LoadMyCollectionsAsync())
            .Where(c => eventInstanceIds.Contains(c.InstanceId))
            .GroupBy(c => c.RoomId)
            .Select(group => (object)new
            {
                RoomId = group.Key,
                KeepsakeInstanceIds = group.Select(c => c.InstanceId).Distinct().Select(id => id.ToString("D")).ToList(),
            })
            .ToList();

        return Ok(new
        {
            Instances = GroupInstanceIdsByRoom(rows),
            CollectionRecords = collected,
        });
    }

    [HttpGet("rooms/{roomId:long}")]
    public async Task<IActionResult> RoomKeepsakes(long roomId)
    {
        var roomExists = await db.Rooms.AsNoTracking().AnyAsync(r => r.Id == roomId);
        if (!roomExists) return NotFound();

        var roomIdText = roomId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rows = await db.Keepsakes
            .Where(k => k.Category == "room"
                && (k.EventKey == $"room:{roomIdText}"
                    || k.EventKey == $"room/{roomIdText}"
                    || k.EventKey == roomIdText
                    || EF.Functions.Like(k.EventKey, $"room:{roomIdText}:%")
                    || EF.Functions.Like(k.EventKey, $"room/{roomIdText}/%")))
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        var instances = rows.Select(row => ToRoomInstanceWire(row, roomId)).ToList();

        // KeepsakeCollectionRecordDTO = {Int32 AccountId, Guid KeepsakeInstanceId, DateTime
        // CollectedAt} - dump.cs:1234642-1234666, wire keys confirmed by its Utf8Json formatter
        // (IsilDump/RecNet.Runtime/CNNAFLNKDCL.txt:48,75,99). Scoped to the caller: the DTO carries
        // AccountId so the client can filter, but returning only the caller's rows is correct under
        // either reading and keeps other players' pickups out of the payload.
        var collectPrefix = $"collect:{roomIdText}:";
        var me = Me;
        var collectionRecords = await db.Keepsakes
            .Where(k => k.PlayerId == me
                && k.Category == CollectionCategory
                && EF.Functions.Like(k.EventKey, collectPrefix + "%"))
            .ToListAsync();

        return Ok(new
        {
            Instances = instances,
            CollectionRecords = collectionRecords
                .Where(k => TryParseCollectionKey(k.EventKey, out _, out _))
                .Select(k =>
                {
                    TryParseCollectionKey(k.EventKey, out _, out var instanceId);
                    return (object)new
                    {
                        AccountId = k.PlayerId is > 0 and <= int.MaxValue ? (int)k.PlayerId : 0,
                        KeepsakeInstanceId = instanceId.ToString("D"),
                        CollectedAt = k.EarnedAt,
                    };
                })
                .ToList(),
            KeepsakeProgressionEventIds = Array.Empty<long>(),
        });
    }

    /// <summary>
    /// Removes a placed keepsake instance. The 2023 client issues
    /// <c>DELETE api/keepsakes/{guid}</c> - <c>NCCLEJPIABA.JBKENCNIEPA(System.Guid)</c> builds
    /// <c>String.Format("{0}/{1}", "api/keepsakes", guid)</c> and passes verb 4 (DELETE) to the
    /// BNDIAONDFFF ctor (NCCLEJPIABA.txt:1544, 1687-1698 - <c>Move rdx, 4</c>). Its return type is
    /// the non-generic <c>LDGADANDBIO</c> promise, so no response body is deserialised; a bare 200
    /// is enough. Client-side the tool refuses unless the caller is at least a room co-owner
    /// ("Must be at least a room co-owner to modify keepsakes.",
    /// RecRoom.Keepsakes.Runtime/PDFJLLECNBE.txt:3363), which we re-check server-side.
    /// </summary>
    [HttpDelete("{keepsakeInstanceId:guid}")]
    public async Task<IActionResult> Delete(Guid keepsakeInstanceId)
    {
        var instance = await FindInstanceAsync(keepsakeInstanceId);
        if (instance is null) return NotFound();

        var roomId = TryParseInstanceKey(instance.EventKey, out var parsedRoomId, out _, out _, out _)
            ? parsedRoomId
            : 0;
        if (!await CanModifyInstanceAsync(instance, roomId, Me)) return Forbid();

        // Drop the collection rows for this instance too, or they linger as records pointing at a
        // keepsake nobody can see and keep suppressing a future re-placement's collect.
        var suffix = keepsakeInstanceId.ToString("N");
        var orphanedCollections = await db.Keepsakes
            .Where(k => k.Category == CollectionCategory && EF.Functions.Like(k.EventKey, $"%:{suffix}"))
            .ToListAsync();

        db.Keepsakes.Remove(instance);
        db.Keepsakes.RemoveRange(orphanedCollections
            .Where(k => TryParseCollectionKey(k.EventKey, out _, out var id) && id == keepsakeInstanceId));
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Records that the caller picked up a placed keepsake and reports the XP it was worth. The
    /// 2023 client issues <c>POST api/keepsakes/{guid}/collect</c> -
    /// <c>NCCLEJPIABA.HFGKFAHFELM(System.Guid)</c> formats <c>"{0}/{1}/collect"</c> over
    /// "api/keepsakes" and passes verb 2 (POST) (NCCLEJPIABA.txt:1738, 1963-1974 -
    /// <c>Move rdx, 2</c>), with no request body. The response type is
    /// <c>FGLDKEJLAKB&lt;DHNBKMHDANK&gt;</c>; DHNBKMHDANK is a two-Int32 object whose Utf8Json
    /// formatter writes "TotalXp" and "SocialBoostXp" (RecNet.Runtime/PKCMBJFBHBO.txt:42,69,100,121
    /// - reader also accepts totalXp/socialBoostXp). The consumer computes
    /// <c>TotalXp - SocialBoostXp</c> for the base figure and shows the boost separately
    /// (PDFJLLECNBE_NestedType_LKIPMJFEAFK.txt:82-99), so TotalXp is the whole award, boost
    /// included - not a running account total.
    /// </summary>
    [HttpPost("{keepsakeInstanceId:guid}/collect")]
    public async Task<IActionResult> Collect(Guid keepsakeInstanceId)
    {
        var instance = await FindInstanceAsync(keepsakeInstanceId);
        if (instance is null) return NotFound();

        var parsed = TryParseInstanceKey(instance.EventKey, out var roomId, out _, out var categoryConfigId, out _);
        if (!parsed)
        {
            roomId = 0;
            categoryConfigId = CategoryId(instance.Category);
        }

        var me = Me;
        var collectKey = BuildCollectionKey(roomId, keepsakeInstanceId);
        var alreadyCollected = await db.Keepsakes
            .AnyAsync(k => k.PlayerId == me && k.Category == CollectionCategory && k.EventKey == collectKey);

        // Re-collecting is a no-op award rather than an error: the client only ever renders the two
        // numbers, and paying out twice for one instance would be a fabricated reward.
        if (alreadyCollected) return Ok(new { TotalXp = 0, SocialBoostXp = 0 });

        db.Keepsakes.Add(new KeepsakeEntity
        {
            PlayerId = me,
            Category = CollectionCategory,
            EventKey = collectKey,
            EarnedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return Ok(new
        {
            TotalXp = XpValueFor(categoryConfigId),
            // SocialBoostXp is the boost slice *of* TotalXp - the client renders
            // TotalXp - SocialBoostXp as the base figure - and globalconfig above advertises
            // SocialXpBoostEnabled=false, so there is no slice and the whole award is base XP.
            SocialBoostXp = 0,
        });
    }

    [HttpGet("globalconfig")]
    [AllowAnonymous]
    public IActionResult GlobalConfig() => Ok(new
    {
        KeepsakeFeatureEnabled = true,
        KeepsakeRoomLimit,
        SocialXpBoostEnabled,
    });

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var req = await ReadRequestAsync();

        // 2023 client: FGLDKEJLAKB<System.Guid> OPPHCOJHEJI(HMECHOKOCBB category)
        // (NCCLEJPIABA.txt:1146) POSTs a RecNet.AddKeepsakeInstanceRequest
        // {Int64 RoomId, Int64? SubRoomId, Int32 KeepsakeCategory} - dump.cs:1234509-1234525,
        // field offsets 0x10/0x18/0x28 in AddKeepsakeInstanceRequest.txt - and deserialises the
        // response as a BARE Guid, not an object. The request is refused client-side for offline
        // rooms ("Cannot add keepsakes in offline rooms", NCCLEJPIABA.txt:1533), so RoomId is
        // always a real room here; its presence is what selects this path over the legacy DTO.
        if (req.RoomId is > 0)
        {
            var roomId = req.RoomId.Value;
            if (!await db.Rooms.AsNoTracking().AnyAsync(r => r.Id == roomId)) return NotFound();

            var instanceId = Guid.NewGuid();
            db.Keepsakes.Add(new KeepsakeEntity
            {
                PlayerId = Me,
                Category = "room",
                EventKey = BuildRoomInstanceKey(roomId, req.SubRoomId, req.KeepsakeCategory ?? 0, instanceId),
                EarnedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Ok(instanceId);
        }

        var key = Trim(req.EventKey, 128);
        if (key.Length == 0) key = $"manual:{Guid.NewGuid():N}";
        var category = Trim(req.Category, 64);
        if (category.Length == 0) category = "event";

        var existing = await db.Keepsakes.FirstOrDefaultAsync(k =>
            k.PlayerId == Me && k.EventKey == key);
        if (existing is null)
        {
            existing = new KeepsakeEntity
            {
                PlayerId = Me,
                EventKey = key,
            };
            db.Keepsakes.Add(existing);
        }

        existing.Category = category;
        existing.Title = Trim(req.Title, 128);
        existing.Description = Trim(req.Description, 1024);
        existing.ImageName = Trim(req.ImageName, 256);
        existing.EarnedAt = req.EarnedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(existing));
    }

    private async Task EnsureLoginKeepsakeAsync(long playerId)
    {
        const string key = "first-login";
        if (await db.Keepsakes.AnyAsync(k => k.PlayerId == playerId && k.EventKey == key))
            return;
        db.Keepsakes.Add(new KeepsakeEntity
        {
            PlayerId = playerId,
            Category = "account",
            EventKey = key,
            Title = "First Login",
            Description = "Joined this server.",
        });
        await db.SaveChangesAsync();
    }

    private async Task<KeepsakeRequest> ReadRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new KeepsakeRequest
            {
                RoomId = long.TryParse(form["RoomId"].FirstOrDefault() ?? form["roomId"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formRoom) ? formRoom : null,
                SubRoomId = long.TryParse(form["SubRoomId"].FirstOrDefault() ?? form["subRoomId"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formSubRoom) ? formSubRoom : null,
                KeepsakeCategory = int.TryParse(form["KeepsakeCategory"].FirstOrDefault() ?? form["keepsakeCategory"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formCategory) ? formCategory : null,
                Category = form["category"].FirstOrDefault() ?? form["Category"].FirstOrDefault(),
                EventKey = form["eventKey"].FirstOrDefault() ?? form["EventKey"].FirstOrDefault(),
                Title = form["title"].FirstOrDefault() ?? form["Title"].FirstOrDefault(),
                Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                ImageName = form["imageName"].FirstOrDefault() ?? form["ImageName"].FirstOrDefault(),
                EarnedAt = DateTime.TryParse(form["earnedAt"].FirstOrDefault() ?? form["EarnedAt"].FirstOrDefault(), out var earned) ? earned : null,
            };
        }

        // The 2023 client sends a RAW JSON body, not a form: NCCLEJPIABA.txt:1458-1465 serialises the
        // whole AddKeepsakeInstanceRequest in one call and hands the string to BNDIAONDFFF's body
        // setter. Read it as a document rather than binding a DTO so the numeric KeepsakeCategory and
        // the legacy string Category can share one wire slot, and so key-name aliases are cheap.
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            var root = doc.RootElement;
            return new KeepsakeRequest
            {
                RoomId = ReadInt64(root, "RoomId"),
                SubRoomId = ReadInt64(root, "SubRoomId"),
                // "Category" is listed as an alias only for the numeric form; when it carries the
                // legacy string ("event"/"room") ReadInt64 declines it and ReadString picks it up.
                KeepsakeCategory = (int?)ReadInt64(root, "KeepsakeCategory", "KeepsakeCategoryConfigId", "Category"),
                Category = ReadString(root, "Category"),
                EventKey = ReadString(root, "EventKey"),
                Title = ReadString(root, "Title"),
                Description = ReadString(root, "Description"),
                ImageName = ReadString(root, "ImageName"),
                EarnedAt = DateTime.TryParse(ReadString(root, "EarnedAt"), out var earnedAt) ? earnedAt : null,
            };
        }
        catch (JsonException)
        {
            return new KeepsakeRequest();
        }
    }

    private static bool TryFindMember(JsonElement root, string[] names, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static long? ReadInt64(JsonElement root, params string[] names)
    {
        if (!TryFindMember(root, names, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var number)) return number;
        if (el.ValueKind == JsonValueKind.String
            && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
        => TryFindMember(root, names, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private object ToWire(KeepsakeEntity row) => new
    {
        row.Id,
        row.PlayerId,
        row.Category,
        row.EventKey,
        row.Title,
        row.Description,
        row.ImageName,
        ImageUrl = string.IsNullOrWhiteSpace(row.ImageName) ? string.Empty : $"https://{domain.Sub("cdn")}/{row.ImageName}",
        row.EarnedAt,
    };

    private static object ToRoomInstanceWire(KeepsakeEntity row, long roomId)
    {
        var placedByAccountId = row.PlayerId is > 0 and <= int.MaxValue
            ? (int)row.PlayerId
            : 0;
        var parsed = TryParseInstanceKey(row.EventKey, out var keyRoomId, out var subRoomId, out var categoryConfigId, out var instanceId);
        return new
        {
            KeepsakeInstanceId = (parsed ? instanceId : StableKeepsakeInstanceId(row.Id)).ToString("D"),
            // KeepsakeCategory is the visual config id (enum PIHCLHIKEPH: Explore=0, GreenPowerCore=1,
            // Present=2, PurplePowerCore=3, ... - dump.cs:1199524), NOT our account/event/room bucket,
            // so replay what the client sent; CategoryId() is only the fallback for legacy rows.
            KeepsakeCategoryConfigId = parsed ? categoryConfigId : CategoryId(row.Category),
            PlacedByAccountId = placedByAccountId,
            RoomId = parsed ? keyRoomId : roomId,
            SubRoomId = subRoomId,
        };
    }

    private static List<object> GroupInstanceIdsByRoom(IEnumerable<KeepsakeEntity> rows) => rows
        .Select(row => TryParseInstanceKey(row.EventKey, out var roomId, out _, out _, out var instanceId)
            ? (RoomId: roomId, InstanceId: instanceId)
            : (RoomId: 0L, InstanceId: StableKeepsakeInstanceId(row.Id)))
        .GroupBy(x => x.RoomId)
        .Select(group => (object)new
        {
            RoomId = group.Key,
            KeepsakeInstanceIds = group.Select(x => x.InstanceId).Distinct().Select(id => id.ToString("D")).ToList(),
        })
        .ToList();

    // KeepsakeEntity has no room/sub-room/instance-id columns, so a placed instance is encoded into
    // the existing 128-char EventKey. Layouts:
    //   room:{roomId}:{subRoomId|-}:{categoryConfigId}:{instanceGuid:N}   (client placements)
    //   event:{eventId}:{roomId}:{subRoomId|-}:{categoryConfigId}:{instanceGuid:N}
    // Both stay inside the LIKE prefixes the room/event queries above already use. Legacy short keys
    // ("room:{roomId}", "event:{eventId}", "first-login") fail to parse and fall back to the
    // MD5-derived instance id, which is what those rows were already reported with.
    private static string BuildRoomInstanceKey(long roomId, long? subRoomId, int categoryConfigId, Guid instanceId)
    {
        var room = roomId.ToString(CultureInfo.InvariantCulture);
        var subRoom = subRoomId?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var category = categoryConfigId.ToString(CultureInfo.InvariantCulture);
        return $"room:{room}:{subRoom}:{category}:{instanceId:N}";
    }

    private static bool TryParseInstanceKey(string? eventKey, out long roomId, out long? subRoomId, out int categoryConfigId, out Guid instanceId)
    {
        roomId = 0;
        subRoomId = null;
        categoryConfigId = 0;
        instanceId = Guid.Empty;

        var parts = (eventKey ?? string.Empty).Split(':');
        int i;
        if (parts.Length == 5 && string.Equals(parts[0], "room", StringComparison.OrdinalIgnoreCase)) i = 1;
        else if (parts.Length == 6 && string.Equals(parts[0], "event", StringComparison.OrdinalIgnoreCase)) i = 2;
        else return false;

        if (!Guid.TryParseExact(parts[i + 3], "N", out instanceId)) return false;
        if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId)) return false;
        if (long.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sub)) subRoomId = sub;
        if (!int.TryParse(parts[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out categoryConfigId)) categoryConfigId = 0;
        return true;
    }

    // A collection record is stored as its own keepsake row so no schema change is needed:
    //   Category = "collection", EventKey = "collect:{roomId}:{instanceGuid:N}"
    // The room id is baked in so the room endpoint can filter by prefix and the event endpoint can
    // group by room without re-reading the instance rows. "collect:" shares no prefix with the
    // "room:"/"event:" instance keys, so collection rows never leak into an instance query, and
    // Mine() filters the category out of the legacy keepsake list.
    private static string BuildCollectionKey(long roomId, Guid instanceId)
        => $"collect:{roomId.ToString(CultureInfo.InvariantCulture)}:{instanceId:N}";

    private static bool TryParseCollectionKey(string? eventKey, out long roomId, out Guid instanceId)
    {
        roomId = 0;
        instanceId = Guid.Empty;
        var parts = (eventKey ?? string.Empty).Split(':');
        if (parts.Length != 3 || !string.Equals(parts[0], "collect", StringComparison.OrdinalIgnoreCase)) return false;
        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId)) return false;
        return Guid.TryParseExact(parts[2], "N", out instanceId);
    }

    private async Task<List<(long RoomId, Guid InstanceId)>> LoadMyCollectionsAsync()
    {
        var me = Me;
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == me && k.Category == CollectionCategory)
            .Select(k => k.EventKey)
            .ToListAsync();
        var results = new List<(long RoomId, Guid InstanceId)>(rows.Count);
        foreach (var key in rows)
        {
            if (TryParseCollectionKey(key, out var roomId, out var instanceId))
                results.Add((roomId, instanceId));
        }

        return results;
    }

    /// <summary>
    /// Resolves the guid the client holds (from <c>api/keepsakes/rooms/{roomId}</c>, or the guid
    /// this server handed back from POST <c>api/keepsakes</c>) to the row that encodes it.
    /// </summary>
    private async Task<KeepsakeEntity?> FindInstanceAsync(Guid keepsakeInstanceId)
    {
        var suffix = keepsakeInstanceId.ToString("N");
        var candidates = await db.Keepsakes
            .Where(k => (k.Category == "room" || k.Category == "event")
                && EF.Functions.Like(k.EventKey, $"%:{suffix}"))
            .ToListAsync();
        var match = candidates.FirstOrDefault(k =>
            TryParseInstanceKey(k.EventKey, out _, out _, out _, out var id) && id == keepsakeInstanceId);
        if (match is not null) return match;

        // Rows written before the instance id was encoded into EventKey are reported to the client
        // with the MD5-derived StableKeepsakeInstanceId, which no LIKE can match. Resolve those by
        // id only - the projection keeps it to one column over the short-key rows.
        var legacyIds = await db.Keepsakes
            .Where(k => (k.Category == "room" || k.Category == "event")
                && !EF.Functions.Like(k.EventKey, "%:%:%:%"))
            .Select(k => k.Id)
            .ToListAsync();
        var legacyId = legacyIds.FirstOrDefault(id => StableKeepsakeInstanceId(id) == keepsakeInstanceId);
        return legacyId == 0 ? null : await db.Keepsakes.FirstOrDefaultAsync(k => k.Id == legacyId);
    }

    /// <summary>
    /// Placement and removal are gated client-side on room co-ownership
    /// ("Must be at least a room co-owner to modify keepsakes.", PDFJLLECNBE.txt:3363); the placer
    /// and server admins are allowed through as well.
    /// </summary>
    private async Task<bool> CanModifyInstanceAsync(KeepsakeEntity instance, long roomId, long playerId)
    {
        if (instance.PlayerId == playerId) return true;

        if (roomId > 0)
        {
            var creatorId = await db.Rooms.AsNoTracking()
                .Where(r => r.Id == roomId)
                .Select(r => (long?)r.CreatorPlayerId)
                .FirstOrDefaultAsync();
            if (creatorId == playerId) return true;

            var isCoOwner = await db.RoomRoles.AnyAsync(r =>
                r.RoomId == roomId && r.PlayerId == playerId && r.Role == 0 && r.Accepted);
            if (isCoOwner) return true;
        }

        return await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.IsAdmin)
            .FirstOrDefaultAsync();
    }

    private static int XpValueFor(int keepsakeCategoryConfigId)
        => CategoryConfigs.Any(c => c.Id == keepsakeCategoryConfigId) ? KeepsakeXpValue : 0;

    private static int CategoryId(string? category) =>
        string.Equals(category, "event", StringComparison.OrdinalIgnoreCase) ? 1 :
        string.Equals(category, "room", StringComparison.OrdinalIgnoreCase) ? 2 :
        0;

    private static Guid StableKeepsakeInstanceId(long id)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"keepsakeInstance:{id}"));
        return new Guid(hash);
    }

    private static string Trim(string? value, int max)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    public sealed class KeepsakeRequest
    {
        // RecNet.AddKeepsakeInstanceRequest (2023 client).
        public long? RoomId { get; set; }
        public long? SubRoomId { get; set; }
        public int? KeepsakeCategory { get; set; }

        public string? Category { get; set; }
        public string? EventKey { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public DateTime? EarnedAt { get; set; }
    }
}
