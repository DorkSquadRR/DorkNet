using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-player objective / achievement progress. The 2020 client's
/// <c>Objectives.MyProgress</c> response is two arrays — Objectives
/// and ObjectiveGroups — keyed by string id. We collapse that to one
/// table here; group rows distinguish themselves with a leading
/// <c>group:</c> prefix on <see cref="Key"/>.
/// </summary>
public class ObjectiveProgressEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>Objective id from the client's hardcoded objective
    /// table (e.g. <c>tutorial_dorm_decorate</c>). Group rows use
    /// <c>group:&lt;groupId&gt;</c>.</summary>
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }
    public DateTime? ClearedAt { get; set; }
}
