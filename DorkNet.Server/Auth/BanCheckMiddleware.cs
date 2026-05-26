using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using System.Security.Claims;
using System.Text.Json;

namespace DorkNet.Server.Auth;

/// <summary>
/// Short-circuits any authenticated request whose JWT-resolved player has
/// a non-null <c>BannedUntil</c> in the future. Sits AFTER
/// <c>UseAuthentication</c>/<c>UseAuthorization</c> in the pipeline so the
/// claims principal is populated before we look at it.
///
/// Anonymous requests (no/invalid bearer) pass through unchanged — this
/// middleware never short-circuits them. Whether they're allowed is the
/// per-endpoint <c>[Authorize]</c> decision, not ours.
///
/// Lookup is one query per request against the Players table. Cached for
/// the request lifetime via the scoped DbContext, so repeated bearer
/// checks within one request hit the EF first-level cache.
/// </summary>
public class BanCheckMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, DorkNetDbContext db)
    {
        // Cheap early-out for anonymous traffic — no DB hit.
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(sub, out var playerId))
        {
            await next(ctx);
            return;
        }

        // Project just the BannedUntil column — no need to materialise the
        // full player entity for every request.
        var bannedUntil = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.BannedUntil)
            .FirstOrDefaultAsync();

        if (bannedUntil.HasValue && bannedUntil.Value > DateTime.UtcNow)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                Error = "banned",
                Until = bannedUntil.Value.ToString("o"),
            }));
            return;
        }

        await next(ctx);
    }
}
