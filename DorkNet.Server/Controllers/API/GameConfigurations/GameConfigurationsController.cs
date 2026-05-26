using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.GameConfigurations;

[ApiController]
public class GameConfigurationsController : ControllerBase
{
    // Actual path used by the 2020 build
    [HttpGet("api/gameconfigs/v1/all")]
    [HttpGet("api/gameconfigs/v1")]
    // Legacy path we originally assumed
    [HttpGet("api/gameconfigurations/v1/all")]
    [HttpGet("api/gameconfigurations/v1")]
    public ActionResult<List<object>> GetAll() => Ok(new List<object>());
}
