using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Backs the Rooms watch tab — search, hot, by-name, by-id, my-rooms,
/// my-bookmarks, room details. Also seeds the canonical "Rec Room
/// Original" rooms on first DB creation so the watch isn't empty.
/// </summary>
public class RoomService(DorkNetDbContext db)
{
    /// <summary>
    /// Idempotent seed of the well-known Rec Room Original rooms — pulled
    /// from the AGRoomRuntimeConfig.Locations array we walked in
    /// resources.assets. Each entry maps a friendly name (the URL segment
    /// used by /goto/room/{name}) to the LocationReplicationId GUID the
    /// client expects in MatchmakingResponse.RoomInstance.location.
    ///
    /// Seeded rooms are:
    /// - public (Accessibility=1)
    /// - AG (IsAGRoom=true — they are baked into the client's resources.assets,
    ///   not user-created; the client's matchmaking gates require the AG flag
    ///   to use the right join path)
    /// - tagged appropriately so the watch's hot/search filters surface them
    /// - high HotScore so they sort to the top of "Trending"
    /// </summary>
    public async Task SeedAsync()
    {
        // Idempotent fix-up: an earlier seed shipped these rooms with
        // IsAGRoom=false, which broke the rec center join (client returned
        // MatchmakingErrorCode.InsufficientSpace because the response shape
        // didn't match an AG instance). Patch any existing seeded rows that
        // still have the old flag instead of forcing a DB wipe.
        await db.Rooms
            .Where(r => r.Id >= 100 && r.Id < 1000 && !r.IsAGRoom && !r.IsDormRoom)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsAGRoom, true));

        // Backfill: existing DBs from before image thumbnails were
        // seeded have ImageName="" — fill them in by name so the watch
        // tile renders something rather than the broken-image fallback.
        // Idempotent: setting an existing image again is a no-op.
        await BackfillSeededImagesAsync();

        // Backfill: rooms predating Phase20RoomVisitsAndStats have
        // VisitorCount=0 even when VisitCount is seeded high. Estimate
        // unique visitors as ~1/3 of total joins (the official Rec.Net
        // ratio for a popular room); admin dashboards stay coherent
        // until live traffic catches up via RoomVisitEntity upserts.
        await BackfillVisitorCountAsync();

        if (await db.Rooms.AnyAsync())
        {
            // Seeds already ran — but earlier versions of the seed
            // didn't include all rooms. Add the missing ones.
            await EnsureMissingSeededRoomsAsync();
            return;
        }

        // Tags stored WITHOUT leading '#'. The watch UI prepends '#' when
        // rendering chips, so storing "#recroomoriginal" produces double-#
        // chips like "##recroomoriginal".
        //
        // ImageName is the cdn filename the watch downloads as the room
        // tile thumbnail (the small square card that shows up on the
        // "Trending"/"Rooms" tabs and on each room's detail page). The
        // watch prepends `https://cdn.rec.net/` and downloads as PNG.
        // We seed with the well-known RR room thumbnail filenames so the
        // catch-all CDN fallback returns the all-perms blob (an empty/
        // zero-byte image) and the watch falls back to the room's name
        // tile rather than crashing on a malformed thumbnail.
        var seeded = new (string Slug, string DisplayName, string Description, string LocationId, string Tags, string Image)[]
        {
            // All LocationReplicationIds below are AGRoomRuntimeConfig.Locations[].ReplicationId
            // values extracted from the 2020.03.06 client's resources.assets via
            // tools/extract-locations-binary.py. Output cached at data/room_locations.json.
            // Each Location has a SceneName matching one of the .unity scenes the client
            // ships (see tools/dump-all-scenes.py for the 96-scene index).
            ("RecCenter",        "Rec Center",         "A social hub to meet and mingle with friends new and old.", "cbad71af-0831-44d8-b8ef-69edafa841f6", "recroomoriginal,featured", "image_RecCenter.png"),
            ("3DCharades",       "3D Charades",        "Take turns drawing, acting, and guessing funny phrases with your friends!", "4078dfed-24bb-4db7-863f-578ba48d726b", "recroomoriginal", "image_3DCharades.png"),
            ("DiscGolfLake",     "DiscGolf Lake",      "A leisurely stroll through the grass. Throw your disc into the goal.", "f6f7256c-e438-4299-b99e-d20bef8cf7e0", "recroomoriginal,sport", "image_DiscGolfLake.png"),
            ("DiscGolfPropulsion","DiscGolf Propulsion","Throw your disc through hazards and around wind machines on this challenging course!", "d9378c9f-80bc-46fb-ad1e-1bed8a674f55", "recroomoriginal,sport", "image_DiscGolfPropulsion.png"),
            ("Dodgeball",        "Dodgeball",          "Throw dodgeballs to knock out your friends in this gym classic!", "3d474b26-26f7-45e9-9a36-9b02847d5e6f", "recroomoriginal,sport", "image_Dodgeball.png"),
            ("Paddleball",       "Paddleball",         "A simple rally game between two players in a plexiglass tube with a zero-g ball.", "d89f74fa-d51e-477a-a425-025a891dd499", "recroomoriginal,sport", "image_Paddleball.png"),
            ("Paintball",        "Paintball",          "Red and Blue teams splat each other in capture the flag and team battle.", "e122fe98-e7db-49e8-a1b1-105424b6e1f0", "recroomoriginal,sport", "image_Paintball.png"),
            // Soccer scene = soccer.unity, Location ReplicationId verified
            // (was previously mis-mapped to IsleOfLostSkulls' GUID).
            ("Soccer",           "Soccer",             "Teams of three run around slamming themselves into an over-sized soccer ball. Goal!", "6d5eea4b-f069-4ed0-9916-0e2f07df0d03", "recroomoriginal,sport", "image_Soccer.png"),
            // LaserTag in the 2020 build IS the CyberJunkCity arena scene
            // (Arena_Cyberjunk_City.unity). Was previously mis-mapped to
            // soccer's GUID. Hangar (Arena_Hangar3.unity) is the alternate
            // map players reach via the in-game lobby.
            ("LaserTag",         "Laser Tag",          "Teams battle each other and waves of robots in a totally cyber neon future city.", "9d6456ce-6264-48b4-808d-2d96b3d91038", "recroomoriginal,sport", "image_LaserTag.png"),
            ("CyberJunkCity",    "Laser Tag CyberJunk","Teams battle each other and waves of robots in a totally cyber neon future city.", "9d6456ce-6264-48b4-808d-2d96b3d91038", "recroomoriginal,sport", "image_CyberJunkCity.png"),
            ("LaserTagHangar",   "Laser Tag Hangar",   "Teams battle in an industrial warehouse map.", "239e676c-f12f-489f-bf3a-d4c383d692c3", "recroomoriginal,sport", "image_LaserTag.png"),
            ("RecRoyaleSquads",  "Rec Royale Squads",  "Squads of three battle it out on Frontier Island. Last squad standing wins!", "253fa009-6e65-4c90-91a1-7137a56a267f", "recroomoriginal,featured,sport", "image_RecRoyaleSquads.png"),
            // RecRoyaleSolos was previously pointing at a Home location
            // GUID (85b43509-…); the real Solos Location is b010171f-….
            ("RecRoyaleSolos",   "Rec Royale Solos",   "Battle it out on Frontier Island. Last person standing wins!", "b010171f-4875-4e89-baba-61e878cd41e1", "recroomoriginal,sport", "image_RecRoyaleSolos.png"),
            ("GoldenTrophy",     "Quest For The Golden Trophy", "The goblin king stole Coach's Golden Trophy. Team up and embark on an epic quest to recover it!", "91e16e35-f48f-4700-ab8a-a1b79e50e51b", "recroomoriginal,quest", "image_GoldenTrophy.png"),
            ("TheRiseofJumbotron","The Rise of Jumbotron", "Robot invaders threaten the galaxy! Team up with your friends and bring the laser heat!", "acc06e66-c2d0-4361-b0cd-46246a4c455c", "recroomoriginal,quest", "image_TheRiseofJumbotron.png"),
            ("CrimsonCauldron",  "Curse of the Crimson Cauldron", "Can your band of adventurers brave the enchanted wilds, and lift the curse of the crimson cauldron?", "949fa41f-4347-45c0-b7ac-489129174045", "recroomoriginal,quest", "image_CrimsonCauldron.png"),
            // IsleOfLostSkulls was previously pointing at Quarry's GUID;
            // real Location ReplicationId is 7e01cfe0-… (scene Quest_Pirate1_additive).
            ("IsleOfLostSkulls", "The Isle of Lost Skulls", "Can your pirate crew get to the Isle, defeat its fearsome guardian, and escape with the gold?", "7e01cfe0-820a-406f-b1b3-0a5bf575235c", "recroomoriginal,quest", "image_IsleOfLostSkulls.png"),
            ("BloodMoon",        "Crescendo of the Blood Moon", "Brave the haunted halls of Castle Dracula and survive the night.", "49cb8993-a956-43e2-86f4-1318f279b22a", "recroomoriginal,quest", "image_CrimsonCauldron.png"),
            ("StuntRunner",      "Stunt Runner",       "A solo platforming gauntlet — sprint, climb and dodge to reach the trophy at the top.", "b7281665-a715-4051-826b-8e08e69c6172", "recroomoriginal,sport,quest", "image_StuntRunner.png"),
            ("Drive-In",         "Rec Drive-In",       "Watch movies, hang out with friends, or chill at the bar.", "65ddbb48-5a01-4e3e-972d-e5c7419e2bc3", "recroomoriginal,featured", "image_DriveIn.png"),
            // Hub / hang rooms — no specific gameplay, just shared spaces.
            ("Park",             "The Park",           "An outdoor park with picnic tables and lawn games — a chill place to hang out.", "0a864c86-5a71-4e18-8041-8124e4dc9d98", "recroomoriginal,featured", "image_Park.png"),
            ("PerformanceHall",  "Performance Hall",   "A big stage for live performances. Sing, perform, or just enjoy the show.", "9932f88f-3929-43a0-a012-a40b5128e346", "recroomoriginal", "image_PerformanceHall.png"),
            ("EventRoom",        "The Lounge",         "A modular event hall with movable furniture for parties and meetings.", "a067557f-ca32-43e6-b6e5-daaec60b4f5a", "recroomoriginal", "image_EventRoom.png"),
            ("BowlingAlley",     "Bowling Alley",      "Classic ten-pin bowling. Roll strikes, beat your friends.", "ae929543-9a07-41d5-8ee9-dbbee8c36800", "recroomoriginal,sport", "image_BowlingAlley.png"),
            // Sandbox templates (cloning canvases) — every "Home"-named
            // Location with a recognizable scene. Players land in these
            // when picking a stage for a custom room.
            ("River",            "River",              "An outdoor template for building. Mountains, river, and forest as your starting canvas.", "e122fe98-e7db-49e8-a1b1-105424b6e1f0", "recroomoriginal,template", "image_River.png"),
            ("Homestead",        "Homestead",          "A frontier-themed template — barn, fences, dusty trails. Build your homestead.", "a785267d-c579-42ea-be43-fec1992d1ca7", "recroomoriginal,template", "image_Homestead.png"),
            ("Quarry",           "Quarry",             "A rocky open-pit template, ideal for industrial and obstacle-course builds.", "ff4c6427-7079-4f59-b22a-69b089420827", "recroomoriginal,template", "image_Quarry.png"),
            ("Clearcut",         "Clearcut",           "An open clearing template — flat ground, mountains in the distance.", "380d18b5-de9c-49f3-80f7-f4a95c1de161", "recroomoriginal,template", "image_Clearcut.png"),
            ("Spillway",         "Spillway",           "A water-park template — pools, waterslides, and concrete chutes for action builds.", "58763055-2dfb-4814-80b8-16fac5c85709", "recroomoriginal,template", "image_Spillway.png"),
            ("MakerRoom",        "Maker Room",         "A blank-walled canvas. The classic starting point for a Maker Pen build.", "a75f7547-79eb-47c6-8986-6767abcb4f92", "recroomoriginal,template,featured", "image_RecCenter.png"),
        };

        // Stable id allocation: 100..1000 reserved for seeded rooms. User
        // rooms get ids above 1000 so /goto/room/{id} never collides with
        // a system room.
        long id = 100;
        // hot scores in descending order — first seeded entry sorts first.
        var hotScore = 1_000_000.0;
        foreach (var entry in seeded)
        {
            db.Rooms.Add(new RoomEntity
            {
                Id = id++,
                Name = entry.Slug,
                Description = entry.Description,
                CreatorPlayerId = 1,
                // Prefer the real Rec.Net CDN image (sourced via
                // tools/fetch-room-images.py and cached in
                // data/images/) over the placeholder image_X.png.
                ImageName = RoomImagesByName.TryGetValue(entry.Slug, out var realImg)
                    ? realImg : entry.Image,
                State = 0,
                Accessibility = 1,
                // Seeded rooms ARE the official AG (Against Gravity / Rec Room
                // Original) rooms — Rec Center, Paintball, Quests, etc. The
                // client treats AG rooms specially during the matchmaking
                // join flow (different room-property expectations, longer
                // capacity caps); marking them non-AG breaks the rec center
                // join with MatchmakingErrorCode.InsufficientSpace because
                // the client decides the response shape doesn't match a
                // real AG instance.
                IsAGRoom = true,
                IsDormRoom = false,
                CloningAllowed = false,
                LocationReplicationId = entry.LocationId,
                TagsCsv = entry.Tags,
                CheerCount = 1000,
                FavoriteCount = 500,
                VisitCount = 100_000,
                HotScore = hotScore,
            });
            hotScore -= 1.0;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Per-room thumbnail filenames. Real names sourced from
    /// the official Rec.Net public API
    /// (<c>https://apim.rec.net/rooms/rooms?name={name}&amp;include=0</c>)
    /// via <c>tools/fetch-room-images.py</c>; the cached PNGs live in
    /// <c>data/images/</c> and are served by the merged
    /// <see cref="Controllers.Cdn.CdnController"/> (img.* branch).
    /// Fallback to <c>image_{name}.png</c> placeholders for sub-rooms
    /// the public API doesn't expose by name (DormRoom, Paintball
    /// sub-maps, CyberJunkCity, etc).</summary>
    private static readonly Dictionary<string, string> RoomImagesByName = LoadRoomImages();

    private static Dictionary<string, string> LoadRoomImages()
    {
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CyberJunkCity"]   = "image_CyberJunkCity.png",
            ["Drive-In"]        = "image_DriveIn.png",
            ["River"]           = "image_River.png",
            ["Homestead"]       = "image_Homestead.png",
            ["Quarry"]          = "image_Quarry.png",
            ["Clearcut"]        = "image_Clearcut.png",
            ["Spillway"]        = "image_Spillway.png",
        };
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "room_images.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "room_images.json"),
        };
        var path = candidates.FirstOrDefault(System.IO.File.Exists);
        if (path is null) return fallback;
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            var real = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(fs)
                ?? new();
            // Real entries override fallbacks.
            foreach (var (k, v) in real) fallback[k] = v;
        }
        catch { /* fall through to fallback */ }
        return new Dictionary<string, string>(fallback, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One-time fixup so existing DBs (seeded before
    /// thumbnails were added, or when the image map updates with real
    /// CDN names) get their ImageName populated. Looks up the seeded
    /// image filename by room name and patches both blank rows AND
    /// rows still carrying placeholder <c>image_X.png</c> names so
    /// the real CDN-sourced image takes effect.</summary>
    private async Task BackfillSeededImagesAsync()
    {
        var rows = await db.Rooms
            .Where(r => r.Id >= 100 && r.Id < 1000)
            .ToListAsync();

        // The watch fetches each tile thumbnail by ImageName; a name
        // that doesn't resolve to a file on disk (or in S3) lands on
        // the transparent-PNG fallback, which the watch's image pipe
        // renders as the red "?" placeholder. For each seeded row pick
        // the best ImageName in this priority order: (1) the hash
        // filename from room_images.json, (2) the original
        // image_<slug>.png placeholder, (3) image_RecCenter.png as a
        // guaranteed-present fallback. Anything we pick must exist as
        // a file under data/images so the disk hit path serves it.
        const string ultimateFallback = "image_RecCenter.png";
        var imagesDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
        bool OnDisk(string name) =>
            !string.IsNullOrEmpty(name) && File.Exists(Path.Combine(imagesDir, name));

        var changed = 0;
        foreach (var r in rows)
        {
            string preferred;
            if (RoomImagesByName.TryGetValue(r.Name, out var realImg) && OnDisk(realImg))
                preferred = realImg;
            else if (OnDisk($"image_{r.Name}.png"))
                preferred = $"image_{r.Name}.png";
            else if (OnDisk(r.ImageName))
                preferred = r.ImageName;
            else
                preferred = ultimateFallback;

            if (r.ImageName != preferred)
            {
                r.ImageName = preferred;
                changed++;
            }
        }
        if (changed > 0) await db.SaveChangesAsync();
    }

    /// <summary>Set <see cref="RoomEntity.VisitorCount"/> for any
    /// row that has joins but no unique-visitor estimate yet. Two
    /// data sources, in priority order:
    ///
    ///   1. Real <see cref="RoomVisitEntity"/> rows (post-Phase20):
    ///      VisitorCount = COUNT(DISTINCT PlayerId) for that room.
    ///      Authoritative — this is the value the live upsert
    ///      maintains going forward.
    ///   2. Estimate from VisitCount when no per-player rows exist
    ///      (e.g. seeded rooms with VisitCount=100,000 from the
    ///      initial seed). Use VisitCount/3 — roughly the official
    ///      Rec.Net ratio of unique visitors to total joins for a
    ///      mature, popular room. Beats showing 0 visitors next
    ///      to 100K joins on the admin dashboard.
    /// </summary>
    private async Task BackfillVisitorCountAsync()
    {
        // Step 1: rebuild from real RoomVisitEntity rows.
        var realCounts = await db.RoomVisits
            .GroupBy(v => v.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Select(v => v.PlayerId).Distinct().Count() })
            .ToListAsync();
        var realByRoom = realCounts.ToDictionary(x => x.RoomId, x => x.Count);

        // Step 2: for any room without per-player rows, fall back to
        // VisitCount/3. Skip rooms whose VisitorCount already matches
        // the canonical value to keep the write set small.
        var rooms = await db.Rooms.ToListAsync();
        var changed = 0;
        foreach (var room in rooms)
        {
            int target;
            if (realByRoom.TryGetValue(room.Id, out var real))
            {
                target = real;
            }
            else if (room.VisitorCount == 0 && room.VisitCount > 0)
            {
                target = Math.Max(1, room.VisitCount / 3);
            }
            else
            {
                continue;
            }
            if (room.VisitorCount != target)
            {
                room.VisitorCount = target;
                changed++;
            }
        }
        if (changed > 0) await db.SaveChangesAsync();
    }

    /// <summary>Adds any seeded rooms that aren't yet in the DB. Used
    /// when an existing DB pre-dates a new room being added to the
    /// seed list (e.g. StuntRunner, the building templates) — without
    /// this, the user would have to wipe the DB to see them.</summary>
    private async Task EnsureMissingSeededRoomsAsync()
    {
        // Drop any StuntRunner row carrying our old placeholder GUID
        // (5b6e1a3f-…) so the next seed pass below can replace it with
        // the canonical b7281665-… id. Without this, an existing DB
        // keeps the broken row and /goto/room/StuntRunner still hits
        // "RecNet game session contains unknown scene location ID".
        const string oldBadGuid = "5b6e1a3f-3a8b-4c7c-9b88-4b51d2b0e1f4";
        var bad = await db.Rooms
            .Where(r => r.Name == "StuntRunner" && r.LocationReplicationId == oldBadGuid)
            .ToListAsync();
        if (bad.Count > 0)
        {
            db.Rooms.RemoveRange(bad);
            await db.SaveChangesAsync();
        }

        // Fix up rows whose LocationReplicationId was wrong in earlier
        // seed passes. Each (room name, real GUID) pair was verified
        // against AGRoomRuntimeConfig.Locations in resources.assets via
        // tools/extract-locations-binary.py. Only updates rows that
        // currently hold the WRONG GUID — already-correct or user-placed
        // rooms aren't touched.
        var corrections = new (string Name, string CorrectGuid)[]
        {
            ("Soccer",           "6d5eea4b-f069-4ed0-9916-0e2f07df0d03"),
            ("LaserTag",         "9d6456ce-6264-48b4-808d-2d96b3d91038"),
            ("RecRoyaleSolos",   "b010171f-4875-4e89-baba-61e878cd41e1"),
            ("IsleOfLostSkulls", "7e01cfe0-820a-406f-b1b3-0a5bf575235c"),
        };
        foreach (var (name, correct) in corrections)
        {
            await db.Rooms
                .Where(r => r.Name == name && r.LocationReplicationId != correct)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LocationReplicationId, correct));
        }

        var missing = new (string Slug, string DisplayName, string Description, string LocationId, string Tags, string Image)[]
        {
            ("StuntRunner",     "Stunt Runner",          "A solo platforming gauntlet — sprint, climb and dodge to reach the trophy at the top.", "b7281665-a715-4051-826b-8e08e69c6172", "recroomoriginal,sport,quest", "image_StuntRunner.png"),
            ("Drive-In",        "Rec Drive-In",          "Watch movies, hang out with friends, or chill at the bar.", "65ddbb48-5a01-4e3e-972d-e5c7419e2bc3", "recroomoriginal,featured", "image_DriveIn.png"),
            ("River",           "River",                 "An outdoor template for building. Mountains, river, and forest as your starting canvas.", "e122fe98-e7db-49e8-a1b1-105424b6e1f0", "recroomoriginal,template", "image_River.png"),
            ("Homestead",       "Homestead",             "A frontier-themed template — barn, fences, dusty trails. Build your homestead.", "a785267d-c579-42ea-be43-fec1992d1ca7", "recroomoriginal,template", "image_Homestead.png"),
            ("Quarry",          "Quarry",                "A rocky open-pit template, ideal for industrial and obstacle-course builds.", "ff4c6427-7079-4f59-b22a-69b089420827", "recroomoriginal,template", "image_Quarry.png"),
            ("Clearcut",        "Clearcut",              "An open clearing template — flat ground, mountains in the distance.", "380d18b5-de9c-49f3-80f7-f4a95c1de161", "recroomoriginal,template", "image_Clearcut.png"),
            ("Spillway",        "Spillway",              "A water-park template — pools, waterslides, and concrete chutes for action builds.", "58763055-2dfb-4814-80b8-16fac5c85709", "recroomoriginal,template", "image_Spillway.png"),
            // Phase 22: hub / hang rooms + maker canvases that weren't
            // in the original seed.
            ("Park",            "The Park",              "An outdoor park with picnic tables and lawn games — a chill place to hang out.", "0a864c86-5a71-4e18-8041-8124e4dc9d98", "recroomoriginal,featured", "image_Park.png"),
            ("PerformanceHall", "Performance Hall",      "A big stage for live performances. Sing, perform, or just enjoy the show.", "9932f88f-3929-43a0-a012-a40b5128e346", "recroomoriginal", "image_PerformanceHall.png"),
            ("EventRoom",       "The Lounge",            "A modular event hall with movable furniture for parties and meetings.", "a067557f-ca32-43e6-b6e5-daaec60b4f5a", "recroomoriginal", "image_EventRoom.png"),
            ("BowlingAlley",    "Bowling Alley",         "Classic ten-pin bowling. Roll strikes, beat your friends.", "ae929543-9a07-41d5-8ee9-dbbee8c36800", "recroomoriginal,sport", "image_BowlingAlley.png"),
            ("BloodMoon",       "Crescendo of the Blood Moon", "Brave the haunted halls of Castle Dracula and survive the night.", "49cb8993-a956-43e2-86f4-1318f279b22a", "recroomoriginal,quest", "image_CrimsonCauldron.png"),
            ("LaserTagHangar",  "Laser Tag Hangar",      "Teams battle in an industrial warehouse map.", "239e676c-f12f-489f-bf3a-d4c383d692c3", "recroomoriginal,sport", "image_LaserTag.png"),
            ("MakerRoom",       "Maker Room",            "A blank-walled canvas. The classic starting point for a Maker Pen build.", "a75f7547-79eb-47c6-8986-6767abcb4f92", "recroomoriginal,template,featured", "image_RecCenter.png"),
        };

        // Allocate ids above the highest existing seeded id so we don't
        // collide with a user room.
        var maxSeededId = await db.Rooms
            .Where(r => r.Id >= 100 && r.Id < 1000)
            .Select(r => (long?)r.Id)
            .MaxAsync() ?? 99;
        var nextId = Math.Max(maxSeededId + 1, 100);
        // Hot score below the existing seeded set so newcomers don't
        // jump to the top of the trending feed.
        var hotScore = 50_000.0;

        var existingNames = await db.Rooms
            .Where(r => r.Id >= 100 && r.Id < 1000)
            .Select(r => r.Name)
            .ToListAsync();
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var entry in missing)
        {
            if (existing.Contains(entry.Slug)) continue;
            db.Rooms.Add(new RoomEntity
            {
                Id = nextId++,
                Name = entry.Slug,
                Description = entry.Description,
                CreatorPlayerId = 1,
                // Prefer the real Rec.Net CDN image (sourced via
                // tools/fetch-room-images.py and cached in
                // data/images/) over the placeholder image_X.png.
                ImageName = RoomImagesByName.TryGetValue(entry.Slug, out var realImg)
                    ? realImg : entry.Image,
                State = 0,
                Accessibility = 1,
                IsAGRoom = true,
                IsDormRoom = false,
                CloningAllowed = false,
                LocationReplicationId = entry.LocationId,
                TagsCsv = entry.Tags,
                CheerCount = 200,
                FavoriteCount = 100,
                VisitCount = 10_000,
                HotScore = hotScore,
            });
            hotScore -= 1.0;
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    public Task<RoomEntity?> GetByIdAsync(long roomId) =>
        db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);

    public Task<RoomEntity?> GetByNameAsync(string name) =>
        db.Rooms.FirstOrDefaultAsync(r => r.Name == name);

    /// <summary>
    /// Get-or-create the personal-dorm <see cref="RoomEntity"/> for a
    /// given player. Each account owns exactly one dorm row, identified
    /// by <c>IsDormRoom = true</c> + <c>CreatorPlayerId == playerId</c>.
    /// The watch's "DormRoom" matchmaking call resolves to this row, so
    /// every player has their own first-class dorm with a real id (not
    /// the synthetic shared id=1 we used to fake) — that means /myrooms
    /// surfaces it, room-saves hit a real Rooms.id row, and a
    /// long-term moderation tool can flag/clean a single dorm without
    /// touching another player's.
    ///
    /// LocationReplicationId is the shared dorm-scene GUID (every
    /// dorm renders the same Unity scene; per-player customisation is
    /// the DormStateEntity blob layered on top).
    /// </summary>
    public async Task<RoomEntity> EnsurePersonalDormAsync(long playerId)
    {
        // Two rows back this player's dorm: a RoomEntity (the canonical
        // room record /goto/room/DormRoom resolves to) and a
        // DormStateEntity (the per-player save-blob pointer the
        // RoomsController.Details substitutes into the dorm response so
        // each player loads THEIR save instead of whoever-saved-last's).
        // Both are idempotent: we no-op if either side already exists,
        // and we always end with both rows present so the watch's
        // GetRoomData → /room/{blobName} flow has a deterministic
        // CurrentDataBlobName to fetch (empty string until the player
        // saves; CdnController.Serve treats empty as "return the
        // default dorm bytes").
        var existing = await db.Rooms
            .FirstOrDefaultAsync(r => r.IsDormRoom && r.CreatorPlayerId == playerId);
        var dormStateExists = await db.DormStates
            .AnyAsync(d => d.PlayerId == playerId);

        if (existing is not null && dormStateExists)
            return existing;

        if (existing is null)
        {
            // Idempotent unique name: collisions only happen if a
            // previous boot crashed mid-create. Suffix with the player
            // id so the unique-name constraint never bites.
            existing = new RoomEntity
            {
                Name = $"Dorm_{playerId}",
                Description = "Your private dorm — yours alone, decorated however you like.",
                CreatorPlayerId = playerId,
                ImageName = "",
                State = 0,
                Accessibility = 0, // private — only the owner enters
                IsAGRoom = false,
                IsDormRoom = true,
                CloningAllowed = false,
                SupportsVRLow = true,
                SupportsMobile = false,
                SupportsScreens = true,
                SupportsWalkVR = true,
                SupportsTeleportVR = true,
                AllowsJuniors = true,
                LocationReplicationId = "76d98498-60a1-430c-ab76-b54a29b7a163",
                TagsCsv = "dorm",
                CheerCount = 0,
                FavoriteCount = 0,
                VisitCount = 0,
                HotScore = 0,
            };
            db.Rooms.Add(existing);
        }

        if (!dormStateExists)
        {
            db.DormStates.Add(new DormStateEntity
            {
                PlayerId = playerId,
                // Empty until the player saves their dorm via Maker
                // Pen. CdnController.Serve sees the empty blob name in
                // the Details response and serves
                // RoomDataBlobService.GetDefaultBlob() back to the
                // watch — boots into the stock dorm scene without a
                // 500.
                CurrentDataBlobName = string.Empty,
            });
        }

        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>Run once at startup to backfill <see cref="RoomEntity"/>
    /// + <see cref="DormStateEntity"/> rows for every account that
    /// pre-dates the auto-create-on-signup work. Idempotent — calls
    /// <see cref="EnsurePersonalDormAsync"/> per missing player so the
    /// guarantee "every account has a dorm" holds even for accounts
    /// that signed in via a code path that didn't run the lazy-create.
    /// </summary>
    public async Task EnsureDormsForAllPlayersAsync()
    {
        // Players who don't have a dorm RoomEntity yet. Skip the system
        // account (Coach, id=1) — it owns the RR-Original rooms, not a
        // personal dorm. Skip players who already have BOTH rows.
        var missing = await db.Players
            .Where(p => p.Id != 1 &&
                (!db.Rooms.Any(r => r.IsDormRoom && r.CreatorPlayerId == p.Id) ||
                 !db.DormStates.Any(d => d.PlayerId == p.Id)))
            .Select(p => p.Id)
            .ToListAsync();

        if (missing.Count == 0) return;

        foreach (var pid in missing)
        {
            await EnsurePersonalDormAsync(pid);
        }
        // EnsurePersonalDormAsync already SaveChangesAsync'd per-call,
        // but log the aggregate once for ops visibility.
        // (Per-call save keeps each row's failure isolated; if one
        // EnsurePersonalDorm throws we still backfilled the rest.)
    }

    /// <summary>
    /// Hot-list query — drives `api/rooms/v1/hot?roomScoreType=...&tags=...`.
    /// Filters by tag prefix-match and orders by HotScore desc.
    /// The watch sends `?tags=%23community` (URL-encoded `#community`); we
    /// strip the leading `#` so it matches the bare tag names stored in
    /// TagsCsv.
    /// </summary>
    public async Task<List<RoomEntity>> HotAsync(string? tag, int take = 50)
    {
        // Exclude personal dorms — they're per-player private spaces
        // and have no business appearing in the watch's public room
        // browser (Trending / Search / BaseRooms all reach this query).
        // Same filter applied below to SearchAsync and CreatedByAsync.
        // HiddenFromBrowse keeps admin-utility rooms (MakerRoom,
        // EventRoom) and rooms-folded-into-others (Paintball maps,
        // LaserTag Hangar) out of every public discovery surface.
        // /goto-by-name and admin tools still find them via GetByNameAsync.
        IQueryable<RoomEntity> q = db.Rooms.Where(r => r.State == 0 && !r.IsDormRoom && !r.HiddenFromBrowse);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var bareTag = tag.TrimStart('#').Trim();
            if (bareTag.Length > 0)
            {
                var needle = $"%{bareTag}%";
                q = q.Where(r => EF.Functions.Like(r.TagsCsv, needle));
            }
        }
        return await q.OrderByDescending(r => r.HotScore).Take(take).ToListAsync();
    }

    /// <summary>
    /// Free-text search — drives `api/rooms/v1/search`. Matches Name,
    /// Description, and Tags substrings.
    ///
    /// Also handles the watch's "rooms by player" convention: when the
    /// watch's PlayerProfile screen opens its Rooms tab, it calls
    /// <c>Rooms.SearchRooms("@" + accountId)</c> instead of a dedicated
    /// per-player endpoint (verified via Cpp2IL — PlayerDetailsWatchUIFlow
    /// formats `@{accountId}` then passes it straight to SearchRooms).
    /// So a query of <c>@1</c> resolves to "every room Coach created"
    /// and surfaces the seeded Rec Room Originals on Coach's profile.
    /// </summary>
    public async Task<List<RoomEntity>> SearchAsync(string query, int take = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await HotAsync(null, take);

        // @<token> — watch's PlayerProfile "Rooms" tab. The token can be
        // either a numeric accountId (admin-SPA / API callers) OR a
        // username (the 2020 watch's actual wire — it formats
        // <c>"@" + Player.Username</c> at PlayerDetailsWatchUIFlow time
        // and posts that straight to <c>rooms/v2/search</c>, so the
        // value we receive is <c>@Alexa</c>, not <c>@1811750</c>).
        // Resolve to the target accountId either way so Coach's seeded
        // Rec Room Originals (CreatorPlayerId=1) show up on his
        // profile's Rooms tab.
        if (query.Length > 1 && query[0] == '@')
        {
            var suffix = query.AsSpan(1).ToString();
            long? accountId = long.TryParse(suffix, out var id) ? id : null;
            if (accountId is null)
            {
                // Case-insensitive username match — Rec Room treats
                // @Alexa, @alexa, and @ALEXA as the same player.
                var lower = suffix.ToLowerInvariant();
                accountId = await db.Players
                    .Where(p => p.Username.ToLower() == lower)
                    .Select(p => (long?)p.Id)
                    .FirstOrDefaultAsync();
            }
            if (accountId is long aid)
            {
                return await db.Rooms
                    .Where(r => r.CreatorPlayerId == aid && !r.IsDormRoom && r.State == 0)
                    .OrderByDescending(r => r.UpdatedAt)
                    .Take(take)
                    .ToListAsync();
            }
            // Unknown username — return empty rather than fall through
            // to substring search (which would match every room with
            // "Alexa" in its name).
            return new();
        }

        var needle = $"%{query}%";
        return await db.Rooms
            .Where(r => r.State == 0 && !r.IsDormRoom && !r.HiddenFromBrowse && (
                EF.Functions.Like(r.Name, needle) ||
                EF.Functions.Like(r.Description, needle) ||
                EF.Functions.Like(r.TagsCsv, needle)))
            .OrderByDescending(r => r.HotScore)
            .Take(take)
            .ToListAsync();
    }

    public Task<List<RoomEntity>> CreatedByAsync(long playerId) =>
        db.Rooms.Where(r => r.CreatorPlayerId == playerId && !r.IsDormRoom)
                .OrderByDescending(r => r.UpdatedAt)
                .ToListAsync();

    public async Task<List<RoomEntity>> BookmarkedByAsync(long playerId)
    {
        var ids = await db.RoomBookmarks
            .Where(b => b.PlayerId == playerId)
            .Select(b => b.RoomId)
            .ToListAsync();
        return await db.Rooms
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.HotScore)
            .ToListAsync();
    }

    public async Task BookmarkAsync(long playerId, long roomId)
    {
        var exists = await db.RoomBookmarks.AnyAsync(b => b.PlayerId == playerId && b.RoomId == roomId);
        if (exists) return;
        db.RoomBookmarks.Add(new RoomBookmarkEntity { PlayerId = playerId, RoomId = roomId });
        await db.SaveChangesAsync();
    }

    public async Task UnbookmarkAsync(long playerId, long roomId)
    {
        var existing = await db.RoomBookmarks
            .FirstOrDefaultAsync(b => b.PlayerId == playerId && b.RoomId == roomId);
        if (existing is null) return;
        db.RoomBookmarks.Remove(existing);
        await db.SaveChangesAsync();
    }

    /// <summary>Every seeded RR-Original room id. Watch uses this to
    /// flag "official" rooms in the browser.</summary>
    public Task<List<long>> AgRoomIdsAsync() =>
        db.Rooms.Where(r => r.IsAGRoom).Select(r => r.Id).ToListAsync();

    /// <summary>Top-N AG rooms by HotScore — the watch's Featured
    /// carousel.</summary>
    public Task<List<long>> FeaturedAgRoomIdsAsync(int take) =>
        db.Rooms
            .Where(r => r.IsAGRoom)
            .OrderByDescending(r => r.HotScore)
            .Take(Math.Clamp(take, 1, 100))
            .Select(r => r.Id)
            .ToListAsync();

    /// <summary>Idempotent room cheer. Returns the new count and
    /// whether the caller had already cheered (true = no-op).</summary>
    public async Task<(long NewCount, bool AlreadyCheered)> CheerRoomAsync(long playerId, long roomId, int type)
    {
        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == playerId && c.TargetRoomId == roomId &&
            c.TargetPlayerId == 0 && c.TargetPhotoId == 0 &&
            c.TargetInventionId == 0 && c.Type == type);
        if (existing is not null)
        {
            var cur = await db.Rooms.Where(r => r.Id == roomId)
                .Select(r => r.CheerCount).FirstOrDefaultAsync();
            return (cur, true);
        }

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId = playerId,
            TargetRoomId = roomId,
            Type = type,
        });
        await db.Rooms.Where(r => r.Id == roomId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CheerCount, r => r.CheerCount + 1)
                .SetProperty(r => r.HotScore, r => r.HotScore + 10.0));
        await db.SaveChangesAsync();

        var newCount = await db.Rooms.Where(r => r.Id == roomId)
            .Select(r => r.CheerCount).FirstOrDefaultAsync();
        return (newCount, false);
    }

    /// <summary>Idempotent uncheer. Returns true when the player had
    /// no prior cheer (no-op).</summary>
    public async Task<bool> UncheerRoomAsync(long playerId, long roomId, int type)
    {
        var row = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == playerId && c.TargetRoomId == roomId && c.Type == type);
        if (row is null) return true;

        db.Cheers.Remove(row);
        await db.Rooms.Where(r => r.Id == roomId && r.CheerCount > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CheerCount, r => r.CheerCount - 1)
                .SetProperty(r => r.HotScore, r => r.HotScore - 10.0));
        await db.SaveChangesAsync();
        return false;
    }

    /// <summary>Cheer count for a room + whether <paramref name="playerId"/>
    /// has cheered it. <paramref name="playerId"/> = 0 returns
    /// <c>iCheered = false</c> (anonymous reader).</summary>
    public async Task<(long Count, bool ICheered)> GetCheerStateAsync(long playerId, long roomId)
    {
        var count = await db.Rooms.Where(r => r.Id == roomId)
            .Select(r => r.CheerCount).FirstOrDefaultAsync();
        var iCheered = playerId != 0 && await db.Cheers
            .AnyAsync(c => c.FromPlayerId == playerId && c.TargetRoomId == roomId);
        return (count, iCheered);
    }

    /// <summary>
    /// Result of a CloneAsync call. Status codes match Rooms
    /// .CreateModifyRoomStatus (TypeDefIndex 11848):
    ///   0 = Success, 1 = Unknown, 4 = RoomDoesNotExist, 10 = DuplicateName,
    ///   11 = ReservedName, 12 = InappropriateName.
    /// On Success, Room is the new entity; otherwise null.
    /// </summary>
    public sealed record CloneResult(int Status, RoomEntity? Room);

    /// <summary>
    /// Clone an existing room into a new room owned by `playerId`. Copies
    /// most of the source room's properties (scene location, capability
    /// flags, accessibility) but resets visit/cheer counts and switches
    /// IsAGRoom=true so the cloned room shows up in `#community` rather
    /// than under Rec Room Originals.
    ///
    /// Returns a CloneResult with the matching CreateModifyRoomStatus code
    /// so the watch can show the right "name taken" / "no source" message.
    /// </summary>
    public async Task<CloneResult> CloneAsync(long sourceRoomId, string newName, long playerId)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return new CloneResult(12 /* InappropriateName */, null);
        newName = newName.Trim();

        var source = await db.Rooms.FirstOrDefaultAsync(r => r.Id == sourceRoomId);
        if (source is null)
            return new CloneResult(4 /* RoomDoesNotExist */, null);

        // Status 10 (DuplicateName) — surfaces in the watch as a friendlier
        // "name already taken" toast instead of a generic Unknown failure.
        if (await db.Rooms.AnyAsync(r => r.Name == newName))
            return new CloneResult(10 /* DuplicateName */, null);

        // User-room ids start above 1000 to avoid colliding with the
        // 100..1000 range reserved for seeded rooms.
        var nextId = await db.Rooms
            .Where(r => r.Id > 1000)
            .MaxAsync(r => (long?)r.Id) ?? 1000L;

        var clone = new RoomEntity
        {
            Id = nextId + 1,
            Name = newName,
            Description = source.Description,
            CreatorPlayerId = playerId,
            ImageName = "",
            State = 0,
            Accessibility = source.Accessibility,
            SupportsLevelVoting = source.SupportsLevelVoting,
            // User-cloned rooms are NOT AG-managed: the AG flag means
            // "owned by the Against Gravity seed account, baked into
            // resources.assets". A user's clone is their own
            // community-content room — tag it accordingly so it
            // shows up as Custom in admin and isn't shielded from
            // hard-delete. (Previously this was =true; that misled
            // both the admin badge AND the purge-refuses-canonical
            // gate, which is why "RR ORIGINAL"-labelled junk rooms
            // showed up that the admin couldn't purge.)
            IsAGRoom = false,
            IsDormRoom = false,
            CloningAllowed = source.CloningAllowed,
            SupportsVRLow = source.SupportsVRLow,
            SupportsMobile = source.SupportsMobile,
            SupportsScreens = source.SupportsScreens,
            SupportsWalkVR = source.SupportsWalkVR,
            SupportsTeleportVR = source.SupportsTeleportVR,
            AllowsJuniors = source.AllowsJuniors,
            RoomWarningMask = source.RoomWarningMask,
            CustomRoomWarning = source.CustomRoomWarning,
            DisableMicAutoMute = source.DisableMicAutoMute,
            LocationReplicationId = source.LocationReplicationId,
            TagsCsv = "community",              // tagged community since user-built
            CheerCount = 0,
            FavoriteCount = 0,
            VisitCount = 0,
            HotScore = 0,                       // brand new — sorts to bottom of hot
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Rooms.Add(clone);
        await db.SaveChangesAsync();
        return new CloneResult(0 /* Success */, clone);
    }

    /// <summary>
    /// Project a RoomEntity to the flat June-2018 wire room (RecNet
    /// Room.Deserialize, RecNet.cs:78244). This branch is 2018-only, so every
    /// room-returning endpoint emits this shape via the shared mapper — the
    /// 2020 RoomDetails/Scenes shape crashes the 2018 deserializer. See
    /// <see cref="Compat2018.Room2018Mapper"/>.
    /// </summary>
    public static object ToWireRoom(RoomEntity r) => Compat2018.Room2018Mapper.From(r);

    /// <summary>
    /// Idempotent admin overrides applied AFTER SeedAsync. Keeps the seed
    /// table itself untouched (so a clean DB build still seeds the
    /// canonical RR-Originals shape) while letting us:
    ///   • Rename BloodMoon → Crescendo + bind a fresh thumbnail.
    ///   • Fold the standalone Paintball map rooms (River, Homestead,
    ///     Quarry, Clearcut, Spillway, Drive-In) into Paintball as
    ///     RoomSceneEntity rows + hide their old standalone rows.
    ///   • Fold LaserTagHangar into LaserTag as a second scene and hide
    ///     the standalone Hangar row.
    ///   • Hide MakerRoom + EventRoom from browse (admin tools / "create
    ///     room" template flow still resolves them by name).
    /// Re-running on a DB that's already been overridden no-ops.
    /// </summary>
    public async Task ApplyCanonicalOverridesAsync(HttpClient http, string imagesDir)
    {
        // Hide admin-utility and folded-in rooms from browse.
        // CyberJunkCity + LaserTagHangar are now subrooms under LaserTag;
        // PerformanceHall + MakerRoom + EventRoom are admin-only.
        var namesToHide = new[]
        {
            "MakerRoom", "EventRoom", "PerformanceHall",
            "River", "Homestead", "Quarry", "Clearcut", "Spillway", "Drive-In",
            "LaserTagHangar", "CyberJunkCity", "BloodMoon"
        };
        await db.Rooms
            .Where(r => namesToHide.Contains(r.Name) && !r.HiddenFromBrowse)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HiddenFromBrowse, true));

        // Download external thumbnails. Saved under stable file names so
        // a second startup re-uses the local copy. ImageName is just the
        // file name; ImgController serves it from data/images/. Bowling
        // Alley pulls from the rec.net CDN id baked into room_images.json
        // (no UI for that yet) since the user didn't pass a custom URL.
        Directory.CreateDirectory(imagesDir);
        await TryDownloadAsync(http, "https://img.rec.net/by3mjs9jbozpdvu6g9aje7jgz.png",
            Path.Combine(imagesDir, "image_Crescendo.png"));
        await TryDownloadAsync(http,
            "https://static.wikia.nocookie.net/rec-room/images/4/48/Paintball_-_Key_Art.png/revision/latest/scale-to-width-down/1000?cb=20200513122350",
            Path.Combine(imagesDir, "image_PaintballKeyArt.png"));
        await TryDownloadAsync(http, "https://img.rec.net/8gwqibu0anm4j0rw54xo2y0yd.png",
            Path.Combine(imagesDir, "image_BowlingAlley.png"));
        await TryDownloadAsync(http, "https://img.rec.net/4o5lschc01nani8xeywao622n.png",
            Path.Combine(imagesDir, "image_Park.png"));

        // Rename BloodMoon → Crescendo AND bind its image off the same
        // entity reference. Previous version did a second
        // FirstOrDefault(Name == "Crescendo") after the rename, which
        // queries the DB (not the change tracker) — the row still had
        // Name="BloodMoon" because SaveChanges hadn't fired yet, so
        // the image binding silently no-op'd. Mutating the already-
        // loaded entity sidesteps that race.
        var crescendoRooms = await db.Rooms
            .Where(r => r.Name.ToLower() == "bloodmoon" || r.Name.ToLower() == "crescendo")
            .ToListAsync();
        var crescendo = crescendoRooms.FirstOrDefault(r =>
            string.Equals(r.Name, "Crescendo", StringComparison.OrdinalIgnoreCase));
        var bloodMoon = crescendoRooms.FirstOrDefault(r =>
            string.Equals(r.Name, "BloodMoon", StringComparison.OrdinalIgnoreCase));
        var shouldRenameBloodMoon = crescendo is null;
        crescendo ??= bloodMoon;
        if (crescendo is not null)
        {
            if (shouldRenameBloodMoon && crescendo.Name != "Crescendo")
                crescendo.Name = "Crescendo";
            if (string.IsNullOrWhiteSpace(crescendo.Description))
                crescendo.Description = "Brave the haunted halls of Castle Dracula and survive the night.";
            if (crescendo.ImageName != "image_Crescendo.png")
                crescendo.ImageName = "image_Crescendo.png";
        }
        foreach (var duplicate in crescendoRooms.Where(r => r.Id != crescendo?.Id))
        {
            duplicate.HiddenFromBrowse = true;
        }

        var paintball = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "Paintball");
        if (paintball is not null && paintball.ImageName != "image_PaintballKeyArt.png")
            paintball.ImageName = "image_PaintballKeyArt.png";

        var bowling = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "BowlingAlley");
        if (bowling is not null && bowling.ImageName != "image_BowlingAlley.png")
            bowling.ImageName = "image_BowlingAlley.png";

        var park = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "Park");
        if (park is not null && park.ImageName != "image_Park.png")
            park.ImageName = "image_Park.png";

        // Paintball sub-room scenes — one RoomSceneEntity row per map.
        // Each row carries the map's old standalone-room
        // LocationReplicationId so /goto/room/Paintball/{map} resolves
        // to the same Unity scene the standalone room used. OrderIndex
        // is stable so wire SubRoomId values don't shuffle between
        // server restarts.
        if (paintball is not null)
        {
            var paintballScenes = new (string Name, string Location)[]
            {
                ("River",     "e122fe98-e7db-49e8-a1b1-105424b6e1f0"),
                ("Homestead", "a785267d-c579-42ea-be43-fec1992d1ca7"),
                ("Quarry",    "ff4c6427-7079-4f59-b22a-69b089420827"),
                ("Clearcut",  "380d18b5-de9c-49f3-80f7-f4a95c1de161"),
                ("Spillway",  "58763055-2dfb-4814-80b8-16fac5c85709"),
                ("Drive-In",  "65ddbb48-5a01-4e3e-972d-e5c7419e2bc3"),
            };
            await EnsureScenesAsync(paintball.Id, paintballScenes);
        }

        // LaserTag sub-rooms: the lobby (current LaserTag scene =
        // CyberJunkCity arena) and the Hangar (was a standalone room).
        var lasertag = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "LaserTag");
        if (lasertag is not null)
        {
            var lasertagScenes = new (string Name, string Location)[]
            {
                ("CyberJunkCity", "9d6456ce-6264-48b4-808d-2d96b3d91038"),
                ("Hangar",        "239e676c-f12f-489f-bf3a-d4c383d692c3"),
            };
            await EnsureScenesAsync(lasertag.Id, lasertagScenes);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Adds any missing scene rows for a parent room without
    /// disturbing existing ones. Matches by (RoomId, Name) so a
    /// re-run that finds the scene already there is a no-op.</summary>
    private async Task EnsureScenesAsync(long roomId, (string Name, string Location)[] scenes)
    {
        var existing = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .Select(s => new { s.Name, s.OrderIndex })
            .ToListAsync();
        var existingNames = existing.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextOrder = existing.Count == 0 ? 0 : existing.Max(e => e.OrderIndex) + 1;
        foreach (var (name, location) in scenes)
        {
            if (existingNames.Contains(name)) continue;
            db.RoomScenes.Add(new RoomSceneEntity
            {
                RoomId = roomId,
                OrderIndex = nextOrder++,
                Name = name,
                RoomSceneLocationId = location,
                DataBlobName = "",
                MaxPlayers = 8,
                IsSandbox = false,
                CanMatchmakeInto = true,
                DataModifiedAt = DateTime.UtcNow,
            });
        }
    }

    private static async Task TryDownloadAsync(HttpClient http, string url, string targetPath)
    {
        // Skip if already on disk — admins can delete the file to force
        // a re-fetch on next startup.
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0) return;
        try
        {
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return;
            await File.WriteAllBytesAsync(targetPath, bytes);
        }
        catch
        {
            // Network blip on a fresh install just leaves the room with
            // its existing placeholder image — admin can re-trigger by
            // restarting once the URL's reachable.
        }
    }
}
