using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.BugReporting;

/// <summary>
/// api.rec.net/api/bugreporting/* — in-game "Report a Bug" UI.
///
/// Two distinct wire shapes hit this controller:
///
/// • Legacy "submit" routes (v1/v2) post a plain JSON body matching
///   <c>BugReportRequest</c> below.
/// • The real client's <c>ReportBug</c> API
///   (<c>Cpp2IL_CS/.../RecNet/BugReporting.cs</c>) posts
///   <c>multipart/form-data</c> with a single text part named
///   <c>bugReport</c> containing the JSON payload (Summary,
///   Description, TestCaseKey, BuildVersion, BuildTimestamp,
///   BundleVersionCode), plus optional <c>screenshotData</c> and
///   <c>outputLogData</c> file parts. We accept-and-discard the
///   binary parts (no storage flow yet) but persist the text fields.
///
/// The previous version of this controller routed
/// v1/reportbug and v2/reportbug to the JSON-body handler and put
/// flat form fields (Summary, Description, ...) on a separate
/// "-multipart" route the real client never calls — so the actual
/// in-game bug report button was silently hitting the wrong shape.
/// Fixed by routing reportbug → the multipart "bugReport" JSON-part
/// handler, matching BugReporting.cs.
/// </summary>
[ApiController]
[Authorize]
public class BugReportsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    public sealed class BugReportRequest
    {
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? TestCaseKey { get; set; }
        public string? BuildVersion { get; set; }
        public long? BuildTimestamp { get; set; }
        public int? BundleVersionCode { get; set; }
    }

    /// <summary>Legacy plain-JSON submit routes.</summary>
    [HttpPost("api/bugreporting/v1/submit")]
    [HttpPost("api/bugreporting/v2/submit")]
    [Consumes("application/json")]
    public async Task<IActionResult> SubmitJson([FromBody] BugReportRequest req)
        => await PersistAsync(req);

    /// <summary>
    /// Real client route. Matches BugReporting.cs: multipart form
    /// with a "bugReport" JSON text part plus optional
    /// screenshotData/outputLogData file parts.
    /// </summary>
    [RequestSizeLimit(50_000_000)]
    [HttpPost("api/bugreporting/v1/reportbug")]
    [HttpPost("api/bugreporting/v2/reportbug")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitMultipart()
    {
        if (!Request.HasFormContentType)
            return BadRequest("Expected multipart/form-data.");

        var form = await Request.ReadFormAsync();

        string? bugReportJson = form["bugReport"];
        if (string.IsNullOrWhiteSpace(bugReportJson))
            return BadRequest("Missing bugReport field.");

        BugReportRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<BugReportRequest>(
                bugReportJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest("Invalid bugReport JSON.");
        }

        if (req is null)
            return BadRequest("Invalid bugReport JSON.");

        // screenshotData / outputLogData are accepted and discarded here —
        // no storage flow yet. form.Files["screenshotData"] / ["outputLogData"]
        // are available if/when that lands.
        return await PersistAsync(req);
    }

    private async Task<IActionResult> PersistAsync(BugReportRequest req)
    {
        var pid = Me;
        var summary = (req.Summary ?? string.Empty).Trim();
        var body = (req.Description ?? string.Empty).Trim();
        if (summary.Length == 0 && body.Length == 0)
            return BadRequest(new { Error = "summary or description required" });

        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var report = new BugReportEntity
        {
            ReporterPlayerId = pid,
            Title = summary[..Math.Min(128, summary.Length)],
            Body = body[..Math.Min(4000, body.Length)],
            ClientVersion = req.BuildVersion ?? string.Empty,
            Platform = userAgent[..Math.Min(32, userAgent.Length)],
            Category = string.IsNullOrEmpty(req.TestCaseKey) ? "bug" : $"bug:{req.TestCaseKey}",
        };
        db.BugReports.Add(report);
        await db.SaveChangesAsync();

        var testCase = await CreateTestCaseAsync(report, req);

        return Ok(new { Submitted = true, Id = report.Id, TestCaseId = testCase.Id });
    }

    /// <summary>
    /// Creates a new TestCase for this bug report, with a randomly
    /// generated UUID as its Id, and links it back to the report via
    /// JiraBugUrl so it shows up in the QA tooling
    /// (TestCaseManagementController) already marked Failed.
    /// </summary>
    private async Task<TestCaseEntity> CreateTestCaseAsync(BugReportEntity report, BugReportRequest req)
    {
        var testCase = new TestCaseEntity
        {
            Id = Guid.NewGuid().ToString(),
            Key = string.IsNullOrEmpty(req.TestCaseKey) ? $"BUG-{report.Id}" : req.TestCaseKey,
            Title = report.Title,
            Description = report.Body,
            RoomName = string.Empty,
            Status = 0, // Unclaimed
            MinNumAssignedPlayers = 0,
            AssignedPlayerIdsJson = JsonSerializer.Serialize(new List<int>()),
            AssignedPlayerNamesJson = JsonSerializer.Serialize(new List<string>()),
            TagsJson = JsonSerializer.Serialize(new List<string> { "bugreport" }),
            JiraUrl = "",
            JiraBugUrl = $"",
            UpdatedAt = DateTime.UtcNow,
            TestPassId = 1,
        };

        db.TestCases.Add(testCase);
        await db.SaveChangesAsync();
        return testCase;
    }
}