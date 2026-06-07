using DorkNet.Models.Config;

namespace DorkNet.Server.Services;

public class ConfigService(IConfiguration config, DomainConfig domain)
{
    public RecRoomConfig GetConfig(string baseUrl)
    {
        var photon = new PhotonConfig
        {
            AppId = config["Photon:AppId"] ?? string.Empty,
            VoiceAppId = config["Photon:VoiceAppId"] ?? string.Empty,
            CloudRegion = config["Photon:CloudRegion"] ?? "us",
        };

        // baseUrl was previously parsed to mirror the request's apex
        // back at the client, but with DomainConfig as the single
        // source of truth every outbound URL is built off the
        // configured deployment apex regardless of which Host header
        // the client used to hit us. The baseUrl parameter is kept
        // for API stability but is no longer dereferenced.
        _ = baseUrl;
        return new RecRoomConfig
        {
            MessageOfTheDay = config["Server:MOTD"] ?? "Welcome to the private server!",
            CdnBaseUri = domain.SingleOriginEnabled
                ? $"{domain.SingleOriginBaseUrl}/__dn/cdn"
                : domain.SubUrl("cdn"),
            PhotonConfig = photon,
            ServiceUrls = BuildServiceUrlMap(domain),
            ConfigTable = new Dictionary<string, string>
            {
                // Season keys the 2019/2020 client checks at startup
                ["Season"]             = "Spring",
                ["CurrentSeason"]      = "Spring",
                ["SeasonId"]           = "1",
                ["SeasonName"]         = "Spring",
                ["ActiveEvent"]        = "None",
                ["EventName"]          = "",
                // Feature flags — keep everything on
                ["FriendsEnabled"]     = "true",
                ["ChatEnabled"]        = "true",
                ["VoiceEnabled"]       = "true",
            },
        };
    }

    /// <summary>
    /// Subdomain → service-name map. The wire response wraps each
    /// value with <c>https://</c> + the configured apex domain
    /// (<see cref="DomainConfig"/>, driven by DORKNET_DOMAIN) so
    /// every URL the client sees comes from one source of truth.
    /// </summary>
    private static readonly (string Service, string Subdomain)[] ServiceSubdomains = new[]
    {
        ("WWW",                    ""),                        // apex
        ("API",                    "api"),
        ("Accounts",               "accounts"),
        ("Auth",                   "auth"),
        ("BugReporting",           "bugreporting"),
        ("Cards",                  "cards"),
        ("CDN",                    "cdn"),
        ("Chat",                   "chat"),
        ("Clubs",                  "clubs"),
        ("CMS",                    "cms"),
        ("Commerce",               "commerce"),
        ("Data",                   "data"),
        ("DataCollection",         "datacollection"),
        ("Discovery",              "discovery"),
        ("Econ",                   "econ"),
        ("GameLogs",               "gamelogs"),
        ("Geo",                    "geo"),
        ("Images",                 "img"),
        ("Leaderboard",            "leaderboard"),
        ("Link",                   "link"),
        ("Lists",                  "lists"),
        ("Matchmaking",            "match"),
        ("Moderation",             "moderation"),
        ("NameServer",             "ns"),
        ("Notifications",          "notify"),
        ("PlatformNotifications",  "platformnotifications"),
        ("PlayerSettings",         "playersettings"),
        ("RoomComments",           "roomcomments"),
        ("RoomieIntegrations",     "roomieintegrations"),
        ("Rooms",                  "rooms"),
        ("Storage",                "storage"),
        ("Strings",                "strings"),
        ("StringsCDN",             "strings-cdn"),
        ("Studio",                 "studio"),
        ("Thorn",                  "thorn"),
        ("Videos",                 "videos"),
    };

    /// <summary>Build the service URL map for a specific apex
    /// domain. Used by NsController + the client config endpoints;
    /// the apex is fed in by the caller (typically
    /// <see cref="DomainConfig.Apex"/>) so the map stays a single
    /// source of truth.</summary>
    public static Dictionary<string, string> BuildServiceUrlMap(string apex)
    {
        var map = new Dictionary<string, string>(ServiceSubdomains.Length);
        foreach (var (service, sub) in ServiceSubdomains)
        {
            var host = string.IsNullOrEmpty(sub) ? apex : $"{sub}.{apex}";
            map[service] = $"https://{host}/";
        }
        return map;
    }

    public static Dictionary<string, string> BuildServiceUrlMap(DomainConfig domain)
    {
        var map = new Dictionary<string, string>(ServiceSubdomains.Length);
        foreach (var (service, sub) in ServiceSubdomains)
        {
            if (domain.SingleOriginEnabled)
            {
                var prefix = string.IsNullOrEmpty(sub) ? "www" : sub;
                map[service] = $"{domain.SingleOriginBaseUrl}/__dn/{prefix}/";
                continue;
            }
            var host = string.IsNullOrEmpty(sub) ? domain.Apex : domain.Sub(sub);
            map[service] = EnsureTrailingSlash(domain.Url(host));
        }
        return map;
    }

    // The 2018.06 client builds every API URL by RAW string concat —
    // new Uri(string.Concat(serviceBase, "api/config/v2")) — so each service
    // base MUST be an absolute origin ending in '/', else the host glues to
    // the path ("https://api.apexapi/config/v2") and every call 404s. The
    // 2020 client runs EnsureEndsWith('/') on these values itself, so the
    // trailing slash is idempotent there — safe for both eras.
    private static string EnsureTrailingSlash(string url)
        => url.EndsWith('/') ? url : url + "/";
}
