namespace DorkNet.Contracts;

public sealed class DorkNetServiceMapOptions
{
    public string Identity { get; set; } = "http://localhost:8081";
    public string Rooms { get; set; } = "http://localhost:8082";
    public string Notify { get; set; } = "http://localhost:8083";
    public string Monolith { get; set; } = "http://localhost:8084";

    public IReadOnlyList<DorkNetServiceEndpoint> Endpoints()
    {
        DorkNetServiceEndpoint[] endpoints =
        [
            new(ServiceNames.Identity, Identity),
            new(ServiceNames.Rooms, Rooms),
            new(ServiceNames.Notify, Notify),
            new(ServiceNames.Monolith, Monolith),
        ];

        return endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.BaseUrl))
            .ToArray();
    }
}

public sealed record DorkNetServiceEndpoint(string Name, string BaseUrl);
