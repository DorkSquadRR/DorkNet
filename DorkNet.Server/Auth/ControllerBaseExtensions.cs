using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DorkNet.Server.Auth;

/// <summary>
/// Single canonical place for "who is the bearer-resolved player on this
/// request." Replaces the per-controller copy-paste of:
/// <code>
///   private long CurrentPlayerId =>
///       long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
/// </code>
/// which historically existed in 10 controllers and silently NPE-bombed
/// the action when the bearer was missing instead of returning 401.
/// </summary>
public static class ControllerBaseExtensions
{
    /// <summary>
    /// Returns the bearer-resolved player id, or null when the caller is
    /// anonymous / the bearer is malformed. Use on optional-auth endpoints.
    /// </summary>
    public static long? CurrentPlayerId(this ControllerBase controller) =>
        CurrentPlayerId(controller.User);

    /// <summary>Overload for non-controller callers (filters,
    /// middleware) that hold a <see cref="ClaimsPrincipal"/> directly.</summary>
    public static long? CurrentPlayerId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// Returns the bearer-resolved player id; throws
    /// <see cref="UnauthorizedAccessException"/> when missing. Use on
    /// <c>[Authorize]</c>-decorated endpoints where a missing id is a
    /// programming error (the framework should have already 401'd).
    /// </summary>
    public static long RequireCurrentPlayerId(this ControllerBase controller) =>
        controller.CurrentPlayerId()
        ?? throw new UnauthorizedAccessException(
            "Bearer token claim 'NameIdentifier' is missing or non-numeric. " +
            "If this fired on an [Authorize] endpoint, JWT validation upstream " +
            "is misconfigured.");
}
