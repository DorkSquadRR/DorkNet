using DorkNet.Server.Auth;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.Pageview;

/// <summary>
/// Endpoints behind the watch's <c>COFHGNFJMOG</c> page-view + link
/// system. The 2020.12 client fires <c>POST pageview/consume</c> on
/// every screen change in the boot/menu flow expecting a response
/// that tells it (a) whether there's an in-app deep-link URL to
/// resolve and (b) how long the answer is fresh for. Returning a 404
/// here surfaces as red toast spam in the menus and (per
/// <c>BootSequence.OINIBKFNFAJ</c>) can short-circuit the avatar /
/// inventory bootstrap.
///
/// <para>Response shape verified at
/// <c>Cpp2IL_ISIL/.../MPHABHIMOOO.txt:45,50</c> — the watch's
/// <c>MPHABHIMOOO.PPGFHEDFBEA(Dictionary)</c> reads two camelCase
/// keys:
/// <list type="bullet">
///   <item><c>url</c> — string. Empty means "no deep-link to load".</item>
///   <item><c>freshnessSeconds</c> — int. How long the client can
///   cache this response before re-polling.</item>
/// </list>
/// Returning a long freshness (1 hour) keeps a private server's
/// chattiness low when there's no real linking layer.</para>
///
/// <para>Sibling endpoints from the same watch class
/// (<c>actionlink/*</c>, <c>datalink/*</c>, <c>referral</c>) are out
/// of scope for now — they fire only on explicit user share/click,
/// not in the boot path. Add them as separate routes when they show
/// up in the production log.</para>
/// </summary>
[ApiController]
public class PageviewController(ILogger<PageviewController> logger) : ControllerBase
{
    /// <summary>POST <c>pageview/consume</c> — record a screen
    /// transition. The body is empty in 2020.12.
    /// <c>Promise&lt;MPHABHIMOOO&gt;</c> result; the watch only uses
    /// the <c>url</c> / <c>freshnessSeconds</c> fields. We
    /// intentionally return an empty url + a 1-hour freshness — the
    /// private server has no in-app deep-link system, so there's
    /// nothing to navigate to and no reason to be polled more often.</summary>
    [HttpPost("pageview/consume")]
    [HttpPost("/pageview/consume")]
    [HttpGet("pageview/consume")]
    [HttpGet("/pageview/consume")]
    public IActionResult Consume()
    {
        logger.LogDebug("[pageview] consume callerId={Caller}", this.CurrentPlayerId() ?? 0);
        return Ok(new
        {
            url = string.Empty,
            freshnessSeconds = 3600,
        });
    }
}
