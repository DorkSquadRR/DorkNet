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
    /// <summary>Screen-share (photo) report. The 2023-03-21 client posts the
    /// form keys <c>ImageName</c>, <c>ReportedPlayerId</c>, <c>RoomId</c>,
    /// <c>RoomInstanceId</c>, <c>RoomInstanceType</c> and <c>Details</c>
    /// (RecNet.Runtime/KAEAEIODGBG.txt:281-360). It sends no
    /// <c>reportCategory</c> at all.
    ///
    /// The old signature bound <c>targetPlayerId</c>, so the reported player
    /// was stored as 0 on every screen-share report and the reported image was
    /// dropped entirely — the report was unactionable. Form binding is
    /// case-insensitive, so <c>RoomId</c>/<c>Details</c> did land; the names
    /// that differ are the ones spelled out explicitly below.</summary>
    [HttpPost("api/screensharereports/v1/report")]
    public async Task<IActionResult> Report(
        [FromForm(Name = "ReportedPlayerId")] long? reportedPlayerId,
        [FromForm] long? targetPlayerId,
        [FromForm] long? roomId,
        [FromForm(Name = "RoomInstanceId")] long? roomInstanceId,
        [FromForm(Name = "RoomInstanceType")] int? roomInstanceType,
        [FromForm(Name = "ImageName")] string? imageName,
        [FromForm] int? reportCategory,
        [FromForm] string? details)
    {
        var reporter = this.RequireCurrentPlayerId();
        var target = reportedPlayerId
                     ?? targetPlayerId
                     ?? (long.TryParse(Request.Query["ReportedPlayerId"].FirstOrDefault()
                                       ?? Request.Query["targetPlayerId"].FirstOrDefault(), out var qTarget) ? qTarget : 0);
        var room = roomId
                   ?? (long.TryParse(Request.Query["roomId"].FirstOrDefault(), out var qRoom) ? qRoom : 0);
        // The client never sends a category for screen-share reports; 5 is the
        // server's existing "other" bucket.
        var category = reportCategory
                       ?? (int.TryParse(Request.Query["reportCategory"].FirstOrDefault(), out var qCategory) ? qCategory : 5);
        var message = details ?? Request.Query["details"].FirstOrDefault() ?? string.Empty;

        // Keep the evidence (image + instance) in the stored text: ReportEntity
        // has no dedicated columns for them and a report without the offending
        // image is not actionable by a moderator.
        var context = new List<string>();
        if (!string.IsNullOrWhiteSpace(imageName)) context.Add($"image={imageName}");
        if (roomInstanceId is > 0) context.Add($"instance={roomInstanceId}");
        if (roomInstanceType is not null) context.Add($"instanceType={roomInstanceType}");
        if (context.Count > 0) message = $"[screenshare {string.Join(' ', context)}] {message}".Trim();

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
