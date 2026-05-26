using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Activities;

/// <summary>
/// api.{rec.net,localhost}/api/activities/* — per-activity content
/// (charades words, party-game prompts, etc.). The 2020 watch
/// fetches these as a static list at room-join time; we ship an
/// empty list so the activity's bundled defaults kick in.
/// </summary>
[ApiController]
public class ActivitiesController : ControllerBase
{
    [HttpGet("api/activities/charades/v1/words")]
    public IActionResult CharadesWords() => Ok(Array.Empty<object>());
}
