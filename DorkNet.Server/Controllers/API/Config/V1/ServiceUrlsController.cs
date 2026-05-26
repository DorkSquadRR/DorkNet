using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Config.V1;

/// <summary>
/// Returns the full Rec Room service URL map. Some builds fetch this standalone
/// at /api/config/v1/services to discover where the other subdomains live.
/// Apex comes from <see cref="DomainConfig"/> (DORKNET_DOMAIN), so a single
/// env var controls every URL emitted.
/// </summary>
[ApiController]
[Route("api/config/v1")]
public class ServiceUrlsController(DomainConfig domain) : ControllerBase
{
    [HttpGet("services")]
    public ActionResult<Dictionary<string, string>> Get()
        => Ok(ConfigService.BuildServiceUrlMap(domain.Apex));
}
