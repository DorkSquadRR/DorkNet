namespace DorkNet.Contracts;

public static class DorkNetRouteOwnership
{
    private const string AdminHost = "admin";

    private static readonly DorkNetRouteGroup[] ServiceRouteGroups =
    [
        new(
            ServiceNames.Identity,
            ["auth", "accounts"],
            [
                "/.well-known",
                "/account",
                "/api/account",
                "/api/auth",
                "/api/createaccount",
                "/api/platformlogin",
                "/api/playerreputation",
                "/api/players",
                "/api/relationships",
                "/api/settings",
                "/cachedlogin",
                "/connect",
                "/eac",
                "/parentalcontrol",
                "/photon",
                "/profileimage",
                "/settings",
            ]),
        new(
            ServiceNames.Rooms,
            ["rooms", "match", "discovery", "lists", "roomcomments", "roomieintegrations", "studio"],
            [
                "/api/activities",
                "/api/curatedroomplaylists",
                "/api/gameconfigs",
                "/api/gameconfigurations",
                "/api/gamesessions",
                "/api/leaderboard",
                "/api/playerelo",
                "/api/playlists",
                "/api/roomkeys",
                "/api/rooms",
                "/api/royale",
                "/discover",
                "/featuredrooms",
                "/goto",
                "/hot_rooms",
                "/hot_roomsandplaylists",
                "/invite",
                "/playlists",
                "/player",
                "/room",
                "/roominstance",
                "/rooms",
                "/roomsandplaylists",
                "/roomserver",
                "/search_rooms",
                "/search_roomsandplaylists",
                "/v1/find",
                "/v1/join",
                "/v1/leave",
                "/v1/session",
            ]),
        new(
            ServiceNames.Notify,
            ["notify", "chat", "platformnotifications"],
            [
                "/api/messages",
                "/api/notification",
                "/api/offlineinvite",
                "/hub",
                "/notify",
                "/thread",
            ]),
        new(
            ServiceNames.Content,
            ["cdn", "data", "img", "storage", "strings-cdn", "videos"],
            [
                "/api/images",
                "/api/photos",
                "/config",
                "/data",
                "/img",
                "/invention",
                "/upload",
                "/video",
            ]),
        new(
            ServiceNames.Social,
            ["clubs"],
            [
                "/announcements",
                "/api/announcement",
                "/api/clubreporting",
                "/api/communityboard",
                "/api/groups",
                "/api/playercheer",
                "/api/playerevents",
                "/api/playersubscriptions",
                "/club",
                "/members",
                "/subscription",
            ]),
        new(
            ServiceNames.Commerce,
            ["commerce", "econ"],
            [
                "/api/avatar",
                "/api/catalog",
                "/api/compatibility",
                "/api/consumables",
                "/api/equipment",
                "/api/gamerewards",
                "/api/inventions",
                "/api/purchase",
                "/api/storefronts",
                "/commerce",
                "/econ",
            ]),
        new(
            ServiceNames.Platform,
            ["cards", "cms", "datacollection", "gamelogs", "geo", "leaderboard", "link", "ns", "playersettings", "strings", "thorn"],
            [
                "/api/cards",
                "/api/config",
                "/api/events",
                "/api/quickplay",
                "/api/role",
                "/api/version",
                "/api/versioncheck",
                "/motd",
                "/pageview",
                "/ping",
                "/role",
                "/strings",
                "/v1/ping",
                "/v1/player",
                "/v1/regions",
                "/v1/services",
            ]),
        new(
            ServiceNames.Moderation,
            ["bugreporting", "moderation"],
            [
                "/api/admin",
                "/api/banappeal",
                "/api/bugreporting",
                "/api/moderation",
                "/api/playerreporting",
                "/api/playersbanned",
                "/api/sanitize",
                "/api/testcasemanagement",
            ]),
        new(
            ServiceNames.Web,
            ["www", AdminHost, "feed"],
            [
                "/api/site",
            ],
            IncludesApexHost: true),
    ];

    public static IReadOnlyList<DorkNetRouteGroup> RouteGroups => ServiceRouteGroups;

    public static IReadOnlyList<string> PublicSubdomains =>
        ServiceRouteGroups
            .SelectMany(group => group.HostSubdomains)
            .Append(AdminHost)
            .Append("feed")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsInfrastructurePath(string path)
    {
        return path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/internal/", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolvePublicService(string? host, string path, string apexDomain)
    {
        var normalizedPath = NormalizePath(path);
        var subdomain = Subdomain(host, apexDomain);

        if (string.Equals(subdomain, AdminHost, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceNames.Web;
        }

        foreach (var group in ServiceRouteGroups.Where(group => group.ServiceName != ServiceNames.Web))
        {
            if (MatchesHost(subdomain, group.HostSubdomains))
            {
                return group.ServiceName;
            }
        }

        foreach (var group in ServiceRouteGroups)
        {
            if (StartsWithAny(normalizedPath, group.PathPrefixes))
            {
                return group.ServiceName;
            }
        }

        var web = ServiceRouteGroups.First(group => group.ServiceName == ServiceNames.Web);
        if ((subdomain is null && web.IncludesApexHost) || MatchesHost(subdomain, web.HostSubdomains))
        {
            return ServiceNames.Web;
        }

        return ServiceNames.Monolith;
    }

    public static bool IsOwnedBy(string serviceName, string? host, string path, string apexDomain)
    {
        if (IsInfrastructurePath(path))
        {
            return true;
        }

        return string.Equals(
            ResolvePublicService(host, path, apexDomain),
            serviceName,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? Subdomain(string? host, string apexDomain)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedHost = host.Split(':', 2)[0].Trim().ToLowerInvariant();
        var normalizedApex = apexDomain.Split(':', 2)[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedHost)
            || string.IsNullOrWhiteSpace(normalizedApex)
            || string.Equals(normalizedHost, normalizedApex, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = "." + normalizedApex;
        if (normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedHost[..^suffix.Length];
        }

        var dot = normalizedHost.IndexOf('.');
        return dot > 0 ? normalizedHost[..dot] : null;
    }

    private static bool StartsWithAny(string path, IEnumerable<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (StartsWithPathSegment(path, prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithPathSegment(string path, string prefix)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedPrefix = NormalizePath(prefix).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            normalizedPrefix = "/";
        }

        return normalizedPath.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesHost(string? subdomain, IEnumerable<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            return false;
        }

        return candidates.Contains(subdomain, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path[0] == '/' ? path : "/" + path;
    }
}

public sealed record DorkNetRouteGroup(
    string ServiceName,
    string[] HostSubdomains,
    string[] PathPrefixes,
    bool IncludesApexHost = false);
