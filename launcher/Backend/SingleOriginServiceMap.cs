using System.Text.Json;

namespace DorkNet.Launcher.Backend;

public static class SingleOriginServiceMap
{
    private static readonly (string Service, string Prefix)[] Services =
    [
        ("WWW", "www"),
        ("API", "api"),
        ("Accounts", "accounts"),
        ("Auth", "auth"),
        ("BugReporting", "bugreporting"),
        ("Cards", "cards"),
        ("CDN", "cdn"),
        ("Chat", "chat"),
        ("Clubs", "clubs"),
        ("CMS", "cms"),
        ("Commerce", "commerce"),
        ("Data", "data"),
        ("DataCollection", "datacollection"),
        ("Discovery", "discovery"),
        ("Econ", "econ"),
        ("GameLogs", "gamelogs"),
        ("Geo", "geo"),
        ("Images", "img"),
        ("Leaderboard", "leaderboard"),
        ("Link", "link"),
        ("Lists", "lists"),
        ("Matchmaking", "match"),
        ("Moderation", "moderation"),
        ("NameServer", "ns"),
        ("Notifications", "notify"),
        ("PlatformNotifications", "platformnotifications"),
        ("PlayerSettings", "playersettings"),
        ("RoomComments", "roomcomments"),
        ("RoomieIntegrations", "roomieintegrations"),
        ("Rooms", "rooms"),
        ("Storage", "storage"),
        ("Strings", "strings"),
        ("StringsCDN", "strings-cdn"),
        ("Studio", "studio"),
        ("Thorn", "thorn"),
        ("Videos", "videos"),
    ];

    public static Dictionary<string, string> Build(string publicBaseUrl)
    {
        var baseUrl = NormalizeBaseUrl(publicBaseUrl);
        return Services.ToDictionary(
            x => x.Service,
            x => $"{baseUrl}/__dn/{x.Prefix}",
            StringComparer.OrdinalIgnoreCase);
    }

    public static string ToJson(string publicBaseUrl)
        => JsonSerializer.Serialize(Build(publicBaseUrl));

    public static string NormalizeBaseUrl(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Public tunnel URL is empty.", nameof(value));
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            trimmed = "https://" + trimmed;
        return trimmed.TrimEnd('/');
    }
}
