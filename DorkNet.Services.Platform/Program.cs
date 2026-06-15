using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Platform);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/platform/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Platform,
        [
            "service-directory",
            "config",
            "version-check",
            "geo",
            "strings",
        ],
        [
            "/v1/services",
            "/api/config/*",
            "/api/versioncheck/*",
            "/v1/regions",
            "/strings/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Platform);
app.UseDorkNetPipeline();

app.Run();
