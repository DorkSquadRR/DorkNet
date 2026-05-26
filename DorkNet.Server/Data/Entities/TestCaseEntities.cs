using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// QA test case — wire type <c>RecNet.TestCase</c>
/// (<c>Cpp2IL_CS/.../RecNet/TestCase.cs</c>). The 2020 Rec Room
/// client uses this to surface JIRA-tracked test cases in an
/// internal QA tab; users with the developer role claim a case,
/// run it in the named room, then submit a Pass/Fail status.
///
/// Field names + types match the client's
/// <c>TestCase.Deserialize</c> exactly. <see cref="Status"/> is the
/// <c>TestCaseStatus</c> enum: 0=NotYetTested, 1=Claimed, 2=Failed,
/// 3=Passed.
/// </summary>
public class TestCaseEntity
{
    /// <summary>EF primary key (long autoincrement) — kept separate
    /// from the wire <see cref="Id"/> string so we can index cleanly.</summary>
    public long Pk { get; set; }

    /// <summary>Wire field <c>Id</c> (string) — typically the JIRA
    /// issue key like <c>RR-1234</c>.</summary>
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(128)]
    public string RoomName { get; set; } = string.Empty;

    /// <summary>TestCaseStatus enum: 0=NotYetTested, 1=Claimed,
    /// 2=Failed, 3=Passed.</summary>
    public int Status { get; set; } = 0;

    public int MinNumAssignedPlayers { get; set; } = 1;

    /// <summary>JSON list of player ids currently claimed on this
    /// test case. Mirrors wire <c>AssignedPlayerIds</c> (List&lt;int&gt;).</summary>
    public string AssignedPlayerIdsJson { get; set; } = "[]";

    /// <summary>Sibling JSON list of names for display (kept in sync
    /// with <see cref="AssignedPlayerIdsJson"/>).</summary>
    public string AssignedPlayerNamesJson { get; set; } = "[]";

    /// <summary>JSON list of tag strings.</summary>
    public string TagsJson { get; set; } = "[]";

    [MaxLength(512)]
    public string JiraUrl { get; set; } = string.Empty;

    [MaxLength(512)]
    public string JiraBugUrl { get; set; } = string.Empty;

    /// <summary>Optional FK to the <see cref="TestPassEntity"/> this
    /// case belongs to (null = orphan / standalone).</summary>
    public uint? TestPassId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Wire type <c>RecNet.TestPass</c> — a versioned bundle of
/// test cases the QA team groups for a release pass. Listed via
/// <c>GET api/testcasemanagement/v1/testpasssummary</c>; one fetched
/// in detail via <c>GET api/testcasemanagement/v1/testpass/{id}</c>.
/// </summary>
public class TestPassEntity
{
    /// <summary>Wire <c>Id</c> is uint — store as long PK and cast.</summary>
    public uint Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the pass is still active.</summary>
    public DateTime? EndDate { get; set; }

    public bool WasManuallyClosed { get; set; } = false;

    /// <summary>JSON list of tag strings.</summary>
    public string TagsJson { get; set; } = "[]";
}
