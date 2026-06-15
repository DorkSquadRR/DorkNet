using DorkNet.Contracts;
using DorkNet.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Rooms);

var app = builder.Build();

app.MapDorkNetServiceDefaults();
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

app.Run();
