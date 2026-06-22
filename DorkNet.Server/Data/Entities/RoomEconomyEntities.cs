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
