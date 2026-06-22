using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Ns;

#pragma warning disable IDE0290

/// <summary>
/// ns.{apex} — RecNet name server. The very first endpoint the client
/// queries on boot. Returns the service URL map so the client knows
/// where every other subdomain lives. Also serves the bare apex
/// <c>GET /</c> (service catalog) and <c>/v1/ping</c> for the
/// keep-alive loop. Apex is read from <see cref="DomainConfig"/>
/// (driven by <c>DORKNET_DOMAIN</c>) so every URL the client sees
/// flows through one source of truth.
/// </summary>
[ApiController]
public class NsController : ControllerBase
{
    private readonly PlayerService _playerService;
    private readonly DomainConfig _domain;
    public NsController(PlayerService playerService, DomainConfig domain)
    {
        _playerService = playerService;
        _domain = domain;
    }

    // Root: service URL discovery (primary boot call from RecNet.Core.ConnectToRecNet)
    // The game's TrySetServiceUri parses this as a flat { "ServiceName": "https://..." } dictionary.
    // Wrapping it breaks lookup — all URIs come back null which crashes EnsureEndsWith.
    //
    // Order is more-negative than CdnController.Serve's wildcard route
    // (Order = -100) because the catch-all <c>{*path}</c> matches the
    // empty path "/" too — without this, GET ns.{apex}/ falls into the
    // CDN serve handler and the client gets binary back instead of the
    // service-map JSON, triggering "Failed to connect to RecNet (error
    // code: locker)" in BootSequence.
    [HttpGet("/", Order = -1000)]
    public ActionResult<Dictionary<string, string>> Root() =>
        Ok(ConfigService.BuildServiceUrlMap(_domain));

    // Some builds fetch /v1 or /v1/services directly
    [HttpGet("/v1")]
    [HttpGet("/v1/services")]
    public ActionResult<Dictionary<string, string>> Services() =>
        Ok(ConfigService.BuildServiceUrlMap(_domain));

    // Ping / health used by the client keep-alive loop
    [HttpGet("/v1/ping")]
    [HttpGet("/ping")]
    public ActionResult<object> Ping()
        => Ok(new { Status = "OK", Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    // Player name → accountId lookup (used for friend invites etc.)
    [HttpGet("/v1/player/{name}")]
    public async Task<IActionResult> PlayerLookup(string name)
    {
        var p = await _playerService.GetByUsernameAsync(name);
        return Ok(p is null
            ? new { Username = name, AccountId = 0L, Found = false }
            : new { Username = p.Username, AccountId = p.Id, Found = true });
    }
}
