using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Web);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/web/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Web,
        [
            "public-site",
            "admin-site",
            "feed-site",
            "site-api",
        ],
        [
            "/",
            "www.*",
            "admin.*",
            "feed.*",
            "/api/site/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Web);
app.UseDorkNetPipeline();

app.Run();
