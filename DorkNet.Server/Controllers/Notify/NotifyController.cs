using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.Notify;

/// <summary>
/// notify.rec.net — legacy raw-WebSocket entrypoint that the 2020 client
/// briefly used before switching to the SignalR hub at
/// <c>notify.rec.net/hub/v1</c>. Kept as a 410 Gone so any straggling
/// boot path that hits it gets a clear "use the hub" signal instead of
/// a silent connection upgrade.
/// </summary>
[ApiController]
public class NotifyController : ControllerBase
{
    [HttpGet("/notify/v1")]
    public IActionResult Connect() => StatusCode(StatusCodes.Status410Gone, new
    {
        error = "use_signalr_hub",
        hint = "connect to /hub/v1 instead",
    });
}
