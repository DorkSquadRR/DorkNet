using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.GameConfigurations;

[ApiController]
public class GameConfigurationsController(ServerSettingsService serverSettings) : ControllerBase
{
    // Actual path used by the 2020 build
    [HttpGet("api/gameconfigs/v1/all")]
    [HttpGet("api/gameconfigs/v1")]
    // Legacy path we originally assumed
    [HttpGet("api/gameconfigurations/v1/all")]
    [HttpGet("api/gameconfigurations/v1")]
    public async Task<ActionResult<IReadOnlyList<GameConfigurationSetting>>> GetAll()
        => Ok(await serverSettings.GetGameConfigurationsAsync());
}
