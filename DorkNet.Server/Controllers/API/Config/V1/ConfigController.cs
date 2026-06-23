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
    /// GET <c>api/config/v1/backtrace</c> — 2023 boot downloads this before
    /// audio/core initialization. Returning 404 leaves the static backtrace
    /// config null and later crashes <c>ConfigSettings</c> consumers. Keep
    /// crash upload/deobfuscation disabled locally while preserving the
    /// object shape and readable config keys the client inspects.
    /// </summary>
    [HttpGet("backtrace")]
    public ActionResult<object> GetBacktrace(
        [FromQuery(Name = "platformType")] string? platformType,
        [FromQuery(Name = "allocate")] bool? allocate)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var submissionUrl = $"{baseUrl}/backtrace/submit";
        var minidumpSubmissionUrl = $"{baseUrl}/backtrace/minidump";
        var config = new Dictionary<string, object>
        {
            ["Backtrace.SampleRate"] = 0.0,
            ["Backtrace.ReportBudget"] = 0,
            ["Backtrace.deobfuscationRate"] = 0.0,
            ["Backtrace.Deobfuscate"] = false,
            ["Backtrace.DeobfuscationUrl"] = string.Empty,
            ["Backtrace.Enabled"] = false,
            ["Backtrace.PlatformType"] = platformType ?? string.Empty,
            ["Backtrace.Allocate"] = allocate ?? false,
        };

        return Ok(new Dictionary<string, object>
        {
            ["MaxMessagesPerMinute"] = 0,
            ["MaxReportsPerMinute"] = 0,
            ["SampleRate"] = 0.0,
            ["ReportBudget"] = 0,
            ["ErrorReportBudget"] = 0,
            ["WarningReportBudget"] = 0,
            ["LogReportBudget"] = 0,
            ["SubmissionUrl"] = submissionUrl,
            ["MinidumpSubmissionUrl"] = minidumpSubmissionUrl,
            ["ServerUrl"] = submissionUrl,
            ["DatabasePath"] = "backtrace/database",
            ["Config"] = config,
            ["maxMessagesPerMinute"] = 0,
            ["maxReportsPerMinute"] = 0,
            ["sampleRate"] = 0.0,
            ["reportBudget"] = 0,
            ["errorReportBudget"] = 0,
            ["warningReportBudget"] = 0,
            ["logReportBudget"] = 0,
            ["submissionUrl"] = submissionUrl,
            ["minidumpSubmissionUrl"] = minidumpSubmissionUrl,
            ["serverUrl"] = submissionUrl,
            ["databasePath"] = "backtrace/database",
            ["config"] = config,
        });
    }

    [HttpGet("azurespeech")]
    public IActionResult GetAzureSpeech()
    {
        return Ok(new Dictionary<string, object>
        {
            ["Enabled"] = false,
            ["SpeechRecognitionEnabled"] = false,
            ["SpeechSynthesisEnabled"] = false,
            ["Region"] = string.Empty,
            ["SubscriptionKey"] = string.Empty,
            ["TokenEndpoint"] = string.Empty,
            ["SpeechRecognitionEndpoint"] = string.Empty,
            ["SpeechSynthesisEndpoint"] = string.Empty,
            ["enabled"] = false,
            ["speechRecognitionEnabled"] = false,
            ["speechSynthesisEnabled"] = false,
            ["region"] = string.Empty,
            ["subscriptionKey"] = string.Empty,
            ["tokenEndpoint"] = string.Empty,
            ["speechRecognitionEndpoint"] = string.Empty,
            ["speechSynthesisEndpoint"] = string.Empty,
        });
    }

    [HttpGet("/voice/config")]
    public IActionResult GetVoiceConfig()
    {
        return Ok(new Dictionary<string, object>
        {
            ["ToxMod.Enabled"] = false,
            ["ToxMod.SampleRate"] = 0.0,
            ["ToxMod.SessionRecordingEnabled"] = false,
            ["ToxMod.Endpoint"] = string.Empty,
            ["ToxMod.ApiKey"] = string.Empty,
            ["Enabled"] = false,
            ["enabled"] = false,
            ["toxModEnabled"] = false,
        });
    }

    /// <summary>GET <c>api/config/v1/freegiftbutton</c> — the
    /// 2020.12 client deserializes this endpoint as a raw Boolean.
    /// This server does not run the timed free-gift campaign, so the
    /// button is hidden.</summary>
    [HttpGet("freegiftbutton")]
    public IActionResult FreeGiftButton() => Content("false", "application/json");

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
