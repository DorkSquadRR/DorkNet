using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DorkNet.Server.Tests;

/// <summary>
/// QA test cases linked to GitHub issues.
///
/// The link lives in <see cref="TestCaseEntity.JiraBugUrl"/> — the field Rec
/// Room's own tooling used for the bug filed against a failing case — so no
/// column is added and the Postgres "migrations never replay" trap is avoided
/// entirely.
///
/// GitHub itself is faked. These tests are about our behaviour around it:
/// filing once rather than per sweep, following issue state in the one
/// direction that is safe, and degrading rather than 500ing when no token is
/// configured.
/// </summary>
public sealed class TestCaseGitHubIssueTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public TestCaseGitHubIssueTests(DorkNetServerFactory factory) => _factory = factory;

    private const int StatusNotYetTested = 0;
    private const int StatusClaimed = 1;
    private const int StatusFailed = 2;

    [Fact]
    public async Task Filing_is_idempotent_and_stores_the_issue_url()
    {
        var github = new FakeGitHub();
        using var factory = WithGitHub(github);
        using var client = await AdminClientAsync(factory);
        var caseId = await SeedCaseAsync(factory, StatusFailed);

        using var first = await client.PostAsync($"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, $"first file -> {(int)first.StatusCode}: {firstBody}");
        Assert.True(JsonDocument.Parse(firstBody).RootElement.GetProperty("created").GetBoolean());

        // The link must be persisted, and in the field the admin UI reads.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var tc = await db.TestCases.SingleAsync(c => c.Id == caseId);
            Assert.Contains("/issues/", tc.JiraBugUrl);
        }

        // Re-running a sweep over a failing pass must not file the bug again.
        // Measured as a DELTA rather than an absolute count: the fake is shared
        // with whatever else the server does during the test, and the property
        // under test is "this call filed nothing", not "nothing was ever filed".
        var filedBefore = github.Created;
        using var second = await client.PostAsync($"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(second.IsSuccessStatusCode, $"second file -> {(int)second.StatusCode}: {secondBody}");
        Assert.False(JsonDocument.Parse(secondBody).RootElement.GetProperty("created").GetBoolean(),
            "filing twice created a duplicate issue");
        Assert.Equal(filedBefore, github.Created);
    }

    [Fact]
    public async Task A_closed_issue_sends_the_case_back_for_retesting_not_to_passed()
    {
        var github = new FakeGitHub();
        using var factory = WithGitHub(github);
        using var client = await AdminClientAsync(factory);
        var caseId = await SeedCaseAsync(factory, StatusFailed);

        using var filed = await client.PostAsync($"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);
        Assert.True(filed.IsSuccessStatusCode);

        github.CloseAll();
        using var sync = await client.PostAsync("/api/testcasemanagement/v1/issues/sync", null);
        Assert.True(sync.IsSuccessStatusCode, $"sync -> {(int)sync.StatusCode}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var tc = await db.TestCases.SingleAsync(c => c.Id == caseId);

        // Closing an issue is a developer saying the fix landed — not a tester
        // saying the case passes. It goes back in the queue, not to Passed.
        Assert.Equal(StatusNotYetTested, tc.Status);
    }

    [Fact]
    public async Task A_reopened_issue_marks_the_case_failed_again()
    {
        var github = new FakeGitHub();
        using var factory = WithGitHub(github);
        using var client = await AdminClientAsync(factory);
        var caseId = await SeedCaseAsync(factory, StatusFailed);

        using var filed = await client.PostAsync($"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);
        Assert.True(filed.IsSuccessStatusCode);

        github.CloseAll();
        using var closeSync = await client.PostAsync("/api/testcasemanagement/v1/issues/sync", null);
        Assert.True(closeSync.IsSuccessStatusCode);

        github.ReopenAll();
        using var reopenSync = await client.PostAsync("/api/testcasemanagement/v1/issues/sync", null);
        Assert.True(reopenSync.IsSuccessStatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        Assert.Equal(StatusFailed, (await db.TestCases.SingleAsync(c => c.Id == caseId)).Status);
    }

    [Fact]
    public async Task A_sweep_does_not_stomp_a_tester_mid_claim()
    {
        var github = new FakeGitHub();
        using var factory = WithGitHub(github);
        using var client = await AdminClientAsync(factory);
        var caseId = await SeedCaseAsync(factory, StatusFailed);

        using var filed = await client.PostAsync($"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);
        Assert.True(filed.IsSuccessStatusCode);

        // Someone picks the case up while the issue is still open.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.TestCases.SingleAsync(c => c.Id == caseId)).Status = StatusClaimed;
            await db.SaveChangesAsync();
        }

        using var sync = await client.PostAsync("/api/testcasemanagement/v1/issues/sync", null);
        Assert.True(sync.IsSuccessStatusCode);

        await using var check = factory.Services.CreateAsyncScope();
        var db2 = check.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var tc = await db2.TestCases.SingleAsync(c => c.Id == caseId);

        // An open issue against a claimed case says nothing new; flipping it to
        // Failed would yank it out from under whoever is testing it.
        Assert.Equal(StatusClaimed, tc.Status);
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_says_so_instead_of_failing()
    {
        // The default fixture has no GitHub configuration, which is what an
        // ordinary deployment looks like.
        using var client = await AdminClientAsync(_factory);
        var caseId = await SeedCaseAsync(_factory, StatusFailed);

        using var response = await client.PostAsync(
            $"/api/testcasemanagement/v1/testcase/{caseId}/issue", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private WebApplicationFactory<Program> WithGitHub(IGitHubIssues github) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitHubIssues>();
                services.AddSingleton(github);
            }));

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == session.PlayerId)).IsAdmin = true;
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<string> SeedCaseAsync(WebApplicationFactory<Program> factory, int status)
    {
        var id = $"tc-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        db.TestCases.Add(new TestCaseEntity
        {
            Id = id,
            Key = $"QA-{Random.Shared.Next(1000, 9999)}",
            Title = "Dorm fails to load after cloning a room",
            Description = "Clone RecCenter, then return to the dorm.",
            RoomName = "Dorm",
            Status = status,
            TagsJson = """["dorm","regression"]""",
            CommentsJson = """["1811750|2026-07-28T08:00:00Z|Reproduced twice"]""",
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>An in-memory GitHub. Records what was filed so the tests can
    /// assert we did not file twice, and lets them flip issue state.</summary>
    private sealed class FakeGitHub : IGitHubIssues
    {
        private readonly Dictionary<int, GitHubIssue> _issues = [];
        private int _next = 1;

        public bool IsConfigured => true;

        public string Repository => "DorkSquadRR/DorkNet";

        public int Created { get; private set; }

        public Task<GitHubIssue?> CreateAsync(
            string title, string body, IEnumerable<string> labels, CancellationToken ct = default)
        {
            var number = _next++;
            var issue = new GitHubIssue(
                number, $"https://github.com/{Repository}/issues/{number}", "open", title);
            _issues[number] = issue;
            Created++;
            return Task.FromResult<GitHubIssue?>(issue);
        }

        public Task<GitHubIssue?> GetAsync(int number, CancellationToken ct = default) =>
            Task.FromResult(_issues.TryGetValue(number, out var issue) ? issue : null);

        public Task<bool> CommentAsync(int number, string body, CancellationToken ct = default) =>
            Task.FromResult(_issues.ContainsKey(number));

        public void CloseAll() => SetState("closed");

        public void ReopenAll() => SetState("open");

        private void SetState(string state)
        {
            foreach (var number in _issues.Keys.ToList())
                _issues[number] = _issues[number] with { State = state };
        }
    }
}
