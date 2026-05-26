using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

public class PlayerSettingEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Value { get; set; } = string.Empty;

    public PlayerEntity? Player { get; set; }
}
