using System.Text.RegularExpressions;

namespace DorkNet.Server.Tests;

/// <summary>
/// Asserts that every HTTP request the Rec Room 2023-03-21 client can issue has
/// a handler on this server, under the verb the client actually uses.
///
/// Rec Room is shut down, so the client can't be observed against a live server.
/// <c>data/client-routes-2023.tsv</c> is the recovered inventory of what it
/// asks for — see <c>docs/recroom-2023-client-api-complete.md</c> for how it was
/// extracted. This test is the regression guard on that inventory: adding a
/// client route without a handler, or moving a handler to a different verb,
/// fails here rather than silently 404/405-ing a real player.
///
/// Routes not yet implemented live in <see cref="KnownGaps"/>. That list is
/// expected to shrink and never grow. Deleting an entry as you implement it is
/// the point — it is the project's running score against the client.
/// </summary>
public sealed class ClientRouteCoverageTests
{
    /// <summary>Client routes with no handler yet. Each entry is
    /// <c>VERB route</c> exactly as it appears in the inventory file.</summary>
    private static readonly HashSet<string> KnownGaps = new(StringComparer.OrdinalIgnoreCase)
    {
        // (populated from the current gap report; remove as they are implemented)
    };

    [Fact]
    public void Every_client_route_has_a_handler()
    {
        var client = LoadClientRoutes();
        Assert.NotEmpty(client);

        var discovered = EndpointContractDiscovery.Discover()
            .Select(r => (Verb: r.Method.ToUpperInvariant(), Pattern: Normalise(r.Template)))
            .ToList();

        var server = discovered.ToHashSet();

        // A bare [Route] with no verb attribute serves every verb.
        var verbless = discovered
            .Where(d => d.Verb == "ANY")
            .Select(d => d.Pattern)
            .ToHashSet(StringComparer.Ordinal);

        var serverAnyVerb = discovered.Select(s => s.Pattern).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        var wrongVerb = new List<string>();

        foreach (var (verb, route, _) in client)
        {
            var pattern = Normalise(route);
            if (server.Contains((verb, pattern)) || verbless.Contains(pattern)) continue;

            // Distinguish "no handler at all" from "handler exists, wrong verb" —
            // they have different causes and different fixes.
            if (serverAnyVerb.Contains(pattern)) wrongVerb.Add($"{verb} {route}");
            else missing.Add($"{verb} {route}");
        }

        var gaps = missing.Concat(wrongVerb)
            .Where(g => !KnownGaps.Contains(g))
            .OrderBy(g => g, StringComparer.Ordinal)
            .ToList();

        var covered = client.Count - missing.Count - wrongVerb.Count;
        var pct = 100.0 * covered / client.Count;

        Assert.True(gaps.Count == 0,
            $"""
             Client route coverage: {covered}/{client.Count} ({pct:F1}%).
             {missing.Count} missing, {wrongVerb.Count} wrong-verb, {KnownGaps.Count} known gaps.

             Unlisted gaps ({gaps.Count}) — implement them, or add to KnownGaps with a reason:
             {string.Join(Environment.NewLine, gaps)}
             """);
    }

    /// <summary>Collapse a route to a comparable shape: no host, no leading or
    /// trailing slash, no query, and every parameter — the client's <c>{0}</c>
    /// and the server's <c>{id:long}</c> alike — reduced to <c>*</c>.</summary>
    private static string Normalise(string route)
    {
        var r = route.Trim();
        var q = r.IndexOf('?');
        if (q >= 0) r = r[..q];
        r = r.Trim('/', '~');
        r = Regex.Replace(r, @"\{[^}]*\}", "*");
        return r.ToLowerInvariant();
    }

    private static List<(string Verb, string Route, string Subsystem)> LoadClientRoutes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "client-routes-2023.tsv");
        Assert.True(File.Exists(path), $"missing route inventory at {path}");

        var rows = new List<(string, string, string)>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            rows.Add((parts[0].Trim().ToUpperInvariant(), parts[1].Trim(),
                      parts.Length > 2 ? parts[2].Trim() : string.Empty));
        }
        return rows;
    }
}
