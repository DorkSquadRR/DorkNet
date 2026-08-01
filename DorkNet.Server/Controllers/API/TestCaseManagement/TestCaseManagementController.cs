using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.TestCaseManagement;

/// <summary>
/// api.rec.net/api/testcasemanagement/* — internal QA tooling. URL
/// surface verified against
/// <c>Cpp2IL_ISIL/.../RecNet/TestCaseManagement.txt</c>:
///
///   GET  api/testcasemanagement/v1/testpasssummary  → List&lt;TestPass&gt;
///   GET  api/testcasemanagement/v1/testpass/{id}    → TestPass
///   GET  api/testcasemanagement/v1/testcase/{id}    → TestCase
///   POST api/testcasemanagement/v1/testcase/{id}/claim
///   POST api/testcasemanagement/v1/testcase/{id}/unclaim
///   POST api/testcasemanagement/v1/testcase/{id}/status
///
/// Wire DTOs match <c>RecNet.TestCase</c> + <c>RecNet.TestPass</c>
/// exactly:
///
/// • TestCase: <c>Id(string), Key, Title, Description, RoomName,
///   Status(TestCaseStatus enum 0=NotYetTested/1=Claimed/2=Failed/3=Passed),
///   MinNumAssignedPlayers, AssignedPlayerIds(List&lt;int&gt;),
///   AssignedPlayerNames, Tags, JiraUrl, JiraBugUrl</c>.
/// • TestPass: <c>Id(uint), Name, Description, StartDate, EndDate?,
///   WasManuallyClosed, TestCases(List&lt;TestCase&gt;), Tags,
///   NumTestCases, NumPassedTestCases, NumFailedTestCases</c>.
///
/// Routes are reachable by anyone (the watch only renders this tab
/// when the player has the developer role; client-side gate). The
/// claim/status mutations no-op if the caller isn't authenticated.
/// </summary>
[ApiController]
public class TestCaseManagementController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/testcasemanagement/v1/testpasssummary")]
    public async Task<IActionResult> TestPassSummaries()
    {
        var passes = await db.TestPasses.ToListAsync();
        var caseCountsByPass = await db.TestCases
            .Where(c => c.TestPassId != null)
            .GroupBy(c => c.TestPassId!.Value)
            .Select(g => new PassCounts(g.Key, g.Count(),
                g.Count(c => c.Status == 3), g.Count(c => c.Status == 2)))
            .ToDictionaryAsync(x => x.PassId);
        return Ok(passes.Select(p => ToWirePass(p, new List<TestCaseEntity>(), caseCountsByPass)));
    }

    [HttpGet("api/testcasemanagement/v1/testpass/{id:long}")]
    public async Task<IActionResult> TestPass(long id)
    {
        if (id < 0 || id > uint.MaxValue) return NotFound();
        var pid = (uint)id;
        var pass = await db.TestPasses.FirstOrDefaultAsync(p => p.Id == pid);
        if (pass is null) return NotFound();
        var cases = await db.TestCases.Where(c => c.TestPassId == pid).ToListAsync();
        var counts = new Dictionary<uint, PassCounts>
        {
            [pid] = new(pid, cases.Count, cases.Count(c => c.Status == 3), cases.Count(c => c.Status == 2)),
        };
        return Ok(ToWirePass(pass, cases, counts));
    }

    private record PassCounts(uint PassId, int Total, int Passed, int Failed);

    [HttpGet("api/testcasemanagement/v1/testcase/{id}")]
    public async Task<IActionResult> TestCase(string id)
    {
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id);
        if (tc is null) return NotFound();
        return Ok(ToWireCase(tc));
    }

    [HttpPost("api/testcasemanagement/v1/testcase/{id}/claim")]
    [Authorize]
    public async Task<IActionResult> Claim(string id)
    {
        var pid = this.RequireCurrentPlayerId();
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id);
        if (tc is null) return NotFound();

        var player = await db.Players.Where(p => p.Id == pid).Select(p => p.Username).FirstOrDefaultAsync();
        var ids = ParseIntList(tc.AssignedPlayerIdsJson);
        var names = ParseStringList(tc.AssignedPlayerNamesJson);
        if (!ids.Contains((int)pid))
        {
            ids.Add((int)pid);
            names.Add(player ?? $"Player {pid}");
            tc.AssignedPlayerIdsJson = JsonSerializer.Serialize(ids);
            tc.AssignedPlayerNamesJson = JsonSerializer.Serialize(names);
        }
        tc.Status = 1; // Claimed
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWireCase(tc));
    }

    [HttpPost("api/testcasemanagement/v1/testcase/{id}/unclaim")]
    [Authorize]
    public async Task<IActionResult> Unclaim(string id)
    {
        var pid = this.RequireCurrentPlayerId();
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id);
        if (tc is null) return NotFound();
        var ids = ParseIntList(tc.AssignedPlayerIdsJson);
        var names = ParseStringList(tc.AssignedPlayerNamesJson);
        var idx = ids.IndexOf((int)pid);
        if (idx >= 0)
        {
            ids.RemoveAt(idx);
            if (idx < names.Count) names.RemoveAt(idx);
            tc.AssignedPlayerIdsJson = JsonSerializer.Serialize(ids);
            tc.AssignedPlayerNamesJson = JsonSerializer.Serialize(names);
        }
        if (ids.Count == 0 && tc.Status == 1) tc.Status = 0; // back to NotYetTested
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWireCase(tc));
    }

    public sealed class StatusUpdateRequest
    {
        public int NewStatus { get; set; }
    }

    [HttpPost("api/testcasemanagement/v1/testcase/{id}/status")]
    [Authorize]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] StatusUpdateRequest req)
    {
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id);
        if (tc is null) return NotFound();
        if (req.NewStatus is < 0 or > 3) return BadRequest("invalid status");
        tc.Status = req.NewStatus;
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWireCase(tc));
    }

    /// <summary>POST <c>api/testcasemanagement/v1/testcase/{id}/comment</c> —
    /// attach a QA note to a test case.
    ///
    /// The client builds the URL by concatenating
    /// <c>"api/testcasemanagement/v1/testcase/"</c> + id + <c>"/comment"</c>
    /// (RecNet.Runtime/FLODOBMEJAO.txt:660, :658) and posts it with verb
    /// ordinal 2 (<c>Move rdx, 2</c> at :673). The issuing method is
    /// <c>LDGADANDBIO KNDOKAKBLCC(String, String)</c> (:575) — a status-only
    /// promise, so the client parses no response DTO and only needs a 2xx.
    ///
    /// Comments append to a JSON list on the case rather than overwriting, so
    /// a second tester's note never erases the first.</summary>
    [HttpPost("api/testcasemanagement/v1/testcase/{id}/comment")]
    [Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> AddComment(string id)
    {
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id);
        if (tc is null) return NotFound();

        var text = (await ReadCommentAsync())?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest("missing comment");
        if (text.Length > 2000) text = text[..2000];

        var comments = ParseStringList(tc.CommentsJson);
        comments.Add($"{this.RequireCurrentPlayerId()}|{DateTime.UtcNow:O}|{text.Replace('|', '/')}");
        tc.CommentsJson = JsonSerializer.Serialize(comments);
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>The comment text, from a form field, the query string, a JSON
    /// object, or a bare JSON string body — the exact spelling is not
    /// recoverable from the ISIL, so every plausible carrier is accepted.</summary>
    private async Task<string?> ReadCommentAsync()
    {
        string[] names = ["comment", "Comment", "text", "Text", "body", "Body", "message", "Message"];

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var n in names)
                if (form.TryGetValue(n, out var v) && v.Count > 0) return v.ToString();
        }
        foreach (var n in names)
            if (Request.Query.TryGetValue(n, out var q) && q.Count > 0) return q.ToString();

        if (!Request.HasFormContentType)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                Request.Body.Position = 0;
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String) return root.GetString();
                if (root.ValueKind == JsonValueKind.Object)
                    foreach (var n in names)
                        if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                            return v.GetString();
            }
            catch (JsonException) { /* no usable body */ }
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<int> ParseIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new(); }
        catch { return new(); }
    }

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static object ToWireCase(TestCaseEntity c) => new
    {
        c.Id,
        c.Key,
        c.Title,
        c.Description,
        c.RoomName,
        c.Status,
        c.MinNumAssignedPlayers,
        AssignedPlayerIds = ParseIntList(c.AssignedPlayerIdsJson),
        AssignedPlayerNames = ParseStringList(c.AssignedPlayerNamesJson),
        Tags = ParseStringList(c.TagsJson),
        c.JiraUrl,
        c.JiraBugUrl,
    };

    private static object ToWirePass(TestPassEntity p, List<TestCaseEntity> cases,
        IDictionary<uint, PassCounts> counts)
    {
        var c = counts.TryGetValue(p.Id, out var v) ? v : new PassCounts(p.Id, 0, 0, 0);
        return new
        {
            p.Id,
            p.Name,
            p.Description,
            p.StartDate,
            p.EndDate,
            p.WasManuallyClosed,
            TestCases = cases.Select(ToWireCase),
            Tags = ParseStringList(p.TagsJson),
            NumTestCases = c.Total,
            NumPassedTestCases = c.Passed,
            NumFailedTestCases = c.Failed,
        };
    }
}
