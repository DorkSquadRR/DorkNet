using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One row per (player, item) pair. Backs both the
/// <c>/api/equipment/v2/getUnlocked</c> and
/// <c>/api/consumables/v1/getUnlocked</c> endpoints — the type comes
/// from the joined <see cref="StoreItemEntity.Category"/>.
///
/// For equipment, <see cref="Quantity"/> is always 1 and
/// <see cref="IsActive"/> tracks whether it's equipped. For
/// consumables, <see cref="Quantity"/> is the stack size and
/// <see cref="IsActive"/> tracks whether the player has it favourited /
/// quick-equipped on the watch.
/// </summary>
public class PlayerInventoryEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>FK by string to <see cref="StoreItemEntity.Slug"/>.
    /// Stored as a string rather than an int FK because a player can
    /// be granted items that aren't in the live store (admin-gifted,
    /// legacy retired items, etc.).</summary>
    [MaxLength(128)]
    public string ItemSlug { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;
    public bool IsActive { get; set; } = false;

    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
}
