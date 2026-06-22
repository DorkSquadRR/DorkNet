using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.AppIntegrity;

[ApiController]
public class AppIntegrityController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/AppIntegrity/v1/iosproducts")]
    [AllowAnonymous]
    public async Task<IActionResult> IosProducts()
    {
        var rows = await db.StoreItems
            .Where(i => i.IsActive)
            .OrderBy(i => i.Id)
            .Take(100)
            .Select(i => new
            {
                ProductId = i.Slug,
                i.Price,
                i.CurrencyType,
                Name = i.DisplayName,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpPost("api/AppIntegrity/v1/iospaymentqueuefailed")]
    [Authorize]
    public async Task<IActionResult> IosPaymentQueueFailed([FromForm] string? reason)
    {
        db.BugReports.Add(new BugReportEntity
        {
            ReporterPlayerId = this.RequireCurrentPlayerId(),
            Title = "iOS payment queue failed",
            Body = reason ?? Request.Query["reason"].FirstOrDefault() ?? string.Empty,
            Category = "appintegrity",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true });
    }
}
