using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Tip shown on the dorm-load splash. Served via
/// <c>cdn.{rec.net,localhost}/config/LoadingScreenTipData</c>; edited via
/// the admin SPA. Wire shape per <c>RecNet/LoadingScreenTip.cs</c>
/// requires Title/Message/ImageName + Context/PlatformMask/HasImage
/// flags + an optional RoomNames list (scopes the tip to specific
/// rooms).
/// </summary>
public class LoadingScreenTipEntity
{
    public long Id { get; set; }

    [MaxLength(128)] public string Title { get; set; } = string.Empty;
    [MaxLength(512)] public string Message { get; set; } = string.Empty;

    /// <summary>Optional uploaded image (RoomDataBlob filename). Empty
    /// when the tip is text-only; CdnController sets
    /// <c>HasImage</c> in the wire payload based on this.</summary>
    [MaxLength(128)] public string ImageName { get; set; } = string.Empty;

    /// <summary>RecNet LoadingScreenContext enum (0 = Any). Controls
    /// which loading screens the tip is eligible for.</summary>
    public int Context { get; set; } = 0;

    /// <summary>PlatformMask bitfield (-1 = every platform). Used by
    /// the watch to filter out tips that reference platform-specific
    /// features (e.g. mobile-only).</summary>
    public int PlatformMask { get; set; } = -1;

    /// <summary>CSV of room names this tip is scoped to. Empty = show
    /// in any room's loading screen. Example: "DormRoom,MakerRoom".</summary>
    [MaxLength(512)] public string RoomNamesCsv { get; set; } = string.Empty;

    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
