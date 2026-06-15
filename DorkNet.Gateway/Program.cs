using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Gateway);
builder.Services.Configure<DorkNetServiceMapOptions>(
    builder.Configuration.GetSection("DorkNet:Services"));

var app = builder.Build();

app.MapDorkNetServiceDefaults();

app.MapGet("/", () => Results.Redirect("/healthz"));
app.MapGet("/internal/services", (IOptions<DorkNetServiceMapOptions> options) =>
    Results.Ok(options.Value.Endpoints()));

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
