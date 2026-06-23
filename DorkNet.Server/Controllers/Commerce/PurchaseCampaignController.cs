using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.Commerce;

[ApiController]
public class PurchaseCampaignController : ControllerBase
{
    [HttpGet("/purchasecampaign/allcurrent/v2")]
    [HttpGet("/purchasecampaign/allcurrent")]
    [AllowAnonymous]
    public IActionResult AllCurrent() => Ok(Array.Empty<object>());
}
