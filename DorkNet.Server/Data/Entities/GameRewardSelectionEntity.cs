using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

public class GameRewardSelectionEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public int RewardType { get; set; }
    public int GiftContext { get; set; }
    [MaxLength(256)] public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SelectedAt { get; set; }
    public int? SelectedGiftDropId { get; set; }

    /// <summary>The three store-item ids offered as the choose-1-of-3
    /// reward. Persisted so re-fetching the pending reward is stable and
    /// <c>/select</c> can grant exactly what was shown. 0 = empty slot.</summary>
    public long Offer1ItemId { get; set; }
    public long Offer2ItemId { get; set; }
    public long Offer3ItemId { get; set; }

    /// <summary>Set once the chosen item has been granted to inventory, so
    /// a replayed <c>/select</c> can't grant twice.</summary>
    public long GrantedItemId { get; set; }
}
