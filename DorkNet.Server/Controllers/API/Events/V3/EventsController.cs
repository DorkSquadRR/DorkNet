using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Events.V3;

[ApiController]
[Route("api/[controller]/v3")]
public class EventsController : ControllerBase
{
    [HttpGet("list")]
    public ActionResult<List<object>> GetActiveEvents() => Ok(new List<object>());
}
