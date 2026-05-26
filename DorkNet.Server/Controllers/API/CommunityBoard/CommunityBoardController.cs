using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.CommunityBoard;

/// <summary>
/// api.rec.net/api/communityboard/v1/current — backs the dorm's
/// community board. The watch's
/// <c>RecNet.CommunityBoard.GetCurrent</c> hits
/// <c>"{api/communityboard/}v1/current"</c>
/// (Cpp2IL_ISIL/.../CommunityBoard.txt:902) and parses the response as
/// a <c>CommunityBoardDTO</c> with FeaturedPlayer / FeaturedRoomGroup /
/// CurrentAnnouncement / InstagramImages / Videos
/// (CommunityBoard_NestedType_CommunityBoardDTO.txt:460-480).
///
/// State lives in <see cref="CommunityBoardService"/> as a single
/// DB row keyed at <c>CommunityBoardRows.Id = 1</c>. Admins update it
/// via <c>POST /api/admin/v1/communityboard</c> from the admin UI.
/// </summary>
[ApiController]
public class CommunityBoardController(
    CommunityBoardService board,
    DorkNetDbContext db,
    DomainConfig domain) : ControllerBase
{
    [HttpGet("/api/communityboard/v1/current")]
    [HttpGet("/api/communityboard/v2/current")]
    public async Task<IActionResult> Current()
    {
        var state = await board.GetAsync();

        // InstagramImage URL backfill — the watch's
        // InstagramImageDTO.Deserialize calls a string-typed GetKey for
        // both ImageName AND ImageUrl
        // (CommunityBoard_NestedType_InstagramImageDTO.txt:85-91); an
        // empty URL yields a broken tile. The admin SPA persists only
        // ImageName, so we derive the URL on read against the configured
        // deployment apex (DORKNET_DOMAIN).
        var instagram = state.InstagramImages.Select(img => new
        {
            ImageName = img.ImageName,
            ImageUrl = string.IsNullOrEmpty(img.ImageUrl) && !string.IsNullOrEmpty(img.ImageName)
                ? $"https://{domain.Sub("img")}/{img.ImageName}"
                : img.ImageUrl,
        }).ToArray();

        // FeaturedRoomGroup expansion — the watch's FeaturedRoomGroupDTO
        // (Rooms_NestedType_FeaturedRoomGroupDTO.txt:75-93) reads
        // FeaturedRooms as <c>List&lt;FeaturedRoom&gt;</c> where each
        // FeaturedRoom is <c>{RoomName, RoomId, ImageName}</c> with
        // RoomId required (Util.GetKey at FeaturedRoom.txt:415-417).
        // The admin SPA persists the list as a bare <c>List&lt;long&gt;</c>
        // of room ids — which the watch can't deserialize, throwing
        // KeyNotFoundException for "RoomId" on each item. With the list
        // throwing, GetCurrentCommunityBoardData fails and the whole
        // community-board panel renders empty. Expand the ids to full
        // FeaturedRoom shapes here so the wire payload deserialises.
        object? featuredRoomGroup = null;
        if (state.FeaturedRoomGroup is { } group)
        {
            var ids = group.FeaturedRooms.ToList();
            var rooms = ids.Count == 0
                ? new()
                : await db.Rooms
                    .Where(r => ids.Contains(r.Id))
                    .Select(r => new { r.Id, r.Name, r.ImageName })
                    .ToDictionaryAsync(r => r.Id);
            featuredRoomGroup = new
            {
                Name = group.Name ?? string.Empty,
                FeaturedRooms = ids.Select(id => rooms.TryGetValue(id, out var r)
                    ? new { RoomName = r.Name, RoomId = r.Id, ImageName = r.ImageName ?? string.Empty }
                    : new { RoomName = string.Empty, RoomId = id, ImageName = string.Empty })
                    .ToArray(),
            };
        }

        return Ok(new
        {
            FeaturedPlayer = state.FeaturedPlayer,
            FeaturedRoomGroup = featuredRoomGroup,
            CurrentAnnouncement = state.CurrentAnnouncement,
            InstagramImages = instagram,
            Videos = state.Videos,
        });
    }
}
