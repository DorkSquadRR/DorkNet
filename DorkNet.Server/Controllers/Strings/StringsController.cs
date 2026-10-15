using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.Strings;

/// <summary>
/// strings.rec.net — game localisation strings.
/// Returns an empty table so the client falls back to its bundled defaults.
/// </summary>
[ApiController]
public class StringsController : ControllerBase
{
    [HttpGet("/strings/v1")]
    public ActionResult<Dictionary<string, string>> GetStrings() => Ok(new Dictionary<string, string>());

    [HttpGet("/strings/v1/{locale}")]
    public ActionResult<Dictionary<string, string>> GetLocale(string locale)
        => Ok(new Dictionary<string, string>());

    /// <summary>The 2023 client's server-string download (RecNet.Runtime/NHILNDLLLHH.txt).
    /// Two GETs share the path <c>/strings</c> on the strings host:
    /// <list type="bullet">
    /// <item><c>?locale=&amp;scopePrefix=client.&amp;eTag=</c> (FMLBOAOBKPM, :531) →
    /// <c>Dictionary&lt;string, Dictionary&lt;long,string&gt;&gt;</c>, scope → id → text;</item>
    /// <item><c>?locale=&amp;scope=client.{name}&amp;eTag=</c> (BNEELKAKCEN, :3) →
    /// <c>Dictionary&lt;long,string&gt;</c>.</item>
    /// </list>
    /// The localisation service stores the first answer
    /// (RecRoom.Localization.Runtime/IBAJDNIMADI.txt BICKHBAJPIG) and its scope lookup
    /// (NHLKEJLGGIE) null-derefs when the download never succeeded but returns an empty
    /// table for a scope the server did not send. DorkNet keeps no server-side string
    /// overrides, so the truthful answer is an empty JSON object for either query —
    /// the lookups then fall back to the bundled text. The path used to 404.</summary>
    [HttpGet("/strings")]
    public IActionResult GetServerStrings() => Content("{}", "application/json");
}
