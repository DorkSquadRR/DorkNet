using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Rooms);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/rooms/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Rooms,
        [
            "rooms",
            "subrooms",
            "room-keys",
            "matchmaking",
            "discovery",
        ],
        [
            "/rooms/*",
            "/api/rooms/*",
            "/roomserver/*",
            "/v1/find",
            "/v1/join/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Rooms);
app.UseDorkNetPipeline();

app.Run();
