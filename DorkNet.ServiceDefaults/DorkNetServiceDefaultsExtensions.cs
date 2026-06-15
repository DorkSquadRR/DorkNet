using DorkNet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DorkNet.ServiceDefaults;

public static class DorkNetServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddDorkNetServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        builder.Services.AddSingleton(new DorkNetServiceInfo(serviceName));
        builder.Services.AddHealthChecks();
        builder.Services.AddHttpClient();
        builder.Services.ConfigureHttpJsonOptions(opt =>
            opt.SerializerOptions.PropertyNamingPolicy = null);

        return builder;
    }

    public static WebApplication MapDorkNetServiceDefaults(this WebApplication app)
    {
        app.MapHealthChecks("/healthz");
        app.MapGet("/internal/healthz", (DorkNetServiceInfo info) =>
        {
            return Results.Ok(new ServiceHealthResponse(
                info.Name,
                "ok",
                DateTimeOffset.UtcNow));
        });

        return app;
    }
}
