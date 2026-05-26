using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One historical version of an <see cref="InventionEntity"/>. Each
/// time the creator uploads a new SpawnableTemplateData blob via
/// <c>POST api/inventions/v3/addversion</c>, we snapshot the prior
/// blob name into a new row here so the watch's "version history"
/// tab and `GET api/inventions/v1/versions?inventionId=X` can list
/// them. Indexed on (InventionId, VersionNumber DESC) for the
/// common "show me the version list" query.
/// </summary>
public class InventionVersionEntity
{
    public long Id { get; set; }
    public long InventionId { get; set; }

    /// <summary>Mirrors <c>InventionVersion.ReplicationId</c> — opaque
    /// GUID the client uses to deduplicate spawns of this specific
    /// version across rooms.</summary>
    [MaxLength(64)]
    public string ReplicationId { get; set; } = string.Empty;

    /// <summary>1-based version index. Auto-assigned to
    /// max(VersionNumber) + 1 on insert.</summary>
    public int VersionNumber { get; set; }

    [MaxLength(128)]
    public string BlobName { get; set; } = string.Empty;

    /// <summary>Tokens it costs to spawn this version of the
    /// invention (decompiled wire field
    /// <c>InventionVersion.InstantiationCost</c>).</summary>
    public int InstantiationCost { get; set; } = 0;

    /// <summary>Watt budget the creation consumes
    /// (<c>InventionVersion.LightsCost</c>).</summary>
    public int LightsCost { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
