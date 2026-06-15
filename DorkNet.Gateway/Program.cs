using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Gateway);
builder.Services.Configure<DorkNetServiceMapOptions>(
    builder.Configuration.GetSection("DorkNet:Services"));
var serviceMap = builder.Configuration
    .GetSection("DorkNet:Services")
    .Get<DorkNetServiceMapOptions>() ?? new DorkNetServiceMapOptions();
var apexDomain = builder.Configuration["Domain:Apex"]
    ?? Environment.GetEnvironmentVariable("DORKNET_DOMAIN")
    ?? "localhost";
builder.Services
    .AddReverseProxy()
    .LoadFromMemory(
        BuildRoutes(apexDomain, serviceMap),
        BuildClusters(serviceMap));

var app = builder.Build();

app.MapDorkNetServiceDefaults();

app.MapGet("/", () => Results.Redirect("/healthz"));
app.MapGet("/internal/services", (IOptions<DorkNetServiceMapOptions> options) =>
    Results.Ok(options.Value.Endpoints()));
app.MapGet("/internal/routes", () =>
    Results.Ok(new
    {
        ApexDomain = apexDomain,
        Routes = BuildRoutes(apexDomain, serviceMap).Select(r => new
        {
            r.RouteId,
            r.ClusterId,
            r.Match.Hosts,
            r.Match.Path,
            r.Order,
        }),
    }));

app.MapGet("/internal/services/health", async (
    IOptions<DorkNetServiceMapOptions> options,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var client = httpClientFactory.CreateClient();
    var probes = new List<ServiceProbeResponse>();

    foreach (var endpoint in options.Value.Endpoints())
    {
        probes.Add(await ProbeServiceAsync(client, endpoint, ct));
    }

    return Results.Ok(probes);
});

app.MapReverseProxy();

app.Run();

static async Task<ServiceProbeResponse> ProbeServiceAsync(
    HttpClient client,
    DorkNetServiceEndpoint endpoint,
    CancellationToken ct)
{
    var checkedAt = DateTimeOffset.UtcNow;
    var baseUrl = endpoint.BaseUrl.TrimEnd('/');

    try
    {
        using var response = await client.GetAsync($"{baseUrl}/internal/healthz", ct);
        return new ServiceProbeResponse(
            endpoint.Name,
            endpoint.BaseUrl,
            response.IsSuccessStatusCode ? "ok" : "unhealthy",
            (int)response.StatusCode,
            null,
            checkedAt);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return new ServiceProbeResponse(
            endpoint.Name,
            endpoint.BaseUrl,
            "unreachable",
            null,
            ex.Message,
            checkedAt);
    }
}

static IReadOnlyList<RouteConfig> BuildRoutes(
    string apexDomain,
    DorkNetServiceMapOptions services)
{
    var routes = new List<RouteConfig>();
    var order = 0;
    var webGroup = DorkNetRouteOwnership.RouteGroups
        .First(group => group.ServiceName == ServiceNames.Web);

    AddHostRoute(
        routes,
        ref order,
        webGroup.ServiceName,
        BaseUrlFor(services, webGroup.ServiceName),
        "web-admin-host",
        [SubdomainHost("admin", apexDomain)]);

    foreach (var group in DorkNetRouteOwnership.RouteGroups.Where(group => group.ServiceName != ServiceNames.Web))
    {
        AddHostRoute(
            routes,
            ref order,
            group.ServiceName,
            BaseUrlFor(services, group.ServiceName),
            $"{group.ServiceName}-hosts",
            group.HostSubdomains.Select(subdomain => SubdomainHost(subdomain, apexDomain)).ToArray());
    }

    foreach (var group in DorkNetRouteOwnership.RouteGroups)
    {
        AddPathRoutes(
            routes,
            ref order,
            group.ServiceName,
            BaseUrlFor(services, group.ServiceName),
            group.PathPrefixes);
    }

    AddHostRoute(
        routes,
        ref order,
        webGroup.ServiceName,
        BaseUrlFor(services, webGroup.ServiceName),
        "web-hosts",
        BuildWebHosts(webGroup, apexDomain));

    if (!string.IsNullOrWhiteSpace(services.Monolith))
    {
        routes.Add(Route(ServiceNames.Monolith, "monolith-fallback", "/{**catch-all}", order++));
    }

    return routes;
}

