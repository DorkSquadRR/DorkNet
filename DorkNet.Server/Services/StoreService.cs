using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DorkNet.Server.Controllers.API.Avatar.V4;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Owns the in-game store catalog, the storefronts API shaping, and
/// the purchase flow (verify balance → deduct currency → grant item to
/// inventory). The watch's "Shop" tab fetches this via
/// <c>GET api/storefronts/v3/all</c> and friends; the purchase flow
/// runs through <c>POST api/purchase/v1/{initiate|process|complete}</c>.
///
/// Currency: items are priced in tokens (CurrencyType=2), the same
/// currency the level-up + cheer-receive flow grants. Real-money
/// purchases (CurrencyType=1) are intentionally not wired up — this is
/// a private server.
/// </summary>
public class StoreService(DorkNetDbContext db, LevelService level, IConfiguration config)
{
    public const long StoreItemPrice = 1;
    public const string AllStorefrontKey = "all";
    public const string RroStorefrontKey = "rro";
    private const int StoreAvatarItemTypeOutfit = 0;
    private const int StoreAvatarItemTypeHairDye = 1;
    private const int GiftBoxContentHairDye = 6;
    private const string WardrobePrefix = "wardrobe-";
    public const string EquipmentSkinPrefix = "equipment-skin:";

    public sealed record StorefrontDefinition(
        string Key,
        int? StorefrontType,
        string DisplayName,
        string Scope);

    private static readonly StorefrontDefinition[] StorefrontDefinitions =
    {
        new("main", null, "Main watch catalog", "watch"),
        new("watch", 3, "Watch gift-drop shelf", "watch"),
        new(AllStorefrontKey, null, "All storefront shelves", "shared"),
        new(RroStorefrontKey, null, "All RRO and Rec Center shelves", "shared"),
        new("season:1", null, "Season 1", "season"),
        new("giftdrop:1", 1, "Laser Tag", "room"),
        new("giftdrop:2", 2, "Rec Center", "room"),
        new("giftdrop:100", 100, "Quest - Lost Skulls", "room"),
        new("giftdrop:101", 101, "Quest - Dracula", "room"),
        new("giftdrop:102", 102, "Quest - Golden Trophy", "room"),
        new("giftdrop:103", 103, "Quest - Crimson Cauldron", "room"),
        new("giftdrop:200", 200, "Rec Royale", "room"),
        new("giftdrop:300", 300, "Cafe", "room"),
        new("giftdrop:400", 400, "Paintball", "room"),
        new("giftdrop:401", 401, "Paintball - River", "room"),
        new("giftdrop:402", 402, "Paintball - Homestead", "room"),
        new("giftdrop:403", 403, "Paintball - Quarry", "room"),
        new("giftdrop:404", 404, "Paintball - Clear Cut", "room"),
        new("giftdrop:405", 405, "Paintball - Spillway", "room"),
        new("giftdrop:406", 406, "Paintball - Sunset Drive-In", "room"),
        new("giftdrop:500", 500, "Bowling", "room"),
        new("giftdrop:600", 600, "Stunt Runner", "room"),
        new("giftdrop:700", 700, "Dorm Mirror", "room"),
    };

    public static IReadOnlyList<StorefrontDefinition> GetStorefrontDefinitions() =>
        StorefrontDefinitions;

    public static string StorefrontKeyForType(int storefrontType) =>
        storefrontType == 3 ? "watch" : $"giftdrop:{storefrontType}";

    private static readonly (string Slug, string Name, string ConsumableDesc, string HairColorGuid)[] PermanentHairDyes =
    {
        ("hairdye-red", "Permanent KREQ Red Hair Dye", "[hairdyepotionconsumable_red]", "xyHGGFKNWEOTnXloDFahPQ"),
        ("hairdye-dark-red", "Permanent Cauldron Crimson Hair Dye", "[hairdyepotionconsumable_darkred]", "XCwNFMT1i0i9Q9gJYTLPUQ"),
        ("hairdye-orange", "Permanent Bucket Orange Hair Dye", "[hairdyepotionconsumable_orange]", "1I8AD333gku-iOQlS6WWlw"),
        ("hairdye-yellow", "Permanent Pirate Gold Hair Dye", "[hairdyepotionconsumable_yellow]", "pQNfh-3DsEGWfiIls6Qf6g"),
        ("hairdye-green", "Permanent Goblin Green Hair Dye", "[hairdyepotionconsumable_green]", "TylV8M6zjUaadhxHPWAS-w"),
        ("hairdye-teal", "Permanent EDM Emerald Hair Dye", "[hairdyepotionconsumable_teal]", "wXJZbYCkZE-NMbPVf2YuFA"),
        ("hairdye-cyan", "Permanent H2Oasis Blue Hair Dye", "[hairdyepotionconsumable_cyan]", "Bjsgo9ddeUCk3zW1-DdJbw"),
        ("hairdye-blue", "Permanent Butterfly Blue Hair Dye", "[hairdyepotionconsumable_blue]", "yTdx1nVVtECU8Vh7EC8dGA"),
        ("hairdye-dark-blue", "Permanent Ranger Roy-Al Blue Hair Dye", "[hairdyepotionconsumable_darkblue]", "3ksu_FLaukeJ-YE90jb4Ow"),
        ("hairdye-purple", "Permanent Propulsion Purple Hair Dye", "[hairdyepotionconsumable_purple]", "53y1prYATU2drZDS-jpaqg"),
        ("hairdye-dark-purple", "Permanent Gizmo Grape Hair Dye", "[hairdyepotionconsumable_darkpurple]", "QmgjYhH_L0q58eF5SWeDhQ"),
        ("hairdye-pink", "Permanent Mousebot Magenta Hair Dye", "[hairdyepotionconsumable_pink]", "8k_gZA_lrEyJxI19GrcAZg"),
        ("hairdye-dark-pink", "Permanent Recreational Raspberry Hair Dye", "[hairdyepotionconsumable_darkpink]", "yejur5BbR0mevbH-rzgXYA"),
        ("hairdye-pastel-red", "Permanent Rebel Rose Hair Dye", "[hairdyepotionconsumable_pastelred]", "aziIarpSREmcT6vihduPwA"),
        ("hairdye-pastel-orange", "Permanent PVPeach Hair Dye", "[hairdyepotionconsumable_pastelorange]", "0RUmjv2bL0ydbKU61dlqlg"),
        ("hairdye-pastel-yellow", "Permanent Standee Sandy Hair Dye", "[hairdyepotionconsumable_pastelyellow]", "H65ddsxAY0m2sccMrF48Aw"),
        ("hairdye-pastel-green", "Permanent Frontier Forest Hair Dye", "[hairdyepotionconsumable_pastelgreen]", "LBz3a9520kiOOMMOwsTExg"),
        ("hairdye-pastel-teal", "Permanent Jungle Jade Hair Dye", "[hairdyepotionconsumable_pastelteal]", "Sc8S3QiTIUuXNJZD6geY1Q"),
        ("hairdye-pastel-cyan", "Permanent Circuit Cerulean Hair Dye", "[hairdyepotionconsumable_pastelcyan]", "W5E2gDCKY0SXaKytP89-OA"),
        ("hairdye-pastel-blue", "Permanent Paula's Periwinkle Hair Dye", "[hairdyepotionconsumable_pastelblue]", "_2QGtfHzuUSq0eyI84rImw"),
        ("hairdye-pastel-purple", "Permanent Lounge Lavender Hair Dye", "[hairdyepotionconsumable_pastelpurple]", "qp_vRXZQPU2rlx_IU1Vr8Q"),
        ("hairdye-pastel-pink", "Permanent Maker Mauve Hair Dye", "[hairdyepotionconsumable_pastelpink]", "G129V_kVXkSLN2OG8EjSqQ"),
    };

