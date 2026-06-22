using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A Maker-Pen creation that the player has saved as a reusable
/// "invention" — can be re-spawned in any room they enter, shared with
/// other players, or featured on the watch's Inventions tab. The
/// underlying SpawnableTemplateData protobuf bytes are uploaded via
/// <c>storage.rec.net/upload</c> with FileType=Invention (5).
/// </summary>
public class InventionEntity
{
    public long Id { get; set; }

    public long CreatorPlayerId { get; set; }

    /// <summary>Stable opaque GUID the client uses to deduplicate
    /// instantiated inventions across rooms (matches
    /// <c>Invention.ReplicationId</c>). Generated server-side at
    /// create time.</summary>
    [MaxLength(64)]
    public string ReplicationId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>Legacy permission column (private=0/friends=1/public=2)
    /// — kept for backward-compat with existing rows; new logic uses
    /// <see cref="GeneralPermission"/> + <see cref="CreatorPermission"/>
    /// (InventionPermission enum: Unassigned=0, LimitedOneUseOnly=10,
    /// UseOnly=20, EditAndSave=40, Publish=60, Charge=80,
    /// Unlimited=100).</summary>
    public int Permission { get; set; } = 0;

    /// <summary>InventionPermission value the creator gets when
    /// spawning their own creation (default Unlimited=100).</summary>
    public int CreatorPermission { get; set; } = 100;

    /// <summary>InventionPermission value other players get when
    /// spawning. Defaults Unassigned=0 (private).</summary>
    public int GeneralPermission { get; set; } = 0;

    /// <summary>True once <c>POST api/inventions/v2/publish</c> sets
    /// a non-Unassigned <see cref="GeneralPermission"/>. Stays true
    /// across re-edits — the wire field tracks "has this ever been
    /// published".</summary>
    public bool IsPublished { get; set; } = false;

    /// <summary>1-based latest version index. Bumped each time
    /// <c>POST api/inventions/v3/addversion</c> uploads new bytes.
    /// Mirrors <c>Invention.CurrentVersionNumber</c>.</summary>
    public int CurrentVersionNumber { get; set; } = 1;

    /// <summary>Set on first publish. Null until then.</summary>
    public DateTime? FirstPublishedAt { get; set; }

    /// <summary>If the creator was in a room when first saving,
    /// the originating room id for analytics.</summary>
    public long? CreationRoomId { get; set; }

    /// <summary>How many distinct players have spawned this
    /// invention in any room (rolling counter — increments on
    /// download).</summary>
    public int NumPlayersHaveUsedInRoom { get; set; } = 0;

    /// <summary>True for AG (community-built) inventions, false for
    /// "Rec Room Original" / staff-baked ones.</summary>
    public bool IsAgInvention { get; set; } = true;

    /// <summary>Most recent uploaded blob name. Format
    /// <c>invention_<id>_v<n>.dat</c>; the cdn catch-all serves the
    /// matching <see cref="RoomDataBlobEntity"/> bytes.</summary>
    [MaxLength(128)]
    public string CurrentBlobName { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string TagsCsv { get; set; } = string.Empty;

    public int CheerCount { get; set; } = 0;
    public int SpawnCount { get; set; } = 0;
    public int Price { get; set; } = 0;

    /// <summary>Soft-delete flag. When set, the row is hidden from
    /// browse / search / detail endpoints but stays in the table so
    /// existing references (cheers, reports) keep resolving and the
    /// blob bytes can be restored if needed.</summary>
    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
