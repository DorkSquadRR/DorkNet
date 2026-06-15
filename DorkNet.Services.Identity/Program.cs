using DorkNet.Contracts;
using DorkNet.ServiceDefaults;
using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Identity);
builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.MapDorkNetServiceDefaults(mapPublicHealth: false);
app.MapGet("/internal/identity/capabilities", () =>
    Results.Ok(new ServiceCapabilityResponse(
        ServiceNames.Identity,
        [
            "accounts",
            "auth",
            "platform-login",
            "jwt-issuance",
        ],
        [
            "/account/*",
            "/api/account/*",
            "/api/platformlogin/*",
            "/api/auth/*",
        ])));

app.UseDorkNetRouteOwnershipGuard(ServiceNames.Identity);
app.UseDorkNetPipeline();

app.Run();
