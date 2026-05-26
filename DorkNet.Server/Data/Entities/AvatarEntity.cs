using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-player avatar state. The 2020 client persists a mix of structured
/// and free-form blobs:
/// <list type="bullet">
///   <item><c>OutfitSelections</c> — comma-separated GUIDs of equipped
///       items per slot (head, torso, legs, feet, accessories). Sent
///       verbatim to <c>POST api/avatar/v2</c>.</item>
///   <item><c>FaceFeatures</c> — JSON blob with eyeId/eyePos/eyeScl/
///       mouthId etc. produced by the in-game face editor.</item>
///   <item><c>HairColor</c> / <c>SkinColor</c> — color vault GUIDs from
///       the client's baked hair/skin color tables.</item>
///   <item><c>SavedOutfitsJson</c> — list of named outfit presets the
///       watch's Backpack tab shows under "saved".</item>
///   <item><c>EquippedItemsJson</c> / <c>InventoryJson</c> — kept for the
///       v3 wire shape (list of <see cref="DorkNet.Models.Avatar.AvatarItemInstance"/>).</item>
/// </list>
/// </summary>
public class AvatarEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    // JSON-serialised list of equipped item IDs (v3 shape)
    public string EquippedItemsJson { get; set; } = "[]";

    // JSON-serialised full inventory (v3 shape)
    public string InventoryJson { get; set; } = "[]";

    /// <summary>v2 outfit selections — comma-separated GUIDs per slot.</summary>
    [MaxLength(2048)]
    public string OutfitSelections { get; set; } = string.Empty;

    /// <summary>v2 face features — opaque JSON written by the face
    /// editor; we never parse it server-side, just round-trip.</summary>
    [MaxLength(8192)]
    public string FaceFeatures { get; set; } = string.Empty;

    [MaxLength(64)]
    public string HairColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SkinColor { get; set; } = string.Empty;

    /// <summary>Saved outfit presets — JSON list of named slots so the
    /// watch's "Saved" tab can recall them.</summary>
    public string SavedOutfitsJson { get; set; } = "[]";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PlayerEntity? Player { get; set; }
}
