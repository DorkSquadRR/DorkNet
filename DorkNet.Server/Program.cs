using DorkNet.Server.Startup;

var builder = WebApplication.CreateBuilder(args);

// Personal/local-machine override layer. Loaded last so it wins over both
// appsettings.json and appsettings.{Environment}.json. Gitignored — use
// this for Photon AppId, JWT secret, and anything you don't want committed.
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServices();

var app = builder.Build();

await app.RunDatabaseBootstrapAsync();
app.UseDorkNetPipeline();
app.Run();