    /// <summary>Every permanent hair-dye store slug — used by the admin
    /// "unlock all items" action to drop the whole hair-dye set into a
    /// player's <see cref="PlayerInventoryEntity"/> (hair dyes are owned as
    /// consumable slugs, not avatar GUIDs).</summary>
    public static IEnumerable<string> AllHairDyeSlugs =>
        PermanentHairDyes.Select(d => d.Slug);

    private bool EnableWatchGiftDrops =>
        config.GetValue("Store:EnableWatchGiftDrops", true);

    private int MaxWatchGiftDrops =>
        // 0 means no cap. Keep the switch configurable in case a
        // deployment needs to temporarily reduce the shelf size while
        // debugging a bad catalog row.
        config.GetValue("Store:MaxWatchGiftDrops", 0);

    /// <summary>Idempotent seed of a starter catalog so the store is
    /// non-empty on first boot. Only inserts items whose Slug isn't
    /// already in the DB; safe to call repeatedly.</summary>
    public async Task SeedAsync()
    {
        var defaults = new (string Slug, string Name, string Desc, string Category, long Price, string Image, string Storefront)[]
        {
            // Hats / heads
            ("dorknet-cap-classic",   "Classic Cap",         "A simple cap. Comfortable, unremarkable.",                  "head",       150,  "store_classic_cap.png", "main"),
            ("dorknet-bowler-hat",    "Bowler Hat",          "Tipped at a jaunty angle. Goes with everything.",           "head",       400,  "store_bowler_hat.png",  "main"),
            ("dorknet-witch-hat",     "Witch Hat",           "Pointy. Definitely magical.",                               "head",       650,  "store_witch_hat.png",   "main"),
            // Torso
            ("dorknet-tee-server",    "Server Crew Tee",     "Sworn to defend the JWT secret.",                           "torso",      300,  "store_tee_server.png",  "main"),
            ("dorknet-hoodie-cyber",  "Cyber Hoodie",        "Glowing accents. Optimised for late-night coding.",         "torso",      900,  "store_hoodie_cyber.png","main"),
            ("dorknet-jacket-bomber", "Bomber Jacket",       "Sleek, bomber-style — for laser tag and lurking.",          "torso",      850,  "store_jacket_bomber.png","main"),
            // Legs
            ("dorknet-jeans-classic", "Blue Jeans",          "Standard issue. Goes with anything.",                        "legs",       200,  "store_jeans.png",       "main"),
            ("dorknet-cargo-pants",   "Cargo Pants",         "Lots of pockets — for inventions, snacks, secrets.",         "legs",       450,  "store_cargo_pants.png", "main"),
            // Feet
            ("dorknet-sneakers",      "Sneakers",            "Soft soles. Quiet on the dorm floor.",                       "feet",       250,  "store_sneakers.png",    "main"),
            ("dorknet-boots-combat",  "Combat Boots",        "Polished. Heavy. Great for stomping.",                       "feet",       550,  "store_boots_combat.png","main"),
            // Accessories
            ("dorknet-shades",        "Sunglasses",          "Maximum chill. Slightly inappropriate indoors.",             "accessory",  300,  "store_shades.png",      "main"),
            ("dorknet-watch-rgb",     "RGB Smartwatch",      "Glowing wrist accessory. Cycles through every colour.",      "accessory",  700,  "store_watch_rgb.png",   "main"),
            ("dorknet-cape-velvet",   "Velvet Cape",         "Floor-length. Suitable for entering a room dramatically.",   "accessory",  1200, "store_cape_velvet.png", "main"),
            // Hair
            ("dorknet-hair-mohawk",   "Mohawk",              "Sharp. Loud. Possibly aerodynamic.",                         "hair",       800,  "store_hair_mohawk.png", "main"),
            ("dorknet-hair-flowing",  "Flowing Locks",       "Long. Glossy. Definitely waving in some imaginary breeze.",   "hair",       650,  "store_hair_flowing.png","main"),
            // Face
            ("dorknet-face-grin",     "Permanent Grin",      "A confident, slightly unsettling smile.",                    "face",       350,  "store_face_grin.png",   "main"),
            ("dorknet-face-monocle",  "Monocle & Mustache",  "Distinguished. Possibly inherited.",                         "face",       550,  "store_face_monocle.png","main"),
            // Consumables
            ("dorknet-firework",      "Firework",            "One-shot launch. Spectacular. Not edible.",                  "consumable", 50,   "store_firework.png",    "main"),
            ("dorknet-pizza-slice",   "Pizza Slice",         "Looks great on the watch. Also restores 5 imaginary HP.",    "consumable", 25,   "store_pizza.png",       "main"),
            // Room templates
            ("dorknet-template-cozy", "Cozy Cabin Template", "A wood-cabin starter for new rooms — fireplace included.",   "roomtemplate", 1500, "store_template_cozy.png", "main"),
            // Limited-time items in the gift drop storefront
            ("dorknet-gift-mystery1", "Mystery Box (Tier 1)","Contains a randomised cosmetic. Open with caution.",         "consumable", 100,  "store_mystery_t1.png",  "giftdrop:1"),
            ("dorknet-gift-mystery2", "Mystery Box (Tier 2)","A heftier mystery box. More chance of rare drops.",          "consumable", 500,  "store_mystery_t2.png",  "giftdrop:1"),
            ("dorknet-gift-aura",     "Golden Aura",         "Limited-time aura cosmetic — radiates while you're nearby.","accessory",  2500, "store_gift_aura.png",   "giftdrop:2"),
        };

        var existing = await db.StoreItems
            .Select(i => i.Slug)
            .ToListAsync();
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        // Drop stale wardrobe-* rows whose embedded GUID isn't in the
        // current avatar_item_lookup.json safelist. The previous seed
        // pulled GUIDs from outfits_assets_*.bundle MonoBehaviours
        // (mask/swatch ASSET GUIDs, not AvatarItem GUIDs); those tiles
        // can't render their 3D preview because the watch's
        // avatarItemPrefabLookup doesn't have those keys. Removing
        // them prevents broken tiles from cluttering the store.
        var safe = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.SafeGuids;
        if (safe.Count > 0)
        {
            // Skip wardrobe-colored-* — those resolve through the color
            // variant catalog, not the safe-GUID set, and the substring
            // after "wardrobe-" is never a bare GUID anyway.
            var stale = await db.StoreItems
                .Where(i => i.Slug.StartsWith("wardrobe-")
                            && !i.Slug.StartsWith(WardrobeColoredPrefix))
                .Select(i => new { i.Id, i.Slug })
                .ToListAsync();
            var toDelete = new List<long>();
            foreach (var row in stale)
            {
                var guid = row.Slug.Substring("wardrobe-".Length);
                if (!safe.Contains(guid)) toDelete.Add(row.Id);
            }
            if (toDelete.Count > 0)
            {
                await db.StoreItems
                    .Where(i => toDelete.Contains(i.Id))
                    .ExecuteDeleteAsync();
                // Re-pull the existing-slug set so the new seed below
                // re-inserts with the correct GUIDs.
                existing = await db.StoreItems.Select(i => i.Slug).ToListAsync();
                existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            }
        }

        // Drop orphan wardrobe-colored-* rows whose slug isn't in the
        // current color-variant catalog. When we remove an entry from
        // data/color_variants.json (e.g. because the asset's swatch
        // colors look wrong in-game), the corresponding StoreItems row
        // would otherwise linger as a broken tile.
        if (_variantCatalog.Value.Count > 0)
        {
            var validColored = new HashSet<string>(_variantCatalog.Value.Keys,
                StringComparer.OrdinalIgnoreCase);
            var orphanColored = await db.StoreItems
                .Where(i => i.Slug.StartsWith(WardrobeColoredPrefix))
                .Select(i => new { i.Id, i.Slug })
                .ToListAsync();
            var coloredToDelete = orphanColored
                .Where(r => !validColored.Contains(r.Slug))
                .Select(r => r.Id)
                .ToList();
            if (coloredToDelete.Count > 0)
            {
                await db.StoreItems
                    .Where(i => coloredToDelete.Contains(i.Id))
                    .ExecuteDeleteAsync();
                existing = await db.StoreItems.Select(i => i.Slug).ToListAsync();
                existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            }
        }

        var added = false;
        foreach (var entry in defaults)
        {
            if (existingSet.Contains(entry.Slug)) continue;
            db.StoreItems.Add(new StoreItemEntity
            {
                Slug = entry.Slug,
                DisplayName = entry.Name,
                Description = entry.Desc,
                Category = entry.Category,
                Price = StoreItemPrice,
                CurrencyType = 2,
                ImageName = entry.Image,
                Storefront = entry.Storefront,
                IsActive = true,
                IsLimitedTime = entry.Storefront.StartsWith("giftdrop:"),
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync();

        await SeedGameCatalogAdditionsAsync(existingSet);
        await SeedEquipmentSkinsAsync(existingSet);
        await SeedClothingFromGameAsync(existingSet);
        await SeedColorVariantsAsync(existingSet);
        await NormalizeStorePricesAsync();
    }

    private async Task SeedGameCatalogAdditionsAsync(HashSet<string> existingSlugs)
    {
        var added = false;

        foreach (var dye in PermanentHairDyes)
        {
            if (existingSlugs.Contains(dye.Slug)) continue;
            db.StoreItems.Add(new StoreItemEntity
            {
                Slug = dye.Slug,
                DisplayName = dye.Name,
                Description = "Permanent hair dye.",
                Category = "consumable",
                Price = StoreItemPrice,
                CurrencyType = 2,
                ImageName = string.Empty,
                Storefront = "main",
                IsActive = true,
                IsLimitedTime = false,
            });
            existingSlugs.Add(dye.Slug);
            added = true;
        }

        if (added) await db.SaveChangesAsync();
    }

    // ── Equipment skins ────────────────────────────────────────────
    // Source: data/equipment_skins.json. The old client does not infer
    // equipment reward payloads from a store slug; it expects the exact
    // PrefabName + ModificationGuid pair from the equipment skin runtime
    // config. Keep those values data-driven so new verified skin rows can
    // be added without changing controller code.
    public sealed class EquipmentSkinEntry
    {
        public string prefab_name { get; set; } = string.Empty;
        public string modification_guid { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string tooltip { get; set; } = string.Empty;
        public int rarity { get; set; }
        public string storefront { get; set; } = "main";
        public long price { get; set; } = StoreItemPrice;
        public string image_name { get; set; } = string.Empty;
    }

    private sealed class EquipmentSkinFile
    {
        public List<EquipmentSkinEntry> items { get; set; } = new();
    }

    private static readonly Lazy<Dictionary<string, EquipmentSkinEntry>> _equipmentSkinCatalog =
        new(LoadEquipmentSkinCatalog);

    public static IReadOnlyDictionary<string, EquipmentSkinEntry> EquipmentSkinCatalog =>
        _equipmentSkinCatalog.Value;

    private static Dictionary<string, EquipmentSkinEntry> LoadEquipmentSkinCatalog()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "equipment_skins.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "equipment_skins.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "equipment_skins.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "DorkNet.Server", "Data", "equipment_skins.json"),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return new();

        try
        {
            using var fs = File.OpenRead(path);
            var data = System.Text.Json.JsonSerializer.Deserialize<EquipmentSkinFile>(fs,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new EquipmentSkinFile();

            var bySlug = new Dictionary<string, EquipmentSkinEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.items)
            {
                if (string.IsNullOrWhiteSpace(item.prefab_name) ||
                    string.IsNullOrWhiteSpace(item.modification_guid))
                    continue;

                bySlug[EquipmentSkinSlug(item.prefab_name, item.modification_guid)] = item;
            }
            return bySlug;
        }
        catch
        {
            return new();
        }
    }

    public static string EquipmentSkinSlug(string prefabName, string modificationGuid)
    {
        prefabName = (prefabName ?? string.Empty).Trim();
        modificationGuid = (modificationGuid ?? string.Empty).Trim();
        return $"{EquipmentSkinPrefix}{prefabName}:{modificationGuid}";
    }

    public static bool TryGetEquipmentPayload(
        string? slug,
        out string prefabName,
        out string modificationGuid,
        out EquipmentSkinEntry? entry)
    {
        prefabName = string.Empty;
        modificationGuid = string.Empty;
        entry = null;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        if (_equipmentSkinCatalog.Value.TryGetValue(slug, out entry))
        {
            prefabName = entry.prefab_name;
            modificationGuid = entry.modification_guid;
            return true;
        }

        if (!slug.StartsWith(EquipmentSkinPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var payload = slug[EquipmentSkinPrefix.Length..];
        var split = payload.IndexOf(':');
        if (split <= 0 || split >= payload.Length - 1) return false;
        prefabName = payload[..split];
        modificationGuid = payload[(split + 1)..];
        return !string.IsNullOrWhiteSpace(prefabName)
            && !string.IsNullOrWhiteSpace(modificationGuid);
    }

    public static bool TryGetEquipmentPayload(
        string? slug,
        out string prefabName,
        out string modificationGuid) =>
        TryGetEquipmentPayload(slug, out prefabName, out modificationGuid, out _);

    public static bool TryGetGiftBoxEquipmentPayload(
        string? slug,
        out string prefabName,
        out string modificationGuid,
        out EquipmentSkinEntry? entry)
    {
        if (!TryGetEquipmentPayload(slug, out prefabName, out modificationGuid, out entry))
            return false;

        // The 2020 client previews GiftPackage equipment by spawning the
        // prefab. Spawning [MakerPen] outside the real tool equip flow wakes
        // MakerPen tool scripts without their visuals wired up, causing a
        // repeating MakerPenVisuals null-ref/spin. Still grant the skin as
        // inventory; just don't ask the gift-box/store-preview path to spawn it.
        if (IsMakerPenPrefab(prefabName))
        {
            prefabName = string.Empty;
            modificationGuid = string.Empty;
            entry = null;
            return false;
        }

        return true;
    }

    public static bool TryGetGiftBoxEquipmentPayload(
        string? slug,
        out string prefabName,
        out string modificationGuid) =>
        TryGetGiftBoxEquipmentPayload(slug, out prefabName, out modificationGuid, out _);

    private static bool IsMakerPenPrefab(string? prefabName) =>
        string.Equals(prefabName, "[MakerPen]", StringComparison.OrdinalIgnoreCase);

    private async Task SeedEquipmentSkinsAsync(HashSet<string> existingSlugs)
    {
        var catalog = _equipmentSkinCatalog.Value;
        if (catalog.Count == 0) return;

        var added = false;
        foreach (var (slug, entry) in catalog)
        {
            if (existingSlugs.Contains(slug)) continue;
            var storefront = string.IsNullOrWhiteSpace(entry.storefront) ? "main" : entry.storefront;

            db.StoreItems.Add(new StoreItemEntity
            {
                Slug = slug,
                DisplayName = string.IsNullOrWhiteSpace(entry.display_name)
                    ? $"{entry.prefab_name} skin"
                    : entry.display_name,
                Description = string.IsNullOrWhiteSpace(entry.tooltip)
                    ? "Equipment skin."
                    : entry.tooltip,
                Category = "weapon",
                Price = entry.price > 0 ? entry.price : StoreItemPrice,
                CurrencyType = 2,
                ImageName = entry.image_name,
                Storefront = storefront,
                IsActive = true,
                IsLimitedTime = storefront.StartsWith("giftdrop:", StringComparison.OrdinalIgnoreCase),
            });
            existingSlugs.Add(slug);
            added = true;
        }

        if (added) await db.SaveChangesAsync();
    }

    // ── Color variants ──────────────────────────────────────────────
    // Source: data/color_variants.json — generated from a static walk
    // of the 2020 game's outfits_assets_*.bundle files (Python script
    // at tools/build-color-variants.py). Each entry maps a swatch
    // (with item GUID + mask GUID) to a color name. We seed one
    // StoreItem per (item, swatch) with slug
    //   wardrobe-colored-{itemGuid}-{colorLower}
    // so TryGetAvatarItemPayload can recover the desc on demand.
    //
    // The pure wardrobe-{itemGuid} slugs (no color) keep working for
    // items without color variants, and for legacy single-color SKUs
    // — those still emit "<itemGuid>,,," and the watch picks a default.
    public const string WardrobeColoredPrefix = "wardrobe-colored-";

    public sealed class ColorVariantEntry
    {
        public string swatch_name { get; set; } = string.Empty;
        public string swatch_guid { get; set; } = string.Empty;
        public string color { get; set; } = string.Empty;
        public string item_name { get; set; } = string.Empty;
        public string item_guid { get; set; } = string.Empty;
        public int? item_outfit_type { get; set; }
        public string mask_guid { get; set; } = string.Empty;
    }
    private sealed class ColorVariantFile
    {
        public List<ColorVariantEntry> matches { get; set; } = new();
    }

    private static readonly Lazy<Dictionary<string, ColorVariantEntry>> _variantCatalog =
        new(LoadColorVariantCatalog);

    private static Dictionary<string, ColorVariantEntry> LoadColorVariantCatalog()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "color_variants.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "color_variants.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "color_variants.json"),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return new();
        try
        {
            using var fs = File.OpenRead(path);
            var data = System.Text.Json.JsonSerializer.Deserialize<ColorVariantFile>(fs,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ColorVariantFile();
            var byKey = new Dictionary<string, ColorVariantEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in data.matches)
            {
                if (string.IsNullOrEmpty(m.item_guid) || string.IsNullOrEmpty(m.swatch_guid)) continue;
                var key = $"{WardrobeColoredPrefix}{m.item_guid}-{m.color}".ToLowerInvariant();
                byKey[key] = m;
            }
            return byKey;
        }
        catch { return new(); }
    }

    public static IReadOnlyDictionary<string, ColorVariantEntry> ColorVariantCatalog => _variantCatalog.Value;

    /// <summary>Item GUIDs that have at least one color-variant SKU. The
    /// generic <c>wardrobe-{guid}</c> tile is suppressed for these
    /// because the colored tiles render the correct swatch; the bare
    /// tile would emit <c>{guid},,,</c> and fall back to the default
    /// swatch (often a placeholder colour the player didn't pick).
    /// </summary>
    private static readonly Lazy<HashSet<string>> _itemsWithColorVariants =
        new(() => new HashSet<string>(
            _variantCatalog.Value.Values.Select(e => e.item_guid),
            StringComparer.OrdinalIgnoreCase));

    private async Task SeedColorVariantsAsync(HashSet<string> existingSlugs)
    {
        var catalog = _variantCatalog.Value;
        if (catalog.Count == 0) return;

        var added = false;
        foreach (var (slug, entry) in catalog)
        {
            if (existingSlugs.Contains(slug)) continue;

            // Friendlier display name: strip parens + slot prefix from
            // the item name, then suffix the color. So
            // "(Shirt_Lumberjack)" + "Blue" → "Lumberjack — Blue".
            var inner = (entry.item_name ?? string.Empty).Trim('(', ')');
            foreach (var s in new[] { "Hat_", "Shirt_", "Head_", "Hair_", "Eye_", "Wrist_",
                                       "Neck_", "Beard_", "Pants_", "Feet_", "Accessory_",
                                       "Belt_", "Hand_" })
            {
                if (inner.StartsWith(s)) { inner = inner[s.Length..]; break; }
            }
            inner = inner.Replace('_', ' ').Trim();
            var displayName = $"{inner} — {entry.color}";

            db.StoreItems.Add(new StoreItemEntity
            {
                Slug = slug,
                DisplayName = displayName,
                Description = $"{entry.color} variant of {inner}.",
                Category = ColorVariantCategory(entry.item_outfit_type),
                Price = StoreItemPrice,
                CurrencyType = 2,
                ImageName = string.Empty,
                Storefront = "main",
                IsActive = true,
                IsLimitedTime = false,
            });
            existingSlugs.Add(slug);
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    /// <summary>Map outfit_type (per <c>avatar_item_lookup.json</c>) to the
    /// store Category column. Outfit types: 0=Hat, 2=Hair, 10=Eye,
    /// 20=Beard, 100=Neck, 101=Torso, 200=Wrist (others fall through).</summary>
    private static string ColorVariantCategory(int? outfitType) => outfitType switch
    {
        0 => "head",
        2 => "hair",
        10 or 20 => "head",
        100 or 101 => "torso",
        200 => "accessory",
        _ => "accessory",
    };

    private async Task NormalizeStorePricesAsync()
    {
        await db.StoreItems
            .Where(i => i.Price != StoreItemPrice)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Price, StoreItemPrice));
    }

    /// <summary>
    /// Seed the storefront from
    /// <see cref="DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.Catalog"/>
    /// — every AvatarItem GUID in the watch's bundled
    /// <c>AvatarItemWardrobeRuntimeConfig.avatarItemDataLookup</c>
    /// becomes one tile. Slug format <c>wardrobe-{avatarItemGuid}</c>
    /// so the watch's `PurchasableGiftDrop` flow can extract the GUID
    /// from the slug at <see cref="BuildPurchasableGiftDrop"/> time
    /// and put it in <c>AvatarItemDesc</c> for the watch to render
    /// the 3D preview prefab in the tile.
    ///
    /// The previous implementation pulled from
    /// <c>data/wardrobe_items.json</c> (Swatch/Mask MonoBehaviour
    /// GUIDs extracted from <c>outfits_assets_*.bundle</c>). Those
    /// were ASSET GUIDs, not AvatarItem GUIDs, so the watch couldn't
    /// resolve them in <c>avatarItemPrefabLookup</c> → tiles came
    /// out blank. The new source uses the same dump
    /// <c>AvatarItemsController</c> validates against, so every
    /// store tile is guaranteed to render.
    /// </summary>
    private async Task SeedClothingFromGameAsync(HashSet<string> existingSlugs)
    {
        var catalog = DorkNet.Server.Controllers.API.Avatar.V4.AvatarItemsController.Catalog;
        if (catalog.Count == 0) return;

        // Backfill prices on existing wardrobe rows that were seeded
        // with the previous Price=0 default — store UI shows "0
        // tokens" on every tile otherwise. Idempotent: only updates
        // rows whose price is still 0.
        var stalePriceRows = await db.StoreItems
            .Where(i => i.Slug.StartsWith("wardrobe-") && i.Price == 0)
            .Select(i => new { i.Id, i.Slug })
            .ToListAsync();
        if (stalePriceRows.Count > 0)
        {
            foreach (var r in stalePriceRows)
            {
                var guid = r.Slug.Substring("wardrobe-".Length);
                await db.StoreItems
                    .Where(s => s.Id == r.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, StoreItemPrice));
            }
        }

        var added = false;
        foreach (var (guid, dto) in catalog)
        {
            var slug = $"wardrobe-{guid}";
            if (existingSlugs.Contains(slug)) continue;
            existingSlugs.Add(slug);
            db.StoreItems.Add(new StoreItemEntity
            {
                Slug         = slug,
                DisplayName  = string.IsNullOrEmpty(dto.FriendlyName) ? guid : dto.FriendlyName,
                Description  = "Wardrobe item.",
                // Category just routes the tile to a tab in the Shop
                // UI; we map AvatarItemsController.Slot codes to
                // human-readable category labels.
                Category     = CategoryFromFriendlyName(dto.FriendlyName),
                Price        = StoreItemPrice,
                CurrencyType = 2,
                ImageName    = string.Empty,
                Storefront   = "main",
                IsActive     = true,
                IsLimitedTime = false,
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    private static int SumCharCodes(string s)
    {
        int sum = 0;
        for (int i = 0; i < s.Length; i++) sum = unchecked(sum * 31 + s[i]);
        return sum;
    }

    private static string CategoryFromFriendlyName(string name)
    {
        // Names from the bundled AvatarItemData arrive as e.g. "Hat
        // Wizard", "Hair AngledBob", "Beard Close", "Eye 2020Glasses",
        // "Belt PaintballBelt" — the FIRST word is the slot label.
        // Map to the same bucket strings the previous catch-all used
        // so any UI code still grouping by category keeps working.
        var space = name.IndexOf(' ');
        var head = (space < 0 ? name : name[..space]).ToLowerInvariant();
        return head switch
        {
            "hat" or "helmet" or "cap" or "crown" or "beanie" or "mask" => "head",
            "hair" => "hair",
            "eye" or "glass" or "glasses" or "goggle" or "goggles" => "face",
            "mouth" or "beard" or "stache" or "moustache" or "mustache"
                or "copstache" or "seventiesstache" => "face",
            "shirt" or "jacket" or "hoodie" or "jumpsuit" or "tshirt" or "blouse"
                or "dress" or "vest" or "torso" or "babydolldress" => "torso",
            "pant" or "short" or "skirt" or "trouser" => "legs",
            "shoe" or "boot" or "sock" or "sneaker" => "feet",
            "neck" or "tie" or "bowtie" or "businesstie" or "cowboyscarf"
                or "archerquiver" or "quiver" => "neck",
            "belt" => "belt",
            "wrist" or "glove" or "watch" or "partyband" => "wrist",
            _ => "accessory",
        };
    }

    public async Task<List<StoreItemEntity>> GetActiveByStorefrontAsync(string storefront) =>
        await db.StoreItems
            .Where(i => i.IsActive && i.Storefront == storefront &&
                (i.AvailableUntil == null || i.AvailableUntil > DateTime.UtcNow))
            .OrderBy(i => i.Category).ThenBy(i => i.Price)
            .ToListAsync();

    public async Task<List<StoreItemEntity>> GetRoomStorefrontItemsAsync(int storefrontType)
    {
        var key = StorefrontKeyForType(storefrontType);
        var keys = new[] { key, RroStorefrontKey, AllStorefrontKey };
        var rows = await db.StoreItems
            .Where(i => i.IsActive && keys.Contains(i.Storefront) &&
                (i.AvailableUntil == null || i.AvailableUntil > DateTime.UtcNow))
            .OrderBy(i => i.Storefront == key ? 0 :
                i.Storefront == RroStorefrontKey ? 1 : 2)
            .ThenBy(i => i.Category)
            .ThenBy(i => i.Price)
            .ToListAsync();

        if (rows.Any(IsRenderableGiftDrop) ||
            !config.GetValue("Store:FallbackEmptyRoomStorefrontsToWatch", true))
        {
            return rows;
        }

        return await GetRenderableGiftDropItemsAsync();
    }

    public async Task<List<StoreItemEntity>> GetAllActiveAsync() =>
        await db.StoreItems
            .Where(i => i.IsActive &&
                (i.AvailableUntil == null || i.AvailableUntil > DateTime.UtcNow))
            .OrderBy(i => i.Storefront).ThenBy(i => i.Category).ThenBy(i => i.Price)
            .ToListAsync();

    public async Task<List<StoreItemEntity>> GetRenderableGiftDropItemsAsync()
    {
        // The March 2020 client imports Watch storefront gift drops into
        // RRUI.Cache.AvatarItemCache and binds them through
        // DynamicAvatarItemImposter. Only publish slots that the decompiled
        // watch/store filters handle; unsupported OutfitTypes can enter the
        // avatar cache and crash during tile preview binding.
        if (!EnableWatchGiftDrops) return new List<StoreItemEntity>();

        var rows = await db.StoreItems
            .Where(i => i.IsActive &&
                (i.AvailableUntil == null || i.AvailableUntil > DateTime.UtcNow))
            .OrderBy(i => i.Category).ThenBy(i => i.DisplayName)
            .ToListAsync();

        var filtered = rows
            .Where(IsRenderableGiftDrop)
            .ToList();

        var max = MaxWatchGiftDrops;
        return max > 0 ? filtered.Take(max).ToList() : filtered;
    }

    public Task<StoreItemEntity?> GetBySlugAsync(string slug) =>
        db.StoreItems.FirstOrDefaultAsync(i => i.Slug == slug);

    public Task<StoreItemEntity?> GetByIdAsync(long id) =>
        db.StoreItems.FirstOrDefaultAsync(i => i.Id == id);

    public sealed record PurchaseResult(bool Success, string? Error, long? Balance, string? Slug);

    /// <summary>Atomic purchase: re-checks balance, deducts currency,
    /// appends the item to the player's inventory. Idempotent on
    /// already-owned items (returns Success=true without re-charging).
    /// Returns the resulting balance and the slug granted.</summary>
    public async Task<PurchaseResult> PurchaseAsync(long playerId, long itemId)
    {
        var item = await GetByIdAsync(itemId);
        if (item is null || !item.IsActive)
            return new(false, "item_not_available", null, null);
        if (item.AvailableUntil is { } until && until <= DateTime.UtcNow)
            return new(false, "item_expired", null, null);

        var balance = await level.GetBalanceAsync(playerId, item.CurrencyType);
        if (balance < item.Price)
            return new(false, "insufficient_funds", balance, null);

        var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == playerId);
        if (avatar is null)
        {
            avatar = new AvatarEntity { PlayerId = playerId };
            db.Avatars.Add(avatar);
        }

        if (TryGetAvatarItemPayload(item.Slug, out _, out var avatarItemDesc))
        {
            var guid = InventoryAvatarItemDesc(avatarItemDesc);
            var ownedGuids = ParseWardrobeInventory(avatar.InventoryJson);
            if (ownedGuids.Contains(guid, StringComparer.OrdinalIgnoreCase))
                return new(true, "already_owned", balance, item.Slug);

            var newWardrobeBalance = await level.GrantCurrencyAsync(
                playerId, item.CurrencyType, -item.Price, $"purchase:{item.Slug}");
            ownedGuids.Add(guid);
            avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(ownedGuids);
            avatar.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return new(true, null, newWardrobeBalance, item.Slug);
        }

        // Idempotency: own-already → return success without re-charging.
        var inventory = ParseInventory(avatar.InventoryJson);
        var existing = inventory.FirstOrDefault(e => e.ItemId == item.Slug);
        if (existing is not null)
        {
            if (string.Equals(item.Category, "consumable", StringComparison.OrdinalIgnoreCase))
            {
                var newBalanceConsumable = await level.GrantCurrencyAsync(
                    playerId, item.CurrencyType, -item.Price, $"purchase:{item.Slug}");
                existing.Quantity += 1;
                avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(inventory);
                avatar.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return new(true, null, newBalanceConsumable, item.Slug);
            }
            return new(true, "already_owned", balance, item.Slug);
        }

        var newBalance = await level.GrantCurrencyAsync(
            playerId, item.CurrencyType, -item.Price, $"purchase:{item.Slug}");
        inventory.Add(new InventoryEntry { ItemId = item.Slug, Quantity = 1 });
        avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(inventory);
        avatar.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, null, newBalance, item.Slug);
    }

    private static List<string> ParseWardrobeInventory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new();
        try
        {
            var strings = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
            if (strings is not null) return strings;
        }
        catch { }

        try
        {
            var entries = ParseInventory(json);
            return entries
                .Select(e => e.ItemId.StartsWith("wardrobe-", StringComparison.Ordinal)
                    ? e.ItemId["wardrobe-".Length..]
                    : e.ItemId)
                .Select(InventoryAvatarItemDesc)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new(); }
    }

    public sealed class InventoryEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("itemId")]
        public string ItemId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }

    public static List<InventoryEntry> ParseInventory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new();
        try
        {
            return System.Text.Json.JsonSerializer
                .Deserialize<List<InventoryEntry>>(json) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// GiftDropStorefront wire shape, verified against ISIL:
    ///   GiftDropStorefront.Deserialize    → reads "StoreItems"
    ///     (Cpp2IL_ISIL/.../GiftDropStorefront.txt:98)
    ///   StoreItems is List&lt;PurchasableGiftDrop&gt;
    ///   PurchasableGiftDrop.Deserialize   → calls PurchasableItem.Deserialize
    ///     then reads "GiftDrops" as List&lt;StorefrontGiftDrop&gt;
    ///     (PurchasableGiftDrop.txt:118)
    ///   PurchasableItem.Deserialize       → REQUIRED keys
    ///     PurchasableItemId / Type / IsFeatured / Prices / SubscriberPrices
    ///     (PurchasableItem.txt:592-654)
    ///   PurchasablePrice.Deserialize      → CurrencyType / Price
    ///     (PurchasablePrice.txt:87-94)
    ///   StorefrontGiftDrop.Deserialize    → REQUIRED GiftDropId / Rarity
    ///     plus optional FriendlyName / Tooltip / AvatarItemDesc /
    ///     AvatarItemType / ConsumableItemDesc / EquipmentPrefabName /
    ///     EquipmentModificationGuid / IsQuery / Unique / SubscribersOnly /
    ///     Level / Context (StorefrontGiftDrop.txt:829-924).
    ///
    /// Each StoreItemEntity becomes one PurchasableGiftDrop holding a
    /// single StorefrontGiftDrop pointing at the associated wardrobe
    /// AvatarItem GUID. Only <c>wardrobe-{guid}</c>-prefixed slugs are
    /// emitted because the 2020 watch storefront binds every gift-drop
    /// tile through <c>BrowsableAvatarItem</c>. Sending a gift drop with
    /// AvatarItemType=Outfit and an empty AvatarItemDesc leaves that
    /// binding half-populated and can crash the client while rendering
    /// the shop grid.
    /// </summary>
    public static object ToStorefrontDto(string name, IEnumerable<StoreItemEntity> items, string apex = "rec.net", int storefrontType = 3)
    {
        var storeItems = items
            .Where(IsRenderableGiftDrop)
            .Select((i, idx) => BuildPurchasableGiftDrop(i, idx))
            .ToArray();
        return new
        {
            StorefrontType = storefrontType,
            StorefrontName = name,
            // NOT year 9999: that overflows the watch's local-timezone date
            // parse in some DST timezones → "malformed RecNet response" and a
            // broken store for that player only. See WireDates.
            NextUpdate = WireDates.FarFutureIso,
            NewUntil = WireDates.FarFutureIso,
            StoreItems = storeItems,
        };
    }

    private static bool IsRenderableGiftDrop(StoreItemEntity item)
    {
        if (TryGetHairDyePayload(item.Slug, out _)) return true;
        if (TryGetConsumableItemDesc(item.Slug, out _)) return true;
        if (TryGetEquipmentPayload(item.Slug, out _, out _)) return true;

        if (!TryGetAvatarItemPayload(item.Slug, out var avatarItemType, out var avatarItemDesc))
            return false;
        if (avatarItemType != StoreAvatarItemTypeOutfit) return false;

        var guid = avatarItemDesc.Split(',', 2)[0];
        return AvatarItemsController.TryGetOutfitType(guid, out var outfitType)
            && IsWatchStorefrontOutfitType(outfitType);
    }

    private static bool IsWatchStorefrontOutfitType(int outfitType) => outfitType is
        AvatarItemsController.Slot.Hat or
        AvatarItemsController.Slot.BackHead or
        AvatarItemsController.Slot.Hair or
        AvatarItemsController.Slot.Eye or
        AvatarItemsController.Slot.Mouth or
        AvatarItemsController.Slot.Neck or
        AvatarItemsController.Slot.Shirt or
        AvatarItemsController.Slot.Belt or
        AvatarItemsController.Slot.Pocket or
        AvatarItemsController.Slot.Wrist or
        AvatarItemsController.Slot.Glove or
        AvatarItemsController.Slot.Watch or
        AvatarItemsController.Slot.TeamShirt or
        AvatarItemsController.Slot.TeamWrist;

    /// <summary>One PurchasableGiftDrop carrying one StorefrontGiftDrop.
    /// PurchasableGiftDrop inherits PurchasableItem so we ship the
    /// PurchasableItem keys plus the GiftDrops list in the same
    /// object.</summary>
    private static object BuildPurchasableGiftDrop(StoreItemEntity i, int positionalIndex)
    {
        // PurchasableItemId is an int and must be unique within the
        // storefront — we use the StoreItem row id, capped to int range.
        var itemId = (int)(i.Id & 0x7fffffff);

        var price = new
        {
            CurrencyType = i.CurrencyType,
            Price        = (int)i.Price,
        };

        // Pull the AvatarItem GUID out of the slug for wardrobe-imported
        // entries. Hand-curated <c>dorknet-*</c> SKUs leave AvatarItemDesc
        // empty — they're cosmetic store tiles only.
        //
        // CRITICAL: the watch's AvatarItem.FromRecNetString does
        // String.Split(',') and indexes customization[0..2] via
        // ArraySegmentEnumerator.get_Current — anything shorter than
        // four comma-separated parts throws ArgumentException ("Offset
        // and length were out of bounds…") AND, because that exception
        // bubbles up through OutfitManager.IsAvatarItemAlreadyPurchased
        // → WatchStoreItemButton.UpdateButtonInteractability → the
        // store's UI grid render path, the watch crashes mid-tile. Bare
        // <c>{guid}</c> = 1 part → instant crash on first store-tile
        // hover; <c>{guid},,,</c> = 4 parts (guid + 3 empty
        // customizations) → safe. AvatarItemsController.Get() applies
        // the same suffix to /api/avatar/v4/items entries; we apply it
        // here too.
        TryGetAvatarItemPayload(i.Slug, out var avatarItemType, out var avatarItemDesc);
        TryGetConsumableItemDesc(i.Slug, out var consumableItemDesc);
        TryGetGiftBoxEquipmentPayload(i.Slug, out var equipmentPrefabName, out var equipmentModificationGuid, out var equipmentEntry);
        var isHairDye = TryGetHairDyePayload(i.Slug, out var consumableHairDyeDesc, out var hairDyeColorGuid);
        if (isHairDye)
        {
            avatarItemType = StoreAvatarItemTypeHairDye;
            avatarItemDesc = hairDyeColorGuid;
            consumableItemDesc = consumableHairDyeDesc;
        }

        var giftDrop = new
        {
            // REQUIRED.
            GiftDropId   = itemId,
            Rarity       = MapRarity(i.Category),
            // Optional / nullable string fields the watch reads.
            FriendlyName              = i.DisplayName,
            Tooltip                   = i.Description,
            AvatarItemDesc            = avatarItemDesc,
            AvatarItemDescOrHairDyeDesc = avatarItemDesc,
            AvatarItemType            = string.IsNullOrEmpty(avatarItemDesc) ? (int?)null : avatarItemType,
            ConsumableItemDesc        = consumableItemDesc,
            EquipmentPrefabName       = equipmentPrefabName,
            EquipmentModificationGuid = equipmentModificationGuid,
            IsQuery                   = false,
            Unique                    = true,
            SubscribersOnly           = false,
            Level                     = 1,
            Context                   = 0,
            Content                   = isHairDye ? GiftBoxContentHairDye : 0,
            EquipmentRarity           = equipmentEntry?.rarity ?? 0,
        };

        return new
        {
            // Base PurchasableItem keys.
            PurchasableItemId = itemId,
            Type              = 0,                       // PurchasableItemType.GiftDrop
            IsFeatured        = positionalIndex < 12,    // first dozen show in Featured tab
            Prices            = new[] { price },
            SubscriberPrices  = Array.Empty<object>(),
            // PurchasableGiftDrop adds GiftDrops on top.
            GiftDrops         = new[] { giftDrop },
        };
    }

    /// <summary>GiftManager.GiftRarity wire values:
    /// None=-1, Common=0, Uncommon=10, Rare=20, Epic=30, Legendary=50.</summary>
    private static int MapRarity(string? category) => category?.ToLowerInvariant() switch
    {
        "consumable" => 0,
        "head" or "hair" or "face" => 10,
        "torso" or "neck" or "belt" or "wrist" => 20,
        "accessory" => 30,
        "roomtemplate" => 50,
        _ => 0,
    };

    public static bool TryGetAvatarItemPayload(string? slug, out int avatarItemType, out string avatarItemDesc)
    {
        avatarItemType = StoreAvatarItemTypeOutfit;
        avatarItemDesc = string.Empty;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        // Colored variant slug (wardrobe-colored-<itemGuid>-<color>) —
        // resolve via the static catalog to get the swatch + mask
        // GUIDs. AvatarItem RecNet desc format verified against the
        // watch's AvatarItemVisualData.ToRecNetString (Cpp2IL_ISIL/.../
        // RecRoom/Avatar/Data/Shared/AvatarItemVisualData.txt:474-585):
        //   <prefabGuid>,<swatchGuid>,<maskGuid>,<decalGuid>
        // — swatch BEFORE mask, not the other way around. Earlier code
        // had them flipped, which made the watch read the mask GUID as
        // a swatch GUID, fail to resolve it, and fall back to the
        // default swatch.
        if (slug.StartsWith(WardrobeColoredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (_variantCatalog.Value.TryGetValue(slug.ToLowerInvariant(), out var entry))
            {
                avatarItemDesc = $"{entry.item_guid},{entry.swatch_guid},{entry.mask_guid},";
                return true;
            }
            // Fall through to wardrobe- handling if not in catalog.
        }

        if (slug.StartsWith(WardrobePrefix, StringComparison.Ordinal))
        {
            avatarItemDesc = slug[WardrobePrefix.Length..] + ",,,";
            return true;
        }

        return false;
    }

    public static string InventoryAvatarItemDesc(string avatarItemDesc)
    {
        if (string.IsNullOrWhiteSpace(avatarItemDesc)) return string.Empty;
        return avatarItemDesc.EndsWith(",,,", StringComparison.Ordinal)
            ? avatarItemDesc[..^3]
            : avatarItemDesc;
    }

    public static bool TryGetConsumableItemDesc(string? slug, out string consumableItemDesc)
    {
        consumableItemDesc = string.Empty;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        if (TryGetHairDyePayload(slug, out consumableItemDesc, out _)) return true;

        return false;
    }

    public static bool TryGetHairDyePayload(string? slug, out string hairDyeDesc)
    {
        return TryGetHairDyePayload(slug, out _, out hairDyeDesc);
    }

    public static bool TryGetHairDyeName(string? slug, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        foreach (var dye in PermanentHairDyes)
        {
            if (!string.Equals(slug, dye.Slug, StringComparison.OrdinalIgnoreCase)) continue;
            name = dye.Name;
            return true;
        }

        return false;
    }

    public static bool TryGetHairDyePayload(string? slug, out string consumableDesc, out string hairColorGuid)
    {
        consumableDesc = string.Empty;
        hairColorGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(slug)) return false;

        foreach (var dye in PermanentHairDyes)
        {
            if (!string.Equals(slug, dye.Slug, StringComparison.OrdinalIgnoreCase)) continue;
            consumableDesc = dye.ConsumableDesc;
            hairColorGuid = dye.HairColorGuid;
            return true;
        }

        return false;
    }

    public static object ToItemDto(StoreItemEntity i, string apex = "rec.net") => new
    {
        ItemId = i.Id,
        Slug = i.Slug,
        Name = i.DisplayName,
        DisplayName = i.DisplayName,
        Description = i.Description,
        Category = i.Category,
        ImageName = i.ImageName,
        ImageUrl = string.IsNullOrEmpty(i.ImageName) ? "" : $"https://cdn.{apex}/{i.ImageName}",
        // Price block — both flat and nested forms so whichever shape
        // the watch's deserialiser reads, it sees a price.
        Price = i.Price,
        CurrencyType = i.CurrencyType,
        Cost = new { Amount = i.Price, CurrencyType = i.CurrencyType },
        IsActive = i.IsActive,
        IsLimitedTime = i.IsLimitedTime,
        AvailableUntil = i.AvailableUntil,
    };
}
