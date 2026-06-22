using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.ScreenShareReports;

[ApiController]
[Authorize]
public class ScreenShareReportsController(DorkNetDbContext db) : ControllerBase
{
    [HttpPost("api/screensharereports/v1/report")]
    public async Task<IActionResult> Report(
        [FromForm] long? targetPlayerId,
        [FromForm] long? roomId,
        [FromForm] int? reportCategory,
        [FromForm] string? details)
    {
        var reporter = this.RequireCurrentPlayerId();
        var target = targetPlayerId
                     ?? (long.TryParse(Request.Query["targetPlayerId"].FirstOrDefault(), out var qTarget) ? qTarget : 0);
        var room = roomId
                   ?? (long.TryParse(Request.Query["roomId"].FirstOrDefault(), out var qRoom) ? qRoom : 0);
        var category = reportCategory
                       ?? (int.TryParse(Request.Query["reportCategory"].FirstOrDefault(), out var qCategory) ? qCategory : 5);
        var message = details ?? Request.Query["details"].FirstOrDefault() ?? string.Empty;

        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = reporter,
            TargetPlayerId = target,
            RoomId = room,
            Category = category,
            Message = message[..Math.Min(1000, message.Length)],
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Error = string.Empty });
    }
}
