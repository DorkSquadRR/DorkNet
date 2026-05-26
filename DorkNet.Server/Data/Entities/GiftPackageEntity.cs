using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per <c>RecNet.Avatars+GiftPackage</c> wire shape (decompiled at
/// <c>Cpp2IL_CS/.../RecNet/Avatars.cs:192-555</c>) — a queued gift the
/// recipient can claim from the watch's "gifts" inbox. Used for both
/// admin-granted gifts and gift-drop / mystery-box / level-up prizes.
///
/// Field names + types match the client's
/// <c>GiftPackage.Deserialize(Dictionary&lt;String, Object&gt;)</c>
/// directly so the watch round-trips cleanly. <see cref="Consumed"/>
/// is the bool the client reads — we additionally stamp
/// <see cref="ConsumedAt"/> for server-side bookkeeping (not on wire).
/// </summary>
public class GiftPackageEntity
{
    public long Id { get; set; }

    /// <summary>The player whose inbox this gift sits in.</summary>
    public long RecipientPlayerId { get; set; }

    /// <summary>Wire field (Nullable&lt;Int32&gt;) — the player who
    /// sent the gift, or null for system / drop gifts.</summary>
    public int? FromPlayerId { get; set; }

    [MaxLength(128)]
    public string ConsumableItemDesc { get; set; } = string.Empty;

    /// <summary>Wire field <c>AvatarItemType</c> as Nullable&lt;int&gt;
    /// (0 = Outfit, 1 = HairDye). Null when the gift has no avatar
    /// item (currency-only, equipment-only, etc.).</summary>
    public int? AvatarItemType { get; set; }

    /// <summary>Wire field name is exactly <c>AvatarItemDescOrHairDyeDesc</c>
    /// — content is either the AvatarItem GUID (when AvatarItemType=0)
    /// or the HairDye desc string (when AvatarItemType=1).</summary>
    [MaxLength(128)]
    public string AvatarItemDescOrHairDyeDesc { get; set; } = string.Empty;

    [MaxLength(128)]
    public string EquipmentPrefabName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string EquipmentModificationGuid { get; set; } = string.Empty;

    /// <summary>CurrencyType enum: 0=Invalid, 1=LaserTagTickets,
    /// 2=RecCenterTokens, 100=LostSkullsGold, 101=DraculaSilver,
    /// 200=RecRoyale_Season1.</summary>
    public int CurrencyType { get; set; } = 0;

    public int Currency { get; set; } = 0;
    public int Xp { get; set; } = 0;
    public int Level { get; set; } = 0;

    /// <summary>GiftContext enum (large, see Avatars.cs comments).
    /// 0 = Default, 100 = LevelUp, 1000 = Holiday, etc.</summary>
    public int GiftContext { get; set; } = 0;

    /// <summary>GiftRarity: -1=None, 0=Common, 10=Uncommon, 20=Rare,
    /// 30=Epic, 50=Legendary.</summary>
    public int GiftRarity { get; set; } = 0;

    [MaxLength(512)]
    public string Message { get; set; } = string.Empty;

    /// <summary>PlatformType: -1=All, 0=Steam, 1=Oculus, 2=PlayStation,
    /// 3=Microsoft, 4=HeadlessBot, 5=IOS.</summary>
    public int Platform { get; set; } = -1;

    /// <summary>Asset name string for the Unity Material that the
    /// gift box renders with.</summary>
    [MaxLength(128)]
    public string PackageMaterial { get; set; } = string.Empty;

    /// <summary>Asset name string for the GiftPackageVariant
    /// ScriptableObject (e.g. "Standard", "Holiday", etc.).</summary>
    [MaxLength(128)]
    public string PackageVariant { get; set; } = string.Empty;

    public bool Consumed { get; set; } = false;
    public bool IsValid { get; set; } = true;

    [MaxLength(512)]
    public string ErrorMessage { get; set; } = string.Empty;

    public bool SupportsCurrentPlatform { get; set; } = true;
    public bool IsGifted { get; set; } = false;

    /// <summary>Server-side audit timestamp; not part of wire
    /// shape.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