static IReadOnlyList<ClusterConfig> BuildClusters(DorkNetServiceMapOptions services)
{
    var clusters = new List<ClusterConfig>();
    foreach (var endpoint in services.Endpoints())
    {
        AddCluster(clusters, endpoint.Name, endpoint.BaseUrl);
    }

    return clusters;
}

static void AddHostRoute(
    List<RouteConfig> routes,
    ref int order,
    string serviceName,
    string baseUrl,
    string routeId,
    string[] hosts)
{
    if (string.IsNullOrWhiteSpace(baseUrl) || hosts.Length == 0)
    {
        return;
    }

    routes.Add(Route(
        serviceName,
        routeId,
        "/{**catch-all}",
        order++,
        hosts));
}

static void AddPathRoutes(
    List<RouteConfig> routes,
    ref int order,
    string serviceName,
    string baseUrl,
    IEnumerable<string> paths)
{
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        return;
    }

    foreach (var path in paths)
    {
        var routeId = $"{serviceName}-{path.Trim('/').Replace('/', '-').Replace("{**catch-all}", "all")}";
        var exactPath = NormalizeRoutePrefix(path);
        routes.Add(Route(serviceName, $"{routeId}-exact", exactPath, order++));
        routes.Add(Route(serviceName, routeId, $"{exactPath}/{{**catch-all}}", order++));
    }
}

static string BaseUrlFor(DorkNetServiceMapOptions services, string serviceName)
{
    return serviceName switch
    {
        ServiceNames.Identity => services.Identity,
        ServiceNames.Rooms => services.Rooms,
        ServiceNames.Notify => services.Notify,
        ServiceNames.Content => services.Content,
        ServiceNames.Social => services.Social,
        ServiceNames.Commerce => services.Commerce,
        ServiceNames.Platform => services.Platform,
        ServiceNames.Moderation => services.Moderation,
        ServiceNames.Web => services.Web,
        ServiceNames.Monolith => services.Monolith,
        _ => string.Empty,
    };
}

static string[] BuildWebHosts(DorkNetRouteGroup group, string apexDomain)
{
    var hosts = new List<string>();
    if (group.IncludesApexHost)
    {
        hosts.Add(apexDomain);
    }

    hosts.AddRange(group.HostSubdomains
        .Where(subdomain => !string.Equals(subdomain, "admin", StringComparison.OrdinalIgnoreCase))
        .Select(subdomain => SubdomainHost(subdomain, apexDomain)));

    return hosts.ToArray();
}

static string SubdomainHost(string subdomain, string apexDomain)
{
    return $"{subdomain}.{apexDomain}";
}

static string NormalizeRoutePrefix(string path)
{
    if (string.IsNullOrWhiteSpace(path) || path == "/")
    {
        return "/";
    }

    return (path[0] == '/' ? path : "/" + path).TrimEnd('/');
}

static RouteConfig Route(
    string clusterId,
    string routeId,
    string path,
    int order,
    string[]? hosts = null)
{
    return new RouteConfig
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = order,
        Match = new RouteMatch
        {
            Hosts = hosts,
            Path = path,
        },
        Transforms =
        [
            new Dictionary<string, string>
            {
                ["RequestHeaderOriginalHost"] = "true",
            },
        ],
    };
}

static void AddCluster(
    List<ClusterConfig> clusters,
    string serviceName,
    string baseUrl)
{
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        return;
    }

    var address = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    clusters.Add(new ClusterConfig
    {
        ClusterId = serviceName,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["primary"] = new() { Address = address },
        },
    });
}
