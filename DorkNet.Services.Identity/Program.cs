using DorkNet.Contracts;
using DorkNet.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServiceDefaults(ServiceNames.Identity);

var app = builder.Build();

app.MapDorkNetServiceDefaults();
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

app.Run();
