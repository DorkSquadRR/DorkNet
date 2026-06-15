namespace DorkNet.Contracts;

public sealed class DorkNetServiceMapOptions
{
    public string Identity { get; set; } = "http://localhost:8081";
    public string Rooms { get; set; } = "http://localhost:8082";
    public string Notify { get; set; } = "http://localhost:8083";
    public string Content { get; set; } = "http://localhost:8084";
    public string Social { get; set; } = "http://localhost:8085";
    public string Commerce { get; set; } = "http://localhost:8086";
    public string Platform { get; set; } = "http://localhost:8087";
    public string Moderation { get; set; } = "http://localhost:8088";
    public string Web { get; set; } = "http://localhost:8089";
    public string Monolith { get; set; } = "http://localhost:8090";

    public IReadOnlyList<DorkNetServiceEndpoint> Endpoints()
    {
        DorkNetServiceEndpoint[] endpoints =
        [
            new(ServiceNames.Identity, Identity),
            new(ServiceNames.Rooms, Rooms),
            new(ServiceNames.Notify, Notify),
            new(ServiceNames.Content, Content),
            new(ServiceNames.Social, Social),
            new(ServiceNames.Commerce, Commerce),
            new(ServiceNames.Platform, Platform),
            new(ServiceNames.Moderation, Moderation),
            new(ServiceNames.Web, Web),
            new(ServiceNames.Monolith, Monolith),
        ];

        return endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.BaseUrl))
            .ToArray();
    }
}

public sealed record DorkNetServiceEndpoint(string Name, string BaseUrl);
