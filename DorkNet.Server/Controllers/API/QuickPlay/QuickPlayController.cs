using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.QuickPlay;

/// <summary>
/// api.rec.net/api/quickPlay/* — the watch's "Quick Play" tile picks
/// a popular open AG room when the player wants to jump in
/// somewhere social without browsing.
///
/// • <c>GET v{1,2}/getandclear</c> — returns the next match the
///   user was queued for, then clears it. We just synthesise a
///   "join hot AG room" target on each call (no real queue).
/// • <c>POST v1/set</c> — stores a desired-match tag. We log + ack;
///   no persistence needed for a single-region private server.
/// </summary>
[ApiController]
public class QuickPlayController(DorkNetDbContext db) : ControllerBase
{
    /// <summary>GET <c>v{1,2}/getandclear</c> — the watch polls this
    /// on every connect / navigation to check whether the user has a
    /// pending quickplay queue. Returning a real {RoomId, RoomName}
    /// makes the client treat it as "you queued, go there now" and
    /// immediately teleports — that's WHY users were randomly
    /// spawning in Paddleball etc.
    ///
    /// Correct behaviour: return empty {} unless we actually have a
    /// queued match for the caller. The /set endpoint is what
    /// queues; getandclear is the consume.
    ///
    /// We don't persist queue state on a single-region private
    /// server (no real matchmaking pool), so always return empty.</summary>
    [HttpGet("api/quickPlay/v1/getandclear")]
    [HttpGet("api/quickPlay/v2/getandclear")]
    public IActionResult GetAndClear() => Ok(new { });

    public sealed class SetQuickPlayRequest
    {
        public string? Tag { get; set; }
    }

    [HttpPost("api/quickPlay/v1/set")]
    [Authorize]
    public IActionResult Set([FromBody] SetQuickPlayRequest? req)
    {
        // Preference is per-session; we don't persist (no benefit in
        // a small private deployment). Just ack.
        return Ok(new { Tag = req?.Tag ?? string.Empty, Set = true });
    }
}
