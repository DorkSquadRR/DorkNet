using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DorkNet.Models.Players;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using System.Security.Cryptography;

namespace DorkNet.Server.Services;

public class PlayerService(DorkNetDbContext db, RoomService rooms, ILogger<PlayerService> logger)
{
    /// <summary>
    /// Default outfit GUIDs piped into <see cref="AvatarEntity.OutfitSelections"/>
    /// for brand-new accounts. The wire format is a comma-separated list of
    /// item-instance GUIDs, one per slot in client order: head, torso, legs,
    /// feet, accessory. The client maps each GUID against its baked
    /// AvatarItemDefinition catalog and falls back to the generic
    /// "starter" mesh when a GUID is unknown — which means private servers
    /// don't need a real items DB to dress new players, just stable
    /// placeholders that the client will resolve to the built-in
    /// starter clothing for each slot.
    ///
    /// Empty string ("") = the client renders no clothes / default skin.
    /// We seed all five slots so the avatar boots fully clothed.
    /// </summary>
    /// <summary>Wire format for <see cref="AvatarEntity.OutfitSelections"/>
    /// — a SEMICOLON-separated list of <c>AvatarItemSelection</c>
    /// entries, each entry is COMMA-separated as
    /// <c>{prefabGuid},{maskGuid},{swatchGuid},{decalGuid},{bodyPart}</c>
    /// (exactly 5 parts; the inner <c>AvatarItemVisualData</c> takes
    /// 4 of them, then <c>AvatarItemSelection.ToRecNetString</c>
    /// appends the int <c>BodyPart</c>). Verified at:
    /// <list type="bullet">
    ///   <item>Cpp2IL_ISIL/.../PlayerOutfit.txt:4601 — split delimiter
    ///     literal <c>0x3B</c> = <c>;</c>.</item>
    ///   <item>Cpp2IL_ISIL/.../AvatarItemSelection.txt:887 — exactly
    ///     5 comma-segments expected, anything else throws
    ///     <c>AvatarParseException("Attempting to parse bad Avatar Item
    ///     string …")</c>.</item>
    ///   <item>Cpp2IL_CS/.../Player.cs:262 — BodyPart enum
    ///     {None=-1, Head=0, Torso=1, LeftHand=2, RightHand=3, Mouth=4}.</item>
    ///   <item>Cpp2IL_ISIL/.../OutfitTypeExt.txt:23-39 —
    ///     <c>GetMatchingBodyPart</c>: type ≤ 10 (Hat/Hair/Eye) →
    ///     Head; type == 20 (Mouth/Beard) → Head; 100..104
    ///     (Neck/Shirt/Belt/Pocket/TeamJersey) → Torso; 200..203
    ///     (Wrist/Glove/Watch/TeamWrist) → LeftHand.</item>
    /// </list>
    ///
    /// We leave mask/swatch/decal empty (three empty parts between
    /// commas) so the watch picks the defaults baked into the
    /// AvatarItem prefab. Every prefabGuid below is in
    /// <c>data/avatar_item_lookup.json</c>'s safe set.</summary>
    public const string StarterOutfitSelections =
        "03f8c394-28fa-4087-978b-8d108f0bd969,,,,0" + ";" + // Hat_Angler        slot 0   → Head
        "1d27b674-f9e2-4ffc-9d8c-a58a1be06457,,,,0" + ";" + // Hair_Afro         slot 2   → Head
        "2c3773a5-a0b4-41c5-a4df-6572dff516c9,,,,0" + ";" + // Eye_2020Glasses   slot 10  → Head
        "8d10cc78-6b00-45f3-affb-205e9cc5b03f,,,,0" + ";" + // Beard_Close       slot 20  → Head
        "5b6535f0-86cd-417a-bcce-a1ace9a5f260,,,,1" + ";" + // ArcherQuiver/Neck slot 100 → Torso
        "ec5255d5-edaf-4888-9fe1-e89ac0e0d300,,,,1" + ";" + // AstronautTorso    slot 101 → Torso
        "eda29bce-5a07-4439-a2a7-cafa11e2ed62,,,,1" + ";" + // Belt_PaintballBelt slot 102 → Torso
        "uubY_Cn9A0-Ww4lsIRCF6w,,,,1"               + ";" + // Neck_DraculaMedal slot 103 → Torso
        "74e6878f-94e0-41f2-93ea-de6412238385,,,,2";        // PartyBand/Wrist   slot 200 → LeftHand

