using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One purchasable item in the in-game store. The watch's Shop tab
/// renders these, and the purchase flow (initiatepurchase →
/// processpurchase → completepurchase) verifies the player has enough
/// of <see cref="CurrencyType"/> currency, deducts <see cref="Price"/>,
/// and appends <see cref="Slug"/> to the buyer's
/// <see cref="AvatarEntity.InventoryJson"/> list so the customisation
/// menu can equip it.
///
/// Item slots / categories track the avatar slots the watch's
/// outfit-selector groups items by — Head, Torso, Legs, Feet,
/// Accessory, Face, Hair, plus non-avatar categories (Consumable,
/// RoomTemplate, Emote) that don't go into outfit selections but
/// still show up in the player's inventory.
/// </summary>
public class StoreItemEntity
{
    public long Id { get; set; }

    /// <summary>Stable string identifier — what we put in
    /// InventoryJson and OutfitSelections. Format is a GUID for
    /// avatar items (so the watch's avatar resolver matches the
    /// baked AvatarItemDefinition catalog), or a kebab-case slug for
    /// non-avatar items (consumables / templates). Unique.</summary>
    // Widened from 64→128 in 2026-05 to accommodate the
    // wardrobe-colored-{guid}-{color} slugs the color-variant seeder
    // generates — longest in the catalog is ~68 chars (long color
    // names like "WhiteHatHacker" + 36-char GUID + prefix).
    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Avatar slot or item type. One of: head, torso, legs,
    /// feet, accessory, face, hair, consumable, roomtemplate, emote.
    /// </summary>
    [MaxLength(32)]
    public string Category { get; set; } = "accessory";

    /// <summary>cdn filename (relative to cdn.rec.net/) for the
    /// store-tile thumbnail. Empty = the watch falls back to a
    /// silhouette. Same format as RoomEntity.ImageName.</summary>
    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>Currency the item is priced in. 2 = standard tokens
    /// (the everyday currency earned from level-up + cheers); higher
    /// numbers reserved for premium currencies (e.g. 1 = real-money
    /// purchases — never enabled on a private server).</summary>
    public int CurrencyType { get; set; } = 2;

    public long Price { get; set; }

    /// <summary>Listing flag. False = hidden from the store but kept
    /// in the catalog so existing inventory references stay
    /// resolvable. Admin-only mutation.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True for limited-time / event items. The watch
    /// renders an "ends in" countdown alongside these and pulls them
    /// from the catalog when AvailableUntil expires (we keep the row,
    /// just flip IsActive=false on expiry).</summary>
    public bool IsLimitedTime { get; set; }

    public DateTime? AvailableUntil { get; set; }

    /// <summary>Storefront grouping. <c>"main"</c> = the everyday
    /// shop; <c>"giftdrop:N"</c> = a specific rotating gift-drop shelf
    /// the 2020 client probes via /storefronts/v3/giftdropstore/{N};
    /// <c>"rro"</c> = every RRO/Rec Center in-room shelf;
    /// <c>"all"</c> = every in-room shelf; <c>"season:N"</c> =
    /// season-pass ladders. Default is main.
    /// </summary>
    [MaxLength(32)]
    public string Storefront { get; set; } = "main";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
