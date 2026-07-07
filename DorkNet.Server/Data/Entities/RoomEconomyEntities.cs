using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

public class RoomCurrencyEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long RoomId { get; set; }
    public long CreatorPlayerId { get; set; }
    [MaxLength(64)] public string Name { get; set; } = string.Empty;
    [MaxLength(512)] public string Description { get; set; } = string.Empty;
    [MaxLength(256)] public string ImageName { get; set; } = string.Empty;
    public int DailyLimit { get; set; } = 0;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RoomCurrencyBalanceEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public long RoomCurrencyId { get; set; }
    public long Balance { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RoomCurrencyPurchaseOfferEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long RoomCurrencyId { get; set; }
    [MaxLength(64)] public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
    public int Price { get; set; }
    public int CurrencyType { get; set; } = 2;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One creator-made consumable for sale in a room — the 2023 client's
/// in-room "shop" (RoomConsumablesManager). Wire identity is
/// <see cref="PublicId"/> ("RoomConsumableId" on the wire); the desc DTO
/// the client deserialises is {RoomConsumableId, RoomId, Name,
/// Description, ImageName, PriceAndCurrency:{Price, CurrencyId}}.
/// <see cref="CurrencyId"/> is null for token-priced items, otherwise
/// the <see cref="RoomCurrencyEntity.PublicId"/> of a room currency.
/// </summary>
public class RoomConsumableEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long RoomId { get; set; }
    public long CreatorPlayerId { get; set; }
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(1024)] public string Description { get; set; } = string.Empty;
    [MaxLength(256)] public string ImageName { get; set; } = string.Empty;
    public long Price { get; set; }
    public Guid? CurrencyId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One (player, room consumable) inventory stack. The client keeps a
/// per-item optimistic-concurrency token: purchase and consume requests
/// carry {CurrentConcurrencyCode, NewConcurrencyCode} and the server
/// stores the new code on success — the wire inventory row is
/// {RoomConsumableId, AccountId, Count, ConcurrencyCode, ModifiedAt,
/// Consumable:{desc}}. <c>Consumable</c> must NEVER be null: the 2023
/// client NREs in RoomConsumablesManager while processing the
/// room-join inventory fetch if it is.
/// </summary>
public class RoomConsumableOwnershipEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public long RoomConsumableId { get; set; }
    public int Count { get; set; }
    public Guid ConcurrencyCode { get; set; } = Guid.NewGuid();
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public class UgcPurchasableEntity
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long RoomId { get; set; }
    public long CreatorPlayerId { get; set; }
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(1024)] public string Description { get; set; } = string.Empty;
    [MaxLength(256)] public string ImageName { get; set; } = string.Empty;
    public int Price { get; set; }
    public int CurrencyType { get; set; } = 2;
    public int ItemType { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
