using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Social);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/social/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Social,
        [
            "clubs",
            "groups",
            "announcements",
            "player-events",
            "subscriptions",
        ],
        [
            "/club/*",
            "/api/groups/*",
            "/api/playerevents/*",
            "/api/playersubscriptions/*",
            "/announcements/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Social);
app.UseDorkNetPipeline();

app.Run();
