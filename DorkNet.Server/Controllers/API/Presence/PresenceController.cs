using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.Players;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Presence;

/// <summary>
/// api/presence/* — the June-2018 client's friend-presence poll (every ~30s,
/// RecNet.cs:63249). POST api/presence/v2/list takes a JSON array of account
/// ids and returns one <see cref="Presence2018"/> per id. There is no 2020
/// equivalent on this host (the 2020 client used match.rec.net/player), so this
/// controller is 2018-specific. Online state comes from
/// <see cref="OnlinePresenceService"/>; GameSession is left null for now (the
/// client renders "online" without it and fills in room info on join).
/// </summary>
[ApiController]
[Authorize]
public class PresenceController(OnlinePresenceService presence) : ControllerBase
{
    [HttpPost("/api/presence/v2/list")]
    [HttpPost("/api/presence/v1/list")]
    public ActionResult<List<Presence2018>> List([FromBody] long[]? ids)
    {
        if (ids is null || ids.Length == 0) return Ok(new List<Presence2018>());
        var online = presence.OnlinePlayerIds().ToHashSet();
        var result = ids.Distinct().Select(id => new Presence2018
        {
            PlayerId = id,
            IsOnline = online.Contains(id),
            GameSession = null,
        }).ToList();
        return Ok(result);
    }
}
