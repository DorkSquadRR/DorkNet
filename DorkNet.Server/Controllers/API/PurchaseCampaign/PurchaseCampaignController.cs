using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.PurchaseCampaign;

/// <summary>
/// Purchase-campaign impression telemetry. Client contract (RecNet.Runtime
/// <c>JHAEJJEGPKJ(int)</c>) is a fire-and-forget POST carrying the campaign
/// id; the return type is the response handle whose body the client ignores.
/// DorkNet doesn't run ad/purchase campaigns, so there's nothing to record —
/// we acknowledge the impression so the client's promise resolves instead of
/// 404-rejecting. This is a genuine no-op telemetry sink, not a stub masking
/// an unimplemented feature.
/// </summary>
[ApiController]
[Authorize]
public class PurchaseCampaignController : ControllerBase
{
    [HttpPost("/purchasecampaign/shown")]
    [HttpPost("/purchasecampaign/shown/{campaignId:int}")]
    public IActionResult Shown() => Ok();
}
