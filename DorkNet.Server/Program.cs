using DorkNet.Server.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Rewrite;

var builder = WebApplication.CreateBuilder(args);

Stella.Signatures.Init();

// Personal/local-machine override layer. Loaded last so it wins over both
// appsettings.json and appsettings.{Environment}.json. Gitignored — use
// this for Photon AppId, JWT secret, and anything you don't want committed.
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

builder.AddDorkNetServices();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;

    if (!string.IsNullOrEmpty(path) && path.StartsWith("//video/", StringComparison.Ordinal))
    {
        context.Request.Path = path[1..]; // Remove one leading slash
    }

    await next();
});

app.UseRouting();

await app.RunDatabaseBootstrapAsync();
app.UseDorkNetPipeline();
app.Run();

public partial class Program;
