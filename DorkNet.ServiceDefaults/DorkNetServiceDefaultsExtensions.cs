using DorkNet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    public static WebApplication MapDorkNetServiceDefaults(
        this WebApplication app,
        bool mapPublicHealth = true)
    {
        if (mapPublicHealth)
        {
            app.MapHealthChecks("/healthz");
        }

        app.MapGet("/internal/healthz", (DorkNetServiceInfo info) =>
        {
            return Results.Ok(new ServiceHealthResponse(
                info.Name,
                "ok",
                DateTimeOffset.UtcNow));
        });

        return app;
    }

    public static WebApplication UseDorkNetRouteOwnershipGuard(
        this WebApplication app,
        string serviceName)
    {
        app.Use(async (ctx, next) =>
        {
            var domain = Environment.GetEnvironmentVariable("DORKNET_DOMAIN")
                ?? ctx.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>()?["Domain:Apex"]
                ?? "localhost";
            var path = ctx.Request.Path.Value ?? "/";
            if (DorkNetRouteOwnership.IsOwnedBy(serviceName, ctx.Request.Host.Host, path, domain))
            {
                await next();
                return;
            }

            var owner = DorkNetRouteOwnership.ResolvePublicService(ctx.Request.Host.Host, path, domain);
            var logger = ctx.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DorkNet.ServiceDefaults.RouteGuard");
            logger.LogDebug(
                "[route-guard] service={Service} rejected {Method} {Host}{Path}; owner={Owner}",
                serviceName,
                ctx.Request.Method,
                ctx.Request.Host,
                ctx.Request.Path,
                owner);
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "route_not_owned_by_service",
                service = serviceName,
                owner,
            });
        });

        return app;
    }
}
