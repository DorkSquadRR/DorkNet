using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Notification.V2;

/// <summary>
/// api/notification/v2 — the JUNE-2018 client opens a raw WebSocket here for
/// push notifications (RecNet.cs:48895, RecNet.IJADBHMBMPG wraps a
/// BestHTTP.WebSocket with open/message(string)/error events). The 2020 client
/// replaced this with the SignalR hub at notify.{apex}/hub/v1.
///
/// This branch targets 2018, so we ACCEPT the WebSocket and hold it open. We
/// don't push anything yet (server-driven notifications are a later feature) —
/// an open, quiet socket keeps the client's notification subsystem "connected"
/// instead of erroring/retry-looping on the old 410 Gone. Incoming frames
/// (subscribe/auth) are drained and ignored.
/// </summary>
[ApiController]
[Route("api/[controller]/v2")]
public class NotificationController(
    DomainConfig domain,
    ILogger<NotificationController> log) : ControllerBase
{
    [HttpGet]
    public async Task Connect()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            // Non-WS probe (or a curl check) — return an empty notification
            // list rather than 410 so HTTP callers don't treat it as an error.
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/json";
            await Response.WriteAsync("[]");
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        log.LogInformation("[notify] 2018 client opened notification WebSocket");
        var buf = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(
                    new ArraySegment<byte>(buf), HttpContext.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "bye", HttpContext.RequestAborted);
                    break;
                }
                // Drain & ignore subscribe/auth frames — no server push yet.
            }
        }
        catch (OperationCanceledException) { /* client navigated away */ }
        catch (WebSocketException) { /* abrupt disconnect — normal */ }
        log.LogDebug("[notify] notification WebSocket closed");
    }

    /// <summary>GET <c>api/notification/hub/v1</c> — 2020 SignalR hub pointer
    /// (unused by the 2018 client; kept for any 2019/2020 caller).</summary>
    [HttpGet("/api/notification/hub/v1")]
    public IActionResult HubInfo() => Ok(new
    {
        Url = $"https://{domain.Sub("notify")}/hub/v1",
        Protocol = "signalr",
        Version = 1,
    });
}
