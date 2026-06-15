using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DorkNet.Server.Tests;

public sealed class GameClientEndpointContractTests :
    IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public GameClientEndpointContractTests(DorkNetServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Game_client_contract_probe_reaches_every_controller_route()
    {
        var routes = EndpointContractDiscovery.Discover();
        Assert.NotEmpty(routes);

        using var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
        });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(20);

        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        var failures = new List<string>();

        foreach (var route in routes)
        {
            var result = await ProbeAsync(client, route, session, _factory.ApexDomain);
            if (!result.Passed)
            {
                failures.Add(
                    $"{route.Method,-6} {result.Host}{result.Path} {route.Source} expected {result.Expected}, got {result.Observed}"
                    + (string.IsNullOrWhiteSpace(result.ResponseExcerpt) ? "" : $" body={result.ResponseExcerpt}"));
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Endpoint contract failures ({failures.Count}/{routes.Count}):{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    private static async Task<EndpointProbeResult> ProbeAsync(
        HttpClient client,
        EndpointContract route,
        GameClientSession session,
        string apexDomain)
    {
        var path = route.PathFor(session);
        path = AddClientQuery(path, route, session);
        var host = HostFor(path, apexDomain);
        using var request = new HttpRequestMessage(
            ToHttpMethod(route.Method),
            new Uri($"http://{host}{path}"));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("RecRoom/2020.12.18");
        request.Headers.TryAddWithoutValidation("X-DorkNet-Version", "december_2020_12_18");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        if (NeedsRequestBody(request.Method))
        {
            request.Content = CreateContent(route, session);
        }

        try
        {
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;
            var passed = ResponseMatchesContract(route, response.StatusCode);
            return new EndpointProbeResult(
                route.Method,
                host,
                path,
                ExpectedContract(route),
                statusCode,
                null,
                Excerpt(responseText),
                passed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new EndpointProbeResult(
                route.Method,
                host,
                path,
                ExpectedContract(route),
                null,
                ex.Message,
                null,
                false);
        }
    }

    private static bool ResponseMatchesContract(
        EndpointContract route,
        HttpStatusCode statusCode)
    {
        if ((int)statusCode >= 500)
        {
            return false;
        }

        if (statusCode is HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented
            or HttpStatusCode.UpgradeRequired)
        {
            return false;
        }

        if (route.RequiresAuthorization
            && statusCode is HttpStatusCode.Unauthorized)
        {
            return false;
        }

        return true;
    }

    private static string ExpectedContract(EndpointContract route)
    {
        if (route.IsAdminEndpoint)
        {
            return "admin endpoint may reject non-admin test player, but must not 5xx/405/426";
        }

        if (route.RequiresAuthorization)
        {
            return "authenticated test player should reach the action without 401/5xx/405/426; 403 is valid for owner/admin-only resources";
        }

        return "game-client request should not 5xx/405/426";
    }

    private static HttpMethod ToHttpMethod(string method)
    {
        return string.Equals(method, "ANY", StringComparison.OrdinalIgnoreCase)
            ? HttpMethod.Get
            : new HttpMethod(method);
    }

    private static bool NeedsRequestBody(HttpMethod method)
    {
        return method == HttpMethod.Post
            || method == HttpMethod.Put
            || method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpContent CreateContent(
        EndpointContract route,
        GameClientSession session)
    {
        var consumes = route.Consumes.Select(c => c.ToLowerInvariant()).ToArray();
        if (consumes.Any(c => c.Contains("application/x-www-form-urlencoded")))
        {
            return new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["account_id"] = session.PlayerId.ToString(),
                ["AccountId"] = session.PlayerId.ToString(),
                ["clubId"] = "1",
                ["ClubId"] = "1",
                ["description"] = "Endpoint contract description",
                ["deviceId"] = session.DeviceId,
                ["grant_type"] = "password",
                ["image"] = "endpoint-contract.png",
                ["imageName"] = "endpoint-contract.png",
                ["fileName"] = "endpoint-contract.png",
                ["message"] = "Endpoint contract message",
                ["name"] = "EndpointContract",
                ["newPassword"] = "endpoint-contract-password",
                ["oldPassword"] = "",
                ["password"] = "endpoint-contract-password",
                ["ping"] = "42",
                ["platformId"] = session.PlatformId,
                ["playerId"] = session.PlayerId.ToString(),
                ["playerIds"] = session.PlayerId.ToString(),
                ["query"] = "DormRoom",
                ["region"] = "us",
                ["targetPlayerId"] = session.PlayerId.ToString(),
                ["text"] = "Endpoint contract message",
                ["username"] = "EndpointContract",
                ["device_id"] = session.DeviceId,
                ["platform"] = "0",
                ["platform_id"] = session.PlatformId,
                ["Ids"] = session.PlayerId.ToString(),
                ["PlayerIds"] = session.PlayerId.ToString(),
            });
        }

        if (consumes.Any(c => c.Contains("multipart/form-data")))
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("endpoint-contract"), "name");
            content.Add(new StringContent(session.PlayerId.ToString()), "playerId");
            content.Add(new StringContent("endpoint-contract.png"), "imageName");
            content.Add(new StringContent("endpoint-contract.png"), "fileName");
            content.Add(new ByteArrayContent([]), "file", "endpoint-contract.bin");
            return content;
        }

        if (consumes.Any(c => c.Contains("text/plain")))
        {
            return new StringContent("", Encoding.UTF8, "text/plain");
        }

        var json = JsonSerializer.Serialize(new
        {
            PlayerId = session.PlayerId,
            AccountId = session.PlayerId,
            ClubId = 1,
            Description = "Endpoint contract description",
            DeviceId = session.DeviceId,
            FileName = "endpoint-contract.png",
            Id = session.PlayerId,
            Image = "endpoint-contract.png",
            ImageName = "endpoint-contract.png",
            Message = "Endpoint contract message",
            Platform = 0,
            PlatformId = session.PlatformId,
            PlayerIds = new[] { session.PlayerId },
            Name = "EndpointContract",
            Password = "endpoint-contract-password",
            Query = "DormRoom",
            RoomName = "DormRoom",
            TargetPlayerId = session.PlayerId,
            Text = "Endpoint contract message",
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string AddClientQuery(
        string path,
        EndpointContract route,
        GameClientSession session)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var lower = path.ToLowerInvariant();
        var query = new List<string>();

        if (lower.Contains("search", StringComparison.Ordinal)
            || lower.Contains("query", StringComparison.Ordinal))
        {
            query.Add("query=EndpointContract");
        }

        if (lower == "/members/bulk")
        {
            query.Add("clubId=1");
            query.Add($"playerIds={session.PlayerId}");
        }

        if (lower == "/player/photonregionpings")
        {
            query.Add("region=us");
            query.Add("ping=42");
        }

        if (route.Source.Contains("CachedLogins", StringComparison.Ordinal)
            || lower.Contains("platformlogin/cached", StringComparison.Ordinal)
            || lower.Contains("savedlogins", StringComparison.Ordinal))
        {
            query.Add("platform=0");
            query.Add($"platformId={Uri.EscapeDataString(session.PlatformId)}");
        }

        if (route.Source.Contains("Bulk", StringComparison.Ordinal)
            && !query.Any(q => q.StartsWith("Ids=", StringComparison.OrdinalIgnoreCase)))
        {
            query.Add($"Ids={session.PlayerId}");
        }

        if (query.Count == 0)
        {
            return path;
        }

        return path + separator + string.Join("&", query);
    }

    private static string HostFor(string path, string apexDomain)
    {
        var lower = path.ToLowerInvariant();

        if (lower.StartsWith("/connect/")
            || lower.StartsWith("/.well-known/")
            || lower.StartsWith("/eac/")
            || lower.StartsWith("/cachedlogin/")
            || lower.StartsWith("/photon/")
            || lower.StartsWith("/api/platformlogin/")
            || lower.StartsWith("/api/version")
            || lower.StartsWith("/api/auth/"))
        {
            return $"auth.{apexDomain}";
        }

        if (lower.StartsWith("/account/"))
        {
            return $"accounts.{apexDomain}";
        }

        if (lower.StartsWith("/roomserver/")
            || lower.StartsWith("/rooms")
            || lower.StartsWith("/playlists")
            || lower.StartsWith("/featuredrooms")
            || lower.StartsWith("/hot_rooms")
            || lower.StartsWith("/search_rooms")
            || lower.StartsWith("/api/rooms")
            || lower.StartsWith("/api/roomkeys")
            || lower.StartsWith("/api/gamesessions")
            || lower.StartsWith("/api/curatedroomplaylists"))
        {
            return $"rooms.{apexDomain}";
        }

        if (lower.StartsWith("/v1/find")
            || lower.StartsWith("/v1/join")
            || lower.StartsWith("/v1/leave")
            || lower.StartsWith("/v1/session")
            || lower.StartsWith("/roominstance/")
            || lower.StartsWith("/goto/")
            || lower.StartsWith("/player/"))
        {
            return $"match.{apexDomain}";
        }

        if (lower.StartsWith("/thread"))
        {
            return $"chat.{apexDomain}";
        }

        if (lower.StartsWith("/club")
            || lower.StartsWith("/announcements/")
            || lower.StartsWith("/subscription/")
            || lower.StartsWith("/members/")
            || lower.StartsWith("/api/clubreporting/"))
        {
            return $"clubs.{apexDomain}";
        }

        if (lower.StartsWith("/api/storefronts/")
            || lower.StartsWith("/api/purchase/")
            || lower.StartsWith("/api/subscription/"))
        {
            return $"commerce.{apexDomain}";
        }

        if (lower.StartsWith("/hub/") || lower.StartsWith("/api/notification/"))
        {
            return $"notify.{apexDomain}";
        }

        if (lower.StartsWith("/v1/regions"))
        {
            return $"geo.{apexDomain}";
        }

        if (lower.StartsWith("/v1/services") || lower.StartsWith("/v1/ping"))
        {
            return $"ns.{apexDomain}";
        }

        if (lower.StartsWith("/upload"))
        {
            return $"storage.{apexDomain}";
        }

        if (lower.StartsWith("/img/") || lower.StartsWith("/room/") || lower.StartsWith("/data/"))
        {
            return $"cdn.{apexDomain}";
        }

        if (lower.StartsWith("/api/admin/"))
        {
            return $"admin.{apexDomain}";
        }

        return $"api.{apexDomain}";
    }

    private static string? Excerpt(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var compact = body.ReplaceLineEndings(" ").Trim();
        return compact.Length <= 240 ? compact : compact[..240] + "...";
    }
}

public sealed record EndpointProbeResult(
    string Method,
    string Host,
    string Path,
    string Expected,
    int? StatusCode,
    string? Error,
    string? ResponseExcerpt,
    bool Passed)
{
    public string Observed => StatusCode is int code ? code.ToString() : Error ?? "no response";
}
