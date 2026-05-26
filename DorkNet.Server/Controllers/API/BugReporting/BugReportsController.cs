using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.BugReporting;

/// <summary>
/// api.rec.net/api/bugreporting/* — in-game "Report a Bug" UI.
/// Wire request DTO matches <c>RecNet.BugReporting+BugReportDTO</c>
/// (<c>Cpp2IL_CS/.../RecNet/BugReporting.cs</c>): <c>Summary,
/// Description, TestCaseKey, BuildVersion, BuildTimestamp,
/// BundleVersionCode</c>.
///
/// Plus <c>POST /api/bugreporting/v1/submit</c> (legacy v1 path) and
/// <c>POST /api/bugreporting/v2/submit</c> (renamed v2). The
/// <c>ReportBug</c> client API also includes screenshot + log byte
/// arrays as multipart parts; we accept-and-discard those (no
/// storage flow yet) but persist the text fields.
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

    [HttpPost("api/bugreporting/v1/submit")]
    [HttpPost("api/bugreporting/v2/submit")]
    [HttpPost("api/bugreporting/v1/reportbug")]
    [HttpPost("api/bugreporting/v2/reportbug")]
    [Consumes("application/json")]
    public async Task<IActionResult> SubmitJson([FromBody] BugReportRequest req)
        => await PersistAsync(req);

    /// <summary>Multipart form variant — the watch posts screenshot
    /// + log as separate parts. We pull the text-only fields and
    /// drop the binary attachments.</summary>
    [HttpPost("api/bugreporting/v1/submit-multipart")]
    [HttpPost("api/bugreporting/v2/reportbug-multipart")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitMultipart(
        [FromForm(Name = "Summary")] string? summary,
        [FromForm(Name = "Description")] string? description,
        [FromForm(Name = "TestCaseKey")] string? testCaseKey,
        [FromForm(Name = "BuildVersion")] string? buildVersion,
        [FromForm(Name = "BuildTimestamp")] long? buildTimestamp)
        => await PersistAsync(new BugReportRequest
        {
            Summary = summary,
            Description = description,
            TestCaseKey = testCaseKey,
            BuildVersion = buildVersion,
            BuildTimestamp = buildTimestamp,
        });

    private async Task<IActionResult> PersistAsync(BugReportRequest req)
    {
        var pid = Me;
        var summary = (req.Summary ?? string.Empty).Trim();
        var body = (req.Description ?? string.Empty).Trim();
        if (summary.Length == 0 && body.Length == 0)
            return BadRequest(new { Error = "summary or description required" });

        db.BugReports.Add(new BugReportEntity
        {
            ReporterPlayerId = pid,
            Title = summary[..Math.Min(128, summary.Length)],
            Body = body[..Math.Min(4000, body.Length)],
            ClientVersion = req.BuildVersion ?? string.Empty,
            Platform = HttpContext.Request.Headers.UserAgent.ToString()[..Math.Min(32, HttpContext.Request.Headers.UserAgent.ToString().Length)],
            Category = string.IsNullOrEmpty(req.TestCaseKey) ? "bug" : $"bug:{req.TestCaseKey}",
        });
        await db.SaveChangesAsync();
        return Ok(new { Submitted = true });
    }
}
