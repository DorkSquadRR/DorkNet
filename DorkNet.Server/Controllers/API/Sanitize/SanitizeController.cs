using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Sanitize;

/// <summary>
/// api.{rec.net,localhost}/api/sanitize/* — server-side profanity filter
/// the watch's text-input UI calls before allowing a room name /
/// invention name / chat message to be submitted. Delegates to
/// <see cref="ProfanityFilter"/> for the actual scrubbing. Response
/// shape: <c>{Text (purified), IsClean (bool)}</c>.
/// </summary>
[ApiController]
public class SanitizeController(ILogger<SanitizeController> logger) : ControllerBase
{
    [HttpPost("api/sanitize/v1")]
    [HttpPost("api/sanitize/v1/text")]
    public async Task<IActionResult> Sanitize()
    {
        // RecRoom.StringSanitization.PurifyString sends JSON body
        // {"Value":"<text>","ReplacementChar":42}  — verified from a
        // live chat-send error trace ("Received malformed RecNet
        // response: ...Text:{\"Value\":...}..."). Earlier we used to
        // pass the whole body through as "text", which made Text in
        // the response contain the literal JSON string, and the watch's
        // LitJson then choked on the malformed wrap.
        string text = string.Empty;
        try
        {
            if (Request.HasFormContentType)
                text = Request.Form["Text"].ToString();
        }
        catch { }
        if (string.IsNullOrEmpty(text))
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(raw))
            {
                var first = raw.AsSpan().TrimStart();
                if (first.Length > 0 && first[0] == '{')
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(raw);
                        var root = doc.RootElement;
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (root.TryGetProperty("Value", out var vEl) && vEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                text = vEl.GetString() ?? string.Empty;
                            else if (root.TryGetProperty("Text", out var tEl) && tEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                text = tEl.GetString() ?? string.Empty;
                            else if (root.TryGetProperty("Input", out var iEl) && iEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                text = iEl.GetString() ?? string.Empty;
                        }
                    }
                    catch { text = raw; }
                }
                else
                {
                    text = raw;
                }
            }
        }
        // StringSanitization.PurifyString_d__9 chains Core.Post to
        // ApiCallback<String> via ExpectPrimitiveResponse, so the watch's
        // LitJson importer wants a BARE JSON string (e.g. "dwadwa"), not
        // an object body. Returning {Text, IsClean} as we did before
        // makes LitJson hand the Dictionary to Convert.ChangeType(_, typeof(string))
        // which throws "Object must implement IConvertible". Strip the
        // envelope — but use Content() with explicit application/json so
        // ASP.NET Core's StringOutputFormatter doesn't serialize as
        // text/plain (raw string sans quotes), which the watch then sees
        // as an empty cleanVersion after LitJson decode.
        var purified = ProfanityFilter.Purify(text);
        var json = System.Text.Json.JsonSerializer.Serialize(purified);
        logger.LogInformation("[sanitize] input=\"{Input}\" purified=\"{Purified}\" json={Json}", text, purified, json);
        return Content(json, "application/json");
    }

    public sealed class SanitizeBody
    {
        public string? Text { get; set; }
        public string? Input { get; set; }
        public string? Value { get; set; }
    }

    /// <summary>POST <c>api/sanitize/v1/purifyString</c> — returns
    /// <c>{Text}</c> with profanity replaced. Wire shape matches
    /// <c>StringSanitization.PurifyString</c>.</summary>
    [HttpPost("api/sanitize/v1/purifyString")]
    public IActionResult PurifyString(
        [FromBody] SanitizeBody? body,
        [FromForm(Name = "Text")] string? textForm,
        [FromForm(Name = "Input")] string? inputForm,
        [FromForm(Name = "Value")] string? valueForm,
        [FromQuery(Name = "text")] string? textQuery)
    {
        var input = body?.Text ?? body?.Input ?? body?.Value
            ?? textForm ?? inputForm ?? valueForm ?? textQuery ?? string.Empty;
        return Ok(new { Text = ProfanityFilter.Purify(input) });
    }

    /// <summary>POST <c>api/sanitize/v1/requestIsStringPure</c> —
    /// returns a bare JSON bool. Watch's <c>ExpectPrimitiveResponse</c>
    /// rejects object bodies here.</summary>
    [HttpPost("api/sanitize/v1/requestIsStringPure")]
    public IActionResult IsStringPure(
        [FromBody] SanitizeBody? body,
        [FromForm(Name = "Text")] string? textForm,
        [FromQuery(Name = "text")] string? textQuery)
    {
        var input = body?.Text ?? body?.Input ?? body?.Value
            ?? textForm ?? textQuery ?? string.Empty;
        return Ok(ProfanityFilter.IsClean(input));
    }
}
