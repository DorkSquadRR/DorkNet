using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Paid room access key authored by a room owner/co-owner. The watch
/// displays these in the room-key editor and purchase flow.
/// </summary>
public class RoomKeyEntity
{
    public long Id { get; set; }
    [MaxLength(64)] public string ReplicationId { get; set; } = Guid.NewGuid().ToString("D");
    public long RoomId { get; set; }
    public long CreatorPlayerId { get; set; }
    [MaxLength(40)] public string Name { get; set; } = string.Empty;
    [MaxLength(174)] public string Description { get; set; } = string.Empty;
    public int Price { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One purchased room key per player.</summary>
public class RoomKeyPurchaseEntity
{
    public long Id { get; set; }
    public long RoomKeyId { get; set; }
    public long PlayerId { get; set; }
    public int PaidPrice { get; set; }
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
}
