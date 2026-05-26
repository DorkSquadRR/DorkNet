using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Version;

[ApiController]
public class VersionController : ControllerBase
{
    // VersionStatus enum: ValidForPlay=0, ValidForMenu=1, UpdateRequired=2
    private static readonly object ValidForPlay = new { VersionStatus = 0 };

    [HttpGet("api/version/v1")]
    [HttpGet("api/version")]
    public ActionResult<object> GetV1() => Ok(ValidForPlay);

    // Actual path the 2020 build uses
    [HttpGet("api/versioncheck/v4")]
    [HttpGet("api/versioncheck/v3")]
    [HttpGet("api/versioncheck/v2")]
    [HttpGet("api/versioncheck/v1")]
    [HttpGet("api/versioncheck")]
    public ActionResult<object> GetCheck() => Ok(ValidForPlay);
}