    /// <summary>JSON list of GUIDs the player owns at signup. Stored
    /// on <see cref="AvatarEntity.InventoryJson"/> and surfaced via
    /// <c>GET api/avatar/v4/items</c> as the unlocked-items list.
    /// The wardrobe drawer in the watch shows only the player's
    /// inventory; the store's "Already Owned" badge intersects every
    /// tile against this list. So this is intentionally only ~40
    /// items spread across 9 slots — enough that each tab has
    /// variety on first boot, but the rest of the 453-item catalog
    /// stays purchasable in the store.
    ///
    /// Every GUID is in <c>data/avatar_item_lookup.json</c>'s safe
    /// list. Adding GUIDs NOT in that file would crash the watch's
    /// wardrobe coroutine on the first one it can't resolve. To
    /// extend, run <c>tools/AvatarCatalogDumpPatch</c> against an
    /// updated client and pull GUIDs from the dump.</summary>
    public const string StarterInventoryJson = """
        [
          "03f8c394-28fa-4087-978b-8d108f0bd969",
          "AbYdU7O_9U-Lwg7zFVZzXQ",
          "d2d3264b-fbf6-40b5-9f10-c85ec737d112",
          "17aed0a0-70ed-49da-ad09-5bc4746f7718",
          "f218e2de-46fd-45b9-9e0e-670b6bb905e7",
          "7a7d8a06-7982-4bcb-b7bd-4d03b48889b4",
          "1d27b674-f9e2-4ffc-9d8c-a58a1be06457",
          "0088603e-ec3b-4478-8694-e6fb1989b3f2",
          "a6cbfe76-534a-4655-a8a8-3fed13d001c7",
          "b861e5f3-fc6d-43b3-9861-c1b45cb493a8",
          "880a3cc0-7407-4b61-b759-f9dd890fe9e5",
          "e36bcd98-7e85-43fa-89f8-57e4ec33823a",
          "2c3773a5-a0b4-41c5-a4df-6572dff516c9",
          "47629ed1-5378-427c-ad61-4b44d2fe90cc",
          "_qXaNbm4yEeyalo372QbvA",
          "TcgHS_0aPkehrBg7ytMOMQ",
          "8d10cc78-6b00-45f3-affb-205e9cc5b03f",
          "61ba3c90-c81e-4deb-bc79-50c0f1fe3e83",
          "cc96f8a5-bc5b-4f89-83b7-ecd53905ada7",
          "3e139dd9-4757-43a8-8546-8da70dc17f0a",
          "5b6535f0-86cd-417a-bcce-a1ace9a5f260",
          "83be5ba4-525a-4781-a5f6-839c22e4d1d3",
          "c6c08eb5-381a-4193-9722-80da95d62abe",
          "5accb7dc-90a3-46ee-bad2-8fd1861d50f1",
          "ec5255d5-edaf-4888-9fe1-e89ac0e0d300",
          "da55bfc3-47b7-46f6-b548-7fb2c2a5e0bf",
          "2f8518fc-c989-40de-98b4-c6f09b304166",
          "348ac48b-986e-4d42-a0f7-1aea16b88271",
          "843e3364-a743-4dfc-a990-0436e47b8c10",
          "b02053b3-013f-42b0-8881-85f5be2b8cda",
          "eda29bce-5a07-4439-a2a7-cafa11e2ed62",
          "75-HXZBOAEiDBqclnOn8YQ",
          "awOFE0_GSECr8GBNBo6a_w",
          "uubY_Cn9A0-Ww4lsIRCF6w",
          "5e9bbda6-d681-42b1-9cc4-5758acf34bc0",
          "b95678aa-351a-4929-8a0e-0274a183eb18",
          "74e6878f-94e0-41f2-93ea-de6412238385",
          "52VsI_lKlkSUAOJlfDnyrQ",
          "5b01eaa3-0cac-40c0-b72a-b3f6a868ae0c",
          "88739d79-aeb9-471a-b25b-776f46bba9f6"
        ]
        """;

