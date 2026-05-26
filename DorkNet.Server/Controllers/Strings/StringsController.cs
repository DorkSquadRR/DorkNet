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
}
