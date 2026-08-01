using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.TestCaseManagement;

/// <summary>
/// Links QA test cases to GitHub issues.
///
/// <para>These routes are NOT part of the 2023 client's surface — the in-game
/// QA tab never calls them. They exist for the admin tooling, so they are
/// admin-gated, unlike the client-facing routes in
/// <see cref="TestCaseManagementController"/> which are deliberately open
/// (the watch gates that tab client-side on the developer role).</para>
///
/// <para>The issue URL is stored in <see cref="TestCaseEntity.JiraBugUrl"/> —
/// the field Rec Room's own tooling used for exactly this, a link to the bug
/// filed against a failing case. Reusing it means no schema change, and on
/// this server that matters more than the name: the Postgres path is
/// EnsureCreated-only and never replays migrations, so every new column has to
/// be added twice or it is missing in production. A field that already exists
/// everywhere has neither problem, and the admin UI already renders it.</para>
/// </summary>
[ApiController]
[AdminOnly]
public class TestCaseIssuesController(
    DorkNetDbContext db,
    IGitHubIssues github,
    ILogger<TestCaseIssuesController> log) : ControllerBase
{
    /// <summary>Test-case status values, matching the client's
    /// <c>TestCaseStatus</c> enum.</summary>
    private const int StatusFailed = 2;
    private const int StatusPassed = 3;


    /// <summary>GET <c>api/admin/v1/testcases</c> — every test case with its
    /// issue link, newest pass first. Optional <c>?passId=</c> narrows to one
    /// test pass and <c>?status=</c> to one status.
    ///
    /// Exists because the admin SPA speaks <c>/api/admin/v1</c> exclusively and
    /// the client-facing test-case routes are shaped for the in-game QA tab
    /// (per-pass, no issue fields).</summary>
    [HttpGet("api/admin/v1/testcases")]
    public async Task<IActionResult> ListCases(
        [FromQuery] uint? passId, [FromQuery] int? status, CancellationToken ct)
    {
        var query = db.TestCases.AsQueryable();
        if (passId is uint pass) query = query.Where(c => c.TestPassId == pass);
        if (status is int s) query = query.Where(c => c.Status == s);

        var cases = await query
            .OrderByDescending(c => c.TestPassId)
            .ThenBy(c => c.Key)
            .Take(500)
            .ToListAsync(ct);

        return Ok(new
        {
            githubConfigured = github.IsConfigured,
            repository = github.Repository,
            cases = cases.Select(c => new
            {
                c.Id,
                c.Key,
                c.Title,
                c.RoomName,
                c.Status,
                c.TestPassId,
                issueUrl = c.JiraBugUrl,
                issueNumber = GitHubIssueService.IssueNumberFromUrl(c.JiraBugUrl),
                c.UpdatedAt,
            }).ToList(),
        });
    }

    /// <summary>POST <c>api/testcasemanagement/v1/testcase/{id}/issue</c> —
    /// file a GitHub issue for a test case and link it.
    ///
    /// Idempotent: a case that already carries a live issue link returns that
    /// issue rather than filing a duplicate. Re-running a sweep over a failing
    /// pass is therefore safe, which is the whole point — the alternative is a
    /// tester filing the same bug on every run.</summary>
    [HttpPost("api/testcasemanagement/v1/testcase/{id}/issue")]
    [HttpPost("api/admin/v1/testcases/{id}/issue")]
    public async Task<IActionResult> CreateIssue(string id, CancellationToken ct)
    {
        if (!github.IsConfigured)
            return Problem(
                "GitHub issue linking is not configured. Set GitHub:Token and GitHub:Repository.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (tc is null) return NotFound();

        if (GitHubIssueService.IssueNumberFromUrl(tc.JiraBugUrl) is int existingNumber)
        {
            var existing = await github.GetAsync(existingNumber, ct);
            // A link to an issue that no longer exists (deleted, or the repo
            // changed) must not block filing a new one.
            if (existing is not null)
                return Ok(Describe(tc, existing, created: false));
        }

        var issue = await github.CreateAsync(TitleFor(tc), BodyFor(tc), LabelsFor(tc), ct);
        if (issue is null)
            return Problem("GitHub rejected the issue.", statusCode: StatusCodes.Status502BadGateway);

        tc.JiraBugUrl = issue.Url;
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "[testcase-issue] filed {Repo}#{Number} for case {Key} ({Id})",
            github.Repository, issue.Number, tc.Key, tc.Id);
        return Ok(Describe(tc, issue, created: true));
    }

    /// <summary>DELETE <c>api/testcasemanagement/v1/testcase/{id}/issue</c> —
    /// unlink, leaving the issue itself alone. Closing someone's issue as a
    /// side effect of tidying a QA link would be the wrong call.</summary>
    [HttpDelete("api/testcasemanagement/v1/testcase/{id}/issue")]
    [HttpDelete("api/admin/v1/testcases/{id}/issue")]
    public async Task<IActionResult> UnlinkIssue(string id, CancellationToken ct)
    {
        var tc = await db.TestCases.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (tc is null) return NotFound();

        var previous = tc.JiraBugUrl;
        tc.JiraBugUrl = string.Empty;
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("[testcase-issue] unlinked {Url} from case {Key}", previous, tc.Key);
        return Ok(new { tc.Id, tc.Key, unlinked = previous });
    }

    /// <summary>POST <c>api/testcasemanagement/v1/issues/sync</c> — reconcile
    /// every linked case against its issue.
    ///
    /// The state a test case should be in follows the issue, in one direction
    /// only:
    ///
    /// <list type="bullet">
    /// <item>Issue CLOSED and the case is Failed → Passed. A closed issue means
    /// the bug is fixed, and the case that was failing on it therefore passes.</item>
    /// <item>Issue REOPENED and the case is Passed → Failed. The bug came back,
    /// so the close transition above is undone.</item>
    /// </list>
    ///
    /// Nothing else moves. Only these two states are the reconciler's to
    /// change, so a case a tester has Claimed, or one nobody has run yet, is
    /// never rewritten underneath them.</summary>
    [HttpPost("api/testcasemanagement/v1/issues/sync")]
    [HttpPost("api/admin/v1/testcases/issues/sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        if (!github.IsConfigured)
            return Problem(
                "GitHub issue linking is not configured. Set GitHub:Token and GitHub:Repository.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var changed = await ReconcileAsync(db, github, log, ct);
        return Ok(new { reconciled = changed.Count, changes = changed });
    }

    /// <summary>Shared by the endpoint and the background reconciler so the two
    /// can never drift apart.</summary>
    internal static async Task<List<object>> ReconcileAsync(
        DorkNetDbContext db, IGitHubIssues github, ILogger log, CancellationToken ct)
    {
        var linked = await db.TestCases
            .Where(c => c.JiraBugUrl != string.Empty)
            .ToListAsync(ct);

        var changes = new List<object>();
        foreach (var tc in linked)
        {
            if (ct.IsCancellationRequested) break;
            if (GitHubIssueService.IssueNumberFromUrl(tc.JiraBugUrl) is not int number) continue;

            var issue = await github.GetAsync(number, ct);
            if (issue is null) continue;

            var closed = issue.State.Equals("closed", StringComparison.OrdinalIgnoreCase);
            // Closed means fixed, so a case failing on that bug now passes.
            // Only ever move between those two states: reopening undoes the
            // close and nothing else. "Open issue and the case isn't Failed"
            // would also catch a case a tester has just Claimed, or one nobody
            // has run yet, and mark it Failed underneath them.
            int? next = (closed, tc.Status) switch
            {
                (true, StatusFailed) => StatusPassed,
                (false, StatusPassed) => StatusFailed,
                _ => null,
            };
            if (next is not int status) continue;

            var from = tc.Status;
            tc.Status = status;
            tc.UpdatedAt = DateTime.UtcNow;
            changes.Add(new { tc.Id, tc.Key, issue = number, from, to = status });
            log.LogInformation(
                "[testcase-issue] case {Key} status {From} -> {To} (issue #{Number} is {State})",
                tc.Key, from, status, number, issue.State);
        }

        if (changes.Count > 0) await db.SaveChangesAsync(ct);
        return changes;
    }

    private object Describe(TestCaseEntity tc, GitHubIssue issue, bool created) => new
    {
        tc.Id,
        tc.Key,
        tc.Title,
        tc.Status,
        issue = new { issue.Number, issue.Url, issue.State },
        created,
    };

    private static string TitleFor(TestCaseEntity tc) =>
        string.IsNullOrWhiteSpace(tc.Key) ? tc.Title : $"[{tc.Key}] {tc.Title}";

    /// <summary>The issue body. Written for whoever picks the bug up, so it
    /// leads with what to do rather than with metadata.</summary>
    private static string BodyFor(TestCaseEntity tc)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(tc.Description))
        {
            lines.Add(tc.Description.Trim());
            lines.Add("");
        }

        lines.Add("| | |");
        lines.Add("| --- | --- |");
        lines.Add($"| Test case | `{tc.Key}` |");
        if (!string.IsNullOrWhiteSpace(tc.RoomName)) lines.Add($"| Room | {tc.RoomName} |");
        if (tc.TestPassId is uint pass) lines.Add($"| Test pass | {pass} |");
        if (!string.IsNullOrWhiteSpace(tc.JiraUrl)) lines.Add($"| Test case link | {tc.JiraUrl} |");

        var comments = ParseComments(tc.CommentsJson);
        if (comments.Count > 0)
        {
            lines.Add("");
            lines.Add("### Tester notes");
            foreach (var comment in comments) lines.Add($"- {comment}");
        }

        lines.Add("");
        lines.Add("<sub>Filed from DorkNet QA test-case management.</sub>");
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> LabelsFor(TestCaseEntity tc)
    {
        yield return "qa";
        foreach (var tag in ParseComments(tc.TagsJson)) yield return tag;
    }

    /// <summary>Both CommentsJson and TagsJson are JSON string arrays. A
    /// comment is stored as <c>playerId|timestamp|text</c>; only the text is
    /// worth putting in an issue.</summary>
    private static List<string> ParseComments(string json)
    {
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return items
                .Select(item =>
                {
                    var parts = item.Split('|', 3);
                    return (parts.Length == 3 ? parts[2] : item).Trim();
                })
                .Where(text => text.Length > 0)
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