    /// <summary>Color vault GUIDs for the starter avatar. The 2020
    /// client's <c>OutfitManager.FindColorFromVault</c> expects IDs from
    /// its baked skin/hair color vaults here, not packed RGBA strings.</summary>
    public const string StarterHairColor = "81_c6R0my0qK9hYM_0a7LQ";
    public const string StarterSkinColor = "cl2EzJ4v6kW3g4Oo9ZQ3hA";

    /// <summary>Starter wallet for every new account: enough RecCenterTokens
    /// (CurrencyType=2) to buy ~10–20 store items so the store doesn't
    /// look broken on first boot. The watch's storefront UI gates the
    /// purchase confirmation on <c>balance &gt;= price</c>; with a
    /// zero balance every tile shows "Not enough tokens" and the
    /// "Buy" button is disabled, which the user can't tell apart from
    /// a server bug.
    ///
    /// Seeded from <see cref="EnsurePlayerWalletAsync"/> at signup
    /// time and from <see cref="BackfillStarterWalletsAsync"/> on
    /// startup (idempotent, only inserts a row when one doesn't
    /// already exist for the player+currency).</summary>
    public const long StarterRecCenterTokens = 5000;

    /// <summary>The system-account player id. Reserved for the
    /// <c>Coach</c> account that owns every Rec Room Original room
    /// (RecCenter, Paintball, Dodgeball, etc.) — those rows hard-code
    /// <c>CreatorPlayerId = 1</c> in the seed data. Real players are
    /// assigned ids in [100_000, 10_000_000), so id=1 stays uncontested
    /// forever.</summary>
    public const long SystemAccountId = 1;
    public const string SystemAccountUsername = "Coach";

