using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.Config;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Config.V2;

[ApiController]
[Route("api/config")]
[Route("api/[controller]/v2")]
public class ConfigController(ConfigService configService) : ControllerBase
{
    [HttpGet]
    public ActionResult<RecRoomConfig> GetConfig()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(configService.GetConfig(baseUrl));
    }
}
