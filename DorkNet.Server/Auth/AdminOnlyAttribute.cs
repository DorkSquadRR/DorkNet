using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Auth;

/// <summary>
/// Action / controller filter that 403s any request whose
/// JWT-resolved player doesn't have <c>IsAdmin == true</c>. Does not
/// itself authenticate — pair it with <c>[Authorize]</c> to ensure a
/// valid bearer is present before this runs.
///
/// Usage:
/// <code>
/// [Authorize]
/// [AdminOnly]
/// [Route("api/admin/v1")]
/// public class AdminController { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class AdminOnlyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // AuthorizationFilterContext.HttpContext is always populated and
        // exposes the same ClaimsPrincipal a controller would see, so
        // there's no reason to round-trip through IHttpContextAccessor
        // (which isn't even registered in this app's DI container — the
        // earlier lookup crashed every admin request with
        // "No service for type IHttpContextAccessor has been registered").
        if (ControllerBaseExtensions.CurrentPlayerId(context.HttpContext.User) is not long id)
        {
            // No JWT → defer to [Authorize]'s 401.
            context.Result = new UnauthorizedResult();
            return;
        }

        var db = context.HttpContext.RequestServices
            .GetRequiredService<DorkNetDbContext>();
        var isAdmin = await db.Players
            .Where(p => p.Id == id)
            .Select(p => p.IsAdmin)
            .FirstOrDefaultAsync();
        if (!isAdmin)
            context.Result = new ForbidResult();
    }
}
