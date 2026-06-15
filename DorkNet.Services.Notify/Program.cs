using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Notify);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/notify/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Notify,
        [
            "signalr-edge",
            "notification-fanout",
            "presence",
            "player-request-logs",
        ],
        [
            "/hub/v1",
            "/api/notification/*",
            "/player/*presence*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Notify);
app.UseDorkNetPipeline();

app.Run();
