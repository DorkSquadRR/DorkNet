using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Commerce);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/commerce/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Commerce,
        [
            "catalog",
            "storefronts",
            "econ",
            "inventory",
            "inventions",
        ],
        [
            "/api/catalog/*",
            "/api/storefronts/*",
            "/econ/*",
            "/api/equipment/*",
            "/api/inventions/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Commerce);
app.UseDorkNetPipeline();

app.Run();
