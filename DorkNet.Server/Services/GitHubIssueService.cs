using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DorkNet.Server.Services;

/// <summary>One GitHub issue, reduced to what QA linking needs.</summary>
public sealed record GitHubIssue(int Number, string Url, string State, string Title);

/// <summary>
/// Files and reads GitHub issues for the QA test-case tooling.
///
/// Deliberately narrow: create, read state, comment. Everything else a QA
/// workflow might want (labels beyond the fixed set, milestones, assignees) is
/// out of scope until something asks for it.
/// </summary>
public interface IGitHubIssues
{
    /// <summary>False when no token/repo is configured. Callers must check this
    /// and degrade rather than throw — a server without GitHub credentials is a
    /// normal deployment, not a broken one.</summary>
    bool IsConfigured { get; }

    /// <summary>owner/name, or empty when unconfigured.</summary>
    string Repository { get; }

    Task<GitHubIssue?> CreateAsync(string title, string body, IEnumerable<string> labels, CancellationToken ct = default);

    Task<GitHubIssue?> GetAsync(int number, CancellationToken ct = default);

    Task<bool> CommentAsync(int number, string body, CancellationToken ct = default);
}

/// <summary>
/// REST implementation over api.github.com.
///
/// Configured with <c>GitHub:Token</c> (a PAT with <c>issues:write</c> on the
/// target repo) and <c>GitHub:Repository</c> (<c>owner/name</c>). Both are
/// read once at construction; absent either, <see cref="IsConfigured"/> is
/// false and every call returns null rather than throwing, so QA endpoints
/// answer "not configured" instead of 500ing.
/// </summary>
public sealed class GitHubIssueService : IGitHubIssues
{
    private const string ApiRoot = "https://api.github.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubIssueService> _log;
    private readonly string _token;

    public GitHubIssueService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubIssueService> log)
    {
        _httpClientFactory = httpClientFactory;
        _log = log;
        _token = config["GitHub:Token"] ?? string.Empty;
        Repository = (config["GitHub:Repository"] ?? string.Empty).Trim().Trim('/');

        if (!IsConfigured)
        {
            _log.LogInformation(
                "[github] no token/repository configured; issue linking is disabled");
        }
        else
        {
            _log.LogInformation("[github] issue linking enabled for {Repository}", Repository);
        }
    }

    public bool IsConfigured => _token.Length > 0 && Repository.Contains('/');

    public string Repository { get; }

    public async Task<GitHubIssue?> CreateAsync(
        string title, string body, IEnumerable<string> labels, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            labels = labels.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToArray(),
        });

        return await SendAsync(HttpMethod.Post, $"/repos/{Repository}/issues", payload, ct);
    }

    public Task<GitHubIssue?> GetAsync(int number, CancellationToken ct = default) =>
        IsConfigured
            ? SendAsync(HttpMethod.Get, $"/repos/{Repository}/issues/{number}", null, ct)
            : Task.FromResult<GitHubIssue?>(null);

    public async Task<bool> CommentAsync(int number, string body, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        var payload = JsonSerializer.Serialize(new { body });
        using var response = await SendRawAsync(
            HttpMethod.Post, $"/repos/{Repository}/issues/{number}/comments", payload, ct);
        return response is { IsSuccessStatusCode: true };
    }

    private async Task<GitHubIssue?> SendAsync(
        HttpMethod method, string path, string? payload, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, payload, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;

        try
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new GitHubIssue(
                root.GetProperty("number").GetInt32(),
                root.GetProperty("html_url").GetString() ?? string.Empty,
                root.GetProperty("state").GetString() ?? "open",
                root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[github] could not read the issue response from {Method} {Path}", method, path);
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendRawAsync(
        HttpMethod method, string path, string? payload, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(method, ApiRoot + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            // GitHub rejects requests without one.
            request.Headers.UserAgent.ParseAdd("DorkNet-QA");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (payload is not null)
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // A 404 on a GET is ordinary (deleted issue); anything else is
                // worth seeing, but never fatal to the caller.
                var level = method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound
                    ? LogLevel.Debug
                    : LogLevel.Warning;
                _log.Log(level, "[github] {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);
            }
            return response;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[github] {Method} {Path} failed", method, path);
            return null;
        }
    }

    /// <summary>Pull the issue number out of a stored issue URL.
    ///
    /// The link lives in <c>TestCaseEntity.JiraBugUrl</c> — the field Rec
    /// Room's own QA tooling used for the same purpose — so the only thing
    /// identifying it as a GitHub issue is the URL shape. Returns null for
    /// anything that isn't one, which is how a genuine Jira link left over
    /// from the original data is ignored rather than misparsed.</summary>
    public static int? IssueNumberFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = Regex.Match(url, @"github\.com/[^/]+/[^/]+/issues/(?<number>\d+)",
            RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : null;
    }
}
