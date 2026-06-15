using DorkNet.Contracts;
using DorkNet.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Notify);

var app = builder.Build();

app.MapDorkNetServiceDefaults();
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

app.Run();
