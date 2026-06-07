using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Version;

[ApiController]
public class VersionController : ControllerBase
{
    // VersionStatus enum: ValidForPlay=0, ValidForMenu=1, UpdateRequired=2
    //
    // Dual-shape response so a single endpoint satisfies both client eras:
    //   • 2020 build reads the int "VersionStatus" (0 == ValidForPlay).
    //   • 2018.06 build (api/versioncheck/v3) reads the bool "ValidVersion"
    //     via a case-sensitive dict index (RecNet.cs BPPMLPMDHCI.Deserialize
    //     -> Util.GetKey<bool>("ValidVersion")). A MISSING key throws
    //     KeyNotFoundException -> the connect coroutine shows "Rec Room
    //     update required" and aborts boot before login. Emitting both keys
    //     keeps each era reading only the field it knows.
    private static readonly object ValidForPlay = new { VersionStatus = 0, ValidVersion = true };

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
