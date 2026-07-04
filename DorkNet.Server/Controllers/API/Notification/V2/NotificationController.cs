using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Notification.V2;

/// <summary>
/// Legacy raw-WebSocket notification endpoint. Replaced by the SignalR
/// hub at <c>notify.{apex}/hub/v1</c>. Returns 410 Gone for any client
/// that still tries the old path.
/// </summary>
[ApiController]
[Route("api/[controller]/v2")]
public class NotificationController(DomainConfig domain) : ControllerBase
{
    [HttpGet]
    public IActionResult Connect() => StatusCode(StatusCodes.Status410Gone, new
    {
        error = "use_signalr_hub",
        hint = $"connect to {domain.Sub("notify")}/hub/v1 instead",
    });

    /// <summary>GET <c>api/notification/hub/v1</c> — returns the
    /// SignalR hub URL the client should open. Without this the
    /// watch falls back to its own default and may try the wrong
    /// scheme. URL is built off the configured deployment apex
    /// (DORKNET_DOMAIN).</summary>
    [HttpGet("/api/notification/hub/v1")]
    public IActionResult HubInfo() => Ok(new
    {
        Url = $"https://{domain.Sub("notify")}/hub/v1",
        Protocol = "signalr",
        Version = 1,
    });
}
