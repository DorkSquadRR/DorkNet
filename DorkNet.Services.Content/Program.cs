using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Content);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/content/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Content,
        [
            "cdn",
            "images",
            "photos",
            "room-blobs",
            "storage",
        ],
        [
            "/api/images/*",
            "/api/photos/*",
            "/data/*",
            "/img/*",
            "/upload",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Content);
app.UseDorkNetPipeline();

app.Run();