    /// <summary>Ensure the Coach system account exists at id=<see cref="SystemAccountId"/>.
    /// Idempotent — repeated calls no-op once the row is in place. Called
    /// from <c>Program.cs</c> at startup BEFORE <see cref="RoomService.SeedAsync"/>
    /// because the RR-Original room seeder sets <c>CreatorPlayerId = 1</c>
    /// and we want that to be a real FK target rather than a dangling
    /// sentinel. The migrator already accepts pre-existing sentinels, so
    /// existing migrated databases will heal on first boot.
    ///
    /// Coach is non-loginable: <c>DeviceId = "system"</c> never matches any
    /// real client deviceId hash (those are 40-char hex), <c>PasswordHash</c>
    /// is null so the password grant rejects, <c>BannedUntil</c> is set far
    /// in the future as belt-and-suspenders so even a JWT minted somehow
    /// can't be used to act as Coach. The owner-of-record on system rooms
    /// gets to be a real account so admin tools' "Find rooms by player"
    /// queries don't have to special-case id=1.</summary>
    public async Task EnsureSystemAccountAsync()
    {
        var existing = await db.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == SystemAccountId);
        if (existing is not null)
        {
            // Self-heal: if the row exists but the bookkeeping fields
            // drift (e.g. someone manually edited Username), restore them
            // so admin tools always show "Coach" as the owner.
            if (existing.Username != SystemAccountUsername || existing.DisplayName != SystemAccountUsername)
            {
                await db.Players
                    .Where(p => p.Id == SystemAccountId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Username, SystemAccountUsername)
                        .SetProperty(p => p.DisplayName, SystemAccountUsername));
            }
            return;
        }

        var coach = new PlayerEntity
        {
            Id = SystemAccountId,
            DeviceId = "system",
            CreatedFromDeviceId = "system",
            LastPlatform = 0,
            LastPlatformId = string.Empty,
            Username = SystemAccountUsername,
            DisplayName = SystemAccountUsername,
            Bio = "Rec Room Original room owner. System account — not a real player.",
            Level = 99,
            XP = 0,
            IsAdmin = false,
            IsDeveloper = false,
            IsVerified = true,
            CanReceiveInvites = false,
            // Far-future ban as a hard authorization backstop in case a
            // forged JWT ever names player 1 as the subject. BanCheckMiddleware
            // refuses any authenticated request when BannedUntil > now.
            BannedUntil = new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            PasswordHash = null,
            CreatedAt = new DateTime(2020, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            LastSeenAt = new DateTime(2020, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            Avatar = new AvatarEntity
            {
                OutfitSelections = StarterOutfitSelections,
                InventoryJson = StarterInventoryJson,
                HairColor = StarterHairColor,
                SkinColor = StarterSkinColor,
                UpdatedAt = new DateTime(2020, 3, 6, 0, 0, 0, DateTimeKind.Utc),
            },
        };
        db.Players.Add(coach);
        await db.SaveChangesAsync();

        // Postgres bigserial doesn't auto-bump its sequence when we
        // insert an explicit Id, so the next AddAsync(player) would try
        // to assign Id=1 and collide. Bring the sequence in line with
        // MAX(Id). SQLite handles this implicitly because it reads the
        // current max on each INSERT; only Postgres needs the nudge.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                SELECT setval(
                    pg_get_serial_sequence('"Players"', 'Id'),
                    GREATEST((SELECT COALESCE(MAX("Id"), 0) FROM "Players"), 1))
                """);
        }

        logger.LogInformation(
            "[seed-system] Created Coach system account at id={Id}", SystemAccountId);
    }

    /// <summary>Insert a starter <see cref="CurrencyBalanceEntity"/>
    /// row for the player if they don't already have one for
    /// <c>CurrencyType=2</c> (RecCenterTokens). Idempotent: existing
    /// balances are left untouched (so a player who has already spent
    /// down isn't mysteriously refilled). Called from both new-account
    /// flows so a brand-new account boots into a non-zero wallet, and
    /// from <see cref="BackfillStarterWalletsAsync"/> at startup so
    /// pre-existing accounts (created before this seed shipped) get
    /// the same one-time grant.</summary>
    private async Task EnsurePlayerWalletAsync(long playerId)
    {
        var exists = await db.CurrencyBalances.AsNoTracking()
            .AnyAsync(c => c.PlayerId == playerId && c.CurrencyType == 2);
        if (exists) return;
        db.CurrencyBalances.Add(new CurrencyBalanceEntity
        {
            PlayerId = playerId,
            CurrencyType = 2,
            Balance = StarterRecCenterTokens,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>One-time backfill at startup: every player who's
    /// missing a CurrencyType=2 row gets the starter grant. Players
    /// who already have a row keep it (whatever balance, even 0 — they
    /// might have spent it, and silently topping them back up would
    /// be exploitable). New rows are inserted with a single bulk
    /// query.
    ///
    /// Why a startup backfill instead of an EF migration: same
    /// reasoning as <see cref="EnsureAvatarSeedAsync"/> — keeps the
    /// starter value in C# without forcing every prod DB to re-run a
    /// one-off SQL UPDATE. Self-heals if the seed value evolves.</summary>
    public async Task BackfillStarterWalletsAsync()
    {
        // Find players who DON'T have a row for currency 2 yet.
        var missing = await db.Players.AsNoTracking()
            .Where(p => p.Id != SystemAccountId)
            .Where(p => !db.CurrencyBalances.Any(c =>
                c.PlayerId == p.Id && c.CurrencyType == 2))
            .Select(p => p.Id)
            .ToListAsync();

        if (missing.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var pid in missing)
        {
            db.CurrencyBalances.Add(new CurrencyBalanceEntity
            {
                PlayerId = pid,
                CurrencyType = 2,
                Balance = StarterRecCenterTokens,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[seed-wallet] granted {Count} players the starter {Tokens}-token balance",
            missing.Count, StarterRecCenterTokens);
    }

    /// <summary>
    /// Backfill <see cref="AvatarEntity.InventoryJson"/>,
    /// <see cref="AvatarEntity.OutfitSelections"/>, and color vault IDs for accounts that
    /// were created BEFORE the new starter set landed (e.g. when the
    /// columns shipped, when the v1 5-mask-GUID seed shipped, or any
    /// avatar row with default-empty JSON). Idempotent — runs every
    /// startup, only touches rows that currently hold a value we know
    /// is broken or empty.
    ///
    /// Why a startup-time backfill instead of an EF migration: the
    /// migrator would force every prod database to re-run a one-off
    /// data UPDATE, and we'd have to keep the SQL identical to the
    /// in-code starter strings. Running it at boot lets us evolve
    /// the starter values in C# without a migration churn cycle, and
    /// self-heals any AvatarEntity row that drifted out of sync (e.g.
    /// from a partial migration, a hand-edit, or a now-removed
    /// signup path that didn't seed the avatar).
    ///
    /// CALLED FROM <c>Program.cs</c> startup AFTER
    /// <see cref="EnsureSystemAccountAsync"/>.
    /// </summary>
    public async Task EnsureAvatarSeedAsync()
    {
        // The earliest seed value for InventoryJson — five mask/swatch
        // asset GUIDs that aren't in the runtime config's
        // avatarItemDataLookup, so the watch's wardrobe parser would
        // throw "Missing data for guid …" on the first one. Any
        // AvatarEntity still holding this gets backfilled to the
        // current StarterInventoryJson.
        const string OldBrokenInventory = """
            [
              "0dd6596c-b4ea-4d55-a219-e1f44e4a376d",
              "941c046e-4e95-49f8-a7d7-19071fcc3c94",
              "b75ef67d-00c3-4ac1-9b72-212032460294",
              "e17481fc-b2b6-47a5-91e5-7205f45a3247",
              "48abd952-214f-48b2-a8f1-1146f6f69aa2"
            ]
            """;
        // Normalise the JSON strings before comparing — DB-stored
        // JSON loses leading whitespace, so we compare without it.
        var oldInvCompact = System.Text.RegularExpressions.Regex.Replace(
            OldBrokenInventory, @"\s+", "");
        var newInvCompact = System.Text.RegularExpressions.Regex.Replace(
            StarterInventoryJson, @"\s+", "");

        // Detection: a valid OutfitSelections is a SEMICOLON-separated
        // list of <c>{guid},{m},{s},{d},{bp}</c> tuples (verified at
        // PlayerOutfit.txt:4601). The 5-mask-GUID seed (one comma list,
        // no semicolons) and the no-bodyPart seed (also one comma list,
        // no semicolons) both lack the semicolon delimiter — that's a
        // sufficient discriminator. Anything with a semicolon was
        // already produced by the new code path and we leave it alone.
        // Empty / missing values also need a backfill.
        //
        // The semicolon test uses <c>Contains(";")</c> (string overload,
        // which EF Core translates to a Postgres LIKE) NOT
        // <c>Contains(';')</c> (char overload, which EF can't translate
        // and previously crashed startup with InvalidOperationException
        // "The LINQ expression … could not be translated").
        var rows = await db.Avatars
            .Where(a =>
                string.IsNullOrEmpty(a.OutfitSelections) ||
                !a.OutfitSelections.Contains(";") ||
                string.IsNullOrEmpty(a.InventoryJson) ||
                a.InventoryJson == "[]" ||
                a.InventoryJson == OldBrokenInventory ||
                a.InventoryJson == oldInvCompact ||
                string.IsNullOrEmpty(a.HairColor) ||
                a.HairColor.Contains(",") ||
                string.IsNullOrEmpty(a.SkinColor) ||
                a.SkinColor.Contains(","))
            .ToListAsync();

        if (rows.Count == 0) return;

        var now = DateTime.UtcNow;
        int outfitsFixed = 0, inventoryFixed = 0, colorsFixed = 0;
        foreach (var a in rows)
        {
            if (string.IsNullOrEmpty(a.OutfitSelections) || !a.OutfitSelections.Contains(";"))
            {
                a.OutfitSelections = StarterOutfitSelections;
                outfitsFixed++;
            }
            var invCompact = string.IsNullOrEmpty(a.InventoryJson)
                ? string.Empty
                : System.Text.RegularExpressions.Regex.Replace(a.InventoryJson, @"\s+", "");
            if (invCompact == string.Empty || invCompact == "[]" || invCompact == oldInvCompact)
            {
                a.InventoryJson = newInvCompact;
                inventoryFixed++;
            }
            if (string.IsNullOrEmpty(a.HairColor) || a.HairColor.Contains(','))
            {
                a.HairColor = StarterHairColor;
                colorsFixed++;
            }
            if (string.IsNullOrEmpty(a.SkinColor) || a.SkinColor.Contains(','))
            {
                a.SkinColor = StarterSkinColor;
                colorsFixed++;
            }
            a.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[seed-avatar] backfilled {OutfitCount} OutfitSelections + {InvCount} InventoryJson rows + {ColorCount} color fields",
            outfitsFixed, inventoryFixed, colorsFixed);
    }

    /// <summary>
    /// Look up an existing account by deviceId, or create a new one if no
    /// account is bound to that device yet. This is the canonical
    /// account-creation entry point — every login flow funnels through here.
    ///
    /// Why deviceId and not platformId/Steam-ID:
    ///   - The client sends both fields in the platformlogin form body. We
    ///     deliberately ignore platformId (Steam 64) so accounts are NOT
    ///     keyed to a Steam identity. A user running the same patched client
    ///     on the same machine gets the same persistent account no matter
    ///     what Steam emulator (Goldberg, real Steam, none) is in front.
    ///   - deviceId is a stable per-installation hash the client generates
    ///     itself. Different machines → different accounts → multiplayer
    ///     works without Steam.
    ///   - When deviceId is empty (rare paths), fall back to a generated
    ///     synthetic id so we never create accidentally-shared accounts.
    /// </summary>
    /// <summary>Lookup-only counterpart to
    /// <see cref="GetOrCreateByDeviceAsync"/>. Returns the existing player
    /// row for the given deviceId, or null if none exists — never creates
    /// a new account. Callers that want to enforce the admin "signups
    /// disabled" toggle use this to gate the device-id login fallback
    /// without accidentally minting an account.</summary>
    public async Task<PlayerEntity?> GetByDeviceAsync(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        return await db.Players
            .Include(p => p.Avatar)
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId);
    }

    public async Task<PlayerEntity> GetOrCreateByDeviceAsync(
        string? deviceId,
        int platform = 0,
        string? platformId = null,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            // Synthesize a stable id from the platformId or generate one so
            // we don't share an account across unrelated callers.
            deviceId = !string.IsNullOrWhiteSpace(platformId)
                ? $"synthetic-{platformId}"
                : $"synthetic-{Guid.NewGuid():N}";
        }

        var existing = await db.Players
            .Include(p => p.Avatar)
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId);

        if (existing is not null)
        {
            existing.LastSeenAt = DateTime.UtcNow;
            existing.LastPlatform = platform;
            if (!string.IsNullOrEmpty(platformId)) existing.LastPlatformId = platformId;
            await db.SaveChangesAsync();
            return existing;
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? GenerateUsername()
            : await EnsureUniqueUsernameAsync(displayName);

        // First-account-on-fresh-DB gets admin so there's always a root
        // admin to bootstrap moderation. Cheap query — Players is keyed by
        // a small int and `Any()` on an empty table is O(1).
        var isFirstAccount = !await db.Players.AsNoTracking().AnyAsync();

        var player = new PlayerEntity
        {
            Id = RandomNumberGenerator.GetInt32(100_000, 9_999_999),
            DeviceId = deviceId,
            // Audit trail — pinned to the creating deviceId and never
            // reassigned, even if `DeviceId` later rotates.
            CreatedFromDeviceId = deviceId,
            LastPlatform = platform,
            LastPlatformId = platformId ?? string.Empty,
            Username = name,
            DisplayName = name,
            IsAdmin = isFirstAccount,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        };
        player.Avatar = new AvatarEntity
        {
            PlayerId = player.Id,
            // Pre-populate so a brand-new account spawns dressed in the
            // canonical 2020 starter outfit rather than the no-clothes
            // T-pose the client renders for empty OutfitSelections.
            OutfitSelections = StarterOutfitSelections,
            InventoryJson = StarterInventoryJson,
            HairColor = StarterHairColor,
            SkinColor = StarterSkinColor,
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        // Every new player gets their own dorm row. Lazy-creates here
        // (and at first /goto/room/DormRoom for legacy accounts that
        // pre-date this code).
        await rooms.EnsurePersonalDormAsync(player.Id);

        // Seed a starter token balance so the new account isn't
        // staring at "Not enough tokens" on every store tile.
        await EnsurePlayerWalletAsync(player.Id);

        if (isFirstAccount)
        {
            logger.LogInformation(
                "[seed-admin] First account on this DB → {Username} (id {PlayerId}, deviceId {DeviceId}) granted IsAdmin=true",
                name, player.Id, deviceId);
        }
        return player;
    }

    /// <summary>
    /// Always creates a new account row, regardless of whether one
    /// already exists for the given <paramref name="deviceId"/>. Used
    /// by <c>account/create</c> so the watch's "Create new account"
    /// selector option actually creates new — the older flow that
    /// returned the existing account by deviceId blocked multi-account-
    /// per-device entirely.
    ///
    /// Boot-flow orphans (the watch always probes account/create on
    /// every boot, even when the user is picking a cached account) are
    /// cleaned up by <see cref="OrphanAccountTracker"/> + the
    /// cached_login handler's "if you logged in to a different id, the
    /// just-created one was an orphan" heuristic.
    /// </summary>
    public async Task<PlayerEntity> CreateNewAccountAsync(
        string? deviceId,
        int platform = 0,
        string? platformId = null,
        string? displayName = null)
    {
        // Synthesise deviceId / platformId for empty inputs so the
        // audit trail still lands somewhere meaningful.
        var effectiveDeviceId = string.IsNullOrWhiteSpace(deviceId)
            ? $"synthetic-{Guid.NewGuid():N}"
            : deviceId;

        var name = string.IsNullOrWhiteSpace(displayName)
            ? GenerateUsername()
            : await EnsureUniqueUsernameAsync(displayName);

        // First-account-on-fresh-DB still gets admin — same logic as
        // GetOrCreateByDeviceAsync, but we honour it here too in case
        // the DB is fresh and the very first call is an explicit
        // create.
        var isFirstAccount = !await db.Players.AsNoTracking().AnyAsync();

        var player = new PlayerEntity
        {
            Id = RandomNumberGenerator.GetInt32(100_000, 9_999_999),
            DeviceId = effectiveDeviceId,
            CreatedFromDeviceId = effectiveDeviceId,
            LastPlatform = platform,
            LastPlatformId = platformId ?? string.Empty,
            Username = name,
            DisplayName = name,
            IsAdmin = isFirstAccount,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        };
        player.Avatar = new AvatarEntity
        {
            PlayerId = player.Id,
            OutfitSelections = StarterOutfitSelections,
            InventoryJson = StarterInventoryJson,
            HairColor = StarterHairColor,
            SkinColor = StarterSkinColor,
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        await rooms.EnsurePersonalDormAsync(player.Id);
        await EnsurePlayerWalletAsync(player.Id);

        if (isFirstAccount)
        {
            logger.LogInformation(
                "[seed-admin] First account on this DB → {Username} (id {PlayerId}) granted IsAdmin=true",
                name, player.Id);
        }
        return player;
    }

    /// <summary>Hard-delete a freshly-created orphan account row. Only
    /// safe to call on accounts that have NEVER been logged into via
    /// JWT-issuing flows — the orphan tracker enforces that
    /// constraint by only flagging accounts created within the same
    /// session that ended up logging into a different id. Cascades
    /// the avatar row.</summary>
    public async Task<bool> DeleteOrphanAsync(long accountId)
    {
        var player = await db.Players
            .Include(p => p.Avatar)
            .FirstOrDefaultAsync(p => p.Id == accountId);
        if (player is null) return false;
        if (player.Avatar is not null) db.Avatars.Remove(player.Avatar);
        db.Players.Remove(player);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<PlayerEntity?> GetByIdAsync(long id) =>
        await db.Players.Include(p => p.Avatar).FirstOrDefaultAsync(p => p.Id == id);

    /// <summary>
    /// Stamp the calling platform onto an existing account so the
    /// next boot's <c>/cachedlogin/forplatformid</c> query finds it.
    /// Called from every successful login path (password, cached_login,
    /// refresh) — without this, a manual username/password sign-in
    /// would never tag the account with the current Steam platformId
    /// and the watch's "remembered accounts" prompt would stay empty.
    /// </summary>
    public async Task TagPlatformAsync(long playerId, int platform, string? platformId)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player is null) return;
        player.LastSeenAt = DateTime.UtcNow;
        if (platform != 0) player.LastPlatform = platform;
        if (!string.IsNullOrWhiteSpace(platformId)) player.LastPlatformId = platformId;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns every account that has ever logged in with the given platform
    /// + platformId combination, most-recent-first. Backs the
    /// <c>auth.rec.net/cachedlogin/forplatformid/{platform}/{platformId}</c>
    /// "remembered logins" prompt the client shows on its account-selection
    /// screen.
    ///
    /// On a private server with a single Steam emulator pointed at one
    /// account per machine, this typically returns 0 or 1 row. Multi-account
    /// developers (or anyone testing the multi-account flow) get a real list.
    /// </summary>
    public async Task<List<PlayerEntity>> GetCachedLoginsAsync(int platform, string? platformId, int take = 10)
    {
        if (string.IsNullOrWhiteSpace(platformId))
            return [];
        return await db.Players
            .Where(p => p.LastPlatform == platform && p.LastPlatformId == platformId)
            .OrderByDescending(p => p.LastSeenAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<PlayerEntity?> GetByUsernameAsync(string username) =>
        await db.Players.FirstOrDefaultAsync(p => p.Username == username);

    public async Task<List<PlayerEntity>> SearchAsync(string query, int take = 20)
    {
        // Clamp the page size — without this a malicious caller could
        // request `take=1000000` and exhaust the connection. 50 is enough
        // for the watch's typeahead UI (it shows ~10 rows at a time).
        var clamped = Math.Clamp(take, 1, 50);
        return await db.Players
            .Where(p => p.Username.Contains(query) || p.DisplayName.Contains(query))
            .Take(clamped)
            .ToListAsync();
    }

    /// <summary>
    /// Update both Username and DisplayName. The client treats display name
    /// changes as username changes for older builds; we keep them in sync
    /// unless the caller is explicitly editing one or the other.
    /// </summary>
    public async Task<bool> UpdateUsernameAsync(long id, string username)
    {
        var player = await db.Players.FindAsync(id);
        if (player is null) return false;
        var unique = await EnsureUniqueUsernameAsync(username, exceptId: id);
        player.Username = unique;
        player.DisplayName = unique;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateDisplayNameAsync(long id, string displayName)
    {
        var player = await db.Players.FindAsync(id);
        if (player is null) return false;
        player.DisplayName = displayName;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateBioAsync(long id, string bio)
    {
        var player = await db.Players.FindAsync(id);
        if (player is null) return false;
        player.Bio = bio;
        await db.SaveChangesAsync();
        return true;
    }

    public static PlayerProfile ToProfile(PlayerEntity e) => new()
    {
        AccountId = e.Id,
        Username = e.Username,
        DisplayName = e.DisplayName,
        Bio = e.Bio,
        Level = e.Level,
        XP = e.XP,
        Reputation = e.Reputation,
        IsVerified = e.IsVerified,
        IsDeveloper = e.IsDeveloper,
        IsCommunityTeam = e.IsCommunityTeam,
        CanReceiveInvites = e.CanReceiveInvites,
        IsJunior = e.IsJunior,
        ProfileImage = e.ProfileImageName,
        CreatedAt = e.CreatedAt,
    };

    private static string GenerateUsername() =>
        $"Player_{RandomNumberGenerator.GetInt32(1000, 99999)}";

    /// <summary>
    /// Append a numeric suffix until the username is unique. Skips the
    /// suffix entirely if the requested name is already free.
    /// </summary>
    private async Task<string> EnsureUniqueUsernameAsync(string requested, long? exceptId = null)
    {
        var name = requested.Trim();
        if (name.Length == 0) name = GenerateUsername();
        var taken = await db.Players.AnyAsync(p => p.Username == name && p.Id != exceptId);
        if (!taken) return name;

        for (var i = 1; i < 1000; i++)
        {
            var candidate = $"{name}_{i}";
            if (!await db.Players.AnyAsync(p => p.Username == candidate && p.Id != exceptId))
                return candidate;
        }
        return $"{name}_{RandomNumberGenerator.GetInt32(1000, 99999)}";
    }
}
