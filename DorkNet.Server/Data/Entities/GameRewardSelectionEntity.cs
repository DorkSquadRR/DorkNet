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
}
