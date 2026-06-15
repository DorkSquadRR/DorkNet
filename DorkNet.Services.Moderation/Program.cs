using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Moderation);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/moderation/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Moderation,
        [
            "bug-reporting",
            "player-reporting",
            "sanitize",
            "admin-api",
            "testcase-management",
        ],
        [
            "/api/bugreporting/*",
            "/api/playerreporting/*",
            "/api/sanitize/*",
            "/api/admin/*",
            "/api/testcasemanagement/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Moderation);
app.UseDorkNetPipeline();

app.Run();
