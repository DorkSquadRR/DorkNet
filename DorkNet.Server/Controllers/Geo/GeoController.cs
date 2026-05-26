using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.Geo;

/// <summary>
/// geo.{apex}/v1/regions — region/server geography. Returns a single
/// configured region (we don't actually run multi-region; the watch's
/// region picker just needs one entry to populate the dropdown).
///
/// The <c>/v1/ping</c> endpoint that previously lived here was
/// removed during the [Host]-strip refactor — both geo and ns
/// answered the same path (NsController.Ping is the canonical
/// one), and with host filters gone the two routes would have
/// ambiguous-matched on any subdomain. NsController wins because
/// its ping body carries the timestamp the keep-alive loop reads;
/// the geo version returned a bare {Ok:true} that nothing consumed.
/// </summary>
[ApiController]
public class GeoController(IConfiguration config) : ControllerBase
{
    [HttpGet("/v1/regions")]
    public ActionResult<List<object>> GetRegions()
    {
        var region = config["Photon:CloudRegion"] ?? "us";
        return Ok(new[]
        {
            new { Id = region, Name = region.ToUpperInvariant(), Ping = 50 },
        });
    }
}
