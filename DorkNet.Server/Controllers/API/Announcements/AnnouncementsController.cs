using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Announcements;

/// <summary>
/// api.{rec.net,localhost}/api/announcement/v{1,2}/get — fetches the
/// current server announcement (shown on the watch's home tab). The
/// payload is sourced from <see cref="CommunityBoardService"/> so the
/// admin SPA's "edit announcement" form is the single place to
/// update what players see.
///
/// Wire shape per <c>RecNet/AnnouncementDTO.cs</c> — AnnouncementId,
/// AnnouncementType, LinkType, Platform are all required via
/// <c>Util.GetKey</c> (throws on miss); Title/Body/ImageName/LinkName/
/// LinkUri/CreatedAt are GetKeyOrDefault.
/// </summary>
[ApiController]
public class AnnouncementsController(CommunityBoardService communityBoard) : ControllerBase
{
    [HttpGet("api/announcement/v1/get")]
    [HttpGet("api/announcement/v2/get")]
    public async Task<IActionResult> Current()
    {
        var board = await communityBoard.GetAsync();
        var ann = board.CurrentAnnouncement;
        if (ann is null) return Ok(Array.Empty<object>());
        return Ok(new[]
        {
            new
            {
                AnnouncementId = 1L,
                AnnouncementType = 0,
                Title = "Server Announcement",
                Body = ann.Message ?? string.Empty,
                ImageName = "",
                LinkType = 0,
                LinkName = "",
                LinkUri = ann.MoreInfoUrl ?? string.Empty,
                Platform = 0,
                CreatedAt = DateTime.UtcNow,
            }
        });
    }
}
