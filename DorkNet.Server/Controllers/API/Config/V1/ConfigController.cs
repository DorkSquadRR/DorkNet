using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Config.V1;

[ApiController]
[Route("api/[controller]/v1")]
public class ConfigController(ConfigService configService) : ControllerBase
{
    [HttpGet("motd")]
    public ActionResult<string> GetMotd()
    {
        var cfg = configService.GetConfig($"{Request.Scheme}://{Request.Host}");
        return Ok(cfg.MessageOfTheDay);
    }

    [HttpGet("objectives")]
    public ActionResult<List<object>> GetObjectives() => Ok(new List<object>());

    // AmplitudeConfig.Deserialize expects key "AmplitudeKey"
    [HttpGet("amplitude")]
    public ActionResult<object> GetAmplitude() => Ok(new { AmplitudeKey = string.Empty });

    /// <summary>
    /// `Config.GetCohortNUXButtonConfigs(cohortId)` — returns the four
    /// "What would you like to do first?" buttons shown in the OOBE
    /// (RegistrationSceneNUX → ChooseCohortScreen). Each button maps to
    /// a room the new player can teleport into.
    ///
    /// Wire shape per CohortNUXButtonConfig (TypeDefIndex 11516) — list of:
    ///   Version          (int)
    ///   ButtonNumber     (int, 0..3 → Button1..Button4)
    ///   Override         (int, 0=None, 1=Hotlist, 2=Custom)
    ///   CustomRoomName   (string)
    ///   CustomTitle      (string)
    ///   DefaultRoomName  (string)
    ///   DefaultTitle     (string)
    ///
    /// The screen picks `CustomRoomName` if Override=Custom, else the
    /// `DefaultRoomName` (resolved by Rooms.GetByName). For simplicity all
    /// four use Override=None and route to Rec Room Original rooms we
    /// already seeded — picking the most newcomer-friendly ones.
    /// </summary>
    [HttpGet("cohortnux/{cohortId:int}")]
    public IActionResult GetCohortNux(int cohortId) => Ok(new[]
    {
        new {
            Version = 1, ButtonNumber = 0, Override = 0,
            CustomRoomName = "", CustomTitle = "",
            DefaultRoomName = "RecCenter",
            DefaultTitle = "Hang out at the Rec Center",
        },
        new {
            Version = 1, ButtonNumber = 1, Override = 0,
            CustomRoomName = "", CustomTitle = "",
            DefaultRoomName = "Paintball",
            DefaultTitle = "Play Paintball",
        },
        new {
            Version = 1, ButtonNumber = 2, Override = 0,
            CustomRoomName = "", CustomTitle = "",
            DefaultRoomName = "Dodgeball",
            DefaultTitle = "Play Dodgeball",
        },
        new {
            Version = 1, ButtonNumber = 3, Override = 0,
            CustomRoomName = "", CustomTitle = "",
            DefaultRoomName = "GoldenTrophy",
            DefaultTitle = "Quest for the Golden Trophy",
        },
    });
}
