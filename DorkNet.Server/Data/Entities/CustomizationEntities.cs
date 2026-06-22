using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

public class CustomAvatarItemEntity
{
    public long Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public long CreatorPlayerId { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public int Price { get; set; } = 100;

    public int ItemType { get; set; } = 0;

    public int BaseAvatarItemId { get; set; }

    [MaxLength(64)]
    public string Color { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string AssetName { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    public bool IsFeatured { get; set; }

    public long CheerCount { get; set; }

    public long PurchaseCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CustomAvatarItemOwnershipEntity
{
    public long Id { get; set; }

    public long PlayerId { get; set; }

    public long CustomAvatarItemId { get; set; }

    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
}

public class ItemWishlistEntity
{
    public long Id { get; set; }

    public long PlayerId { get; set; }

    [MaxLength(128)]
    public string ItemKey { get; set; } = string.Empty;

    public int ItemType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class KeepsakeEntity
{
    public long Id { get; set; }

    public long PlayerId { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = "general";

    [MaxLength(128)]
    public string EventKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
}
