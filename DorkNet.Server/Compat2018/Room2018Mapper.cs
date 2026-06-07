using DorkNet.Models.Rooms;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Compat2018;

/// <summary>
/// Maps a <see cref="RoomEntity"/> to the flat June-2018 wire room
/// (<see cref="Room2018"/>). Server-side because DorkNet.Models can't reference
/// the data entity. Some 2018 fields have no column (Instanced, MaxPlayers,
/// ReportCount, ActivityLevelId) and get sensible defaults; IsSandbox is derived
/// from IsAGRoom (official AG rooms are not sandbox/maker rooms).
/// </summary>
public static class Room2018Mapper
{
    public static Room2018 From(RoomEntity r) => new()
    {
        RoomId = r.Id,
        Name = r.Name,
        Description = r.Description,
        CreatorPlayerId = r.CreatorPlayerId,
        ImageName = r.ImageName,
        DataBlobName = r.CurrentDataBlobName ?? string.Empty,
        ActivityLevelId = string.Empty,
        IsSandbox = !r.IsAGRoom,
        Instanced = true,
        MaxPlayers = 20,
        Accessibility = r.Accessibility,
        AccessibilityLocked = false,
        VisitorCount = r.VisitorCount,
        VisitCount = r.VisitCount,
        CheerCount = r.CheerCount,
        ReportCount = 0,
        State = r.State,
        StateModifiedAt = r.UpdatedAt,
        CreatedAt = r.CreatedAt,
        ModifiedAt = r.UpdatedAt,
        LastVisitedAt = r.UpdatedAt,
        DataModifiedAt = r.UpdatedAt,
        CoOwners = [],
        Hosts = [],
        PersonalDetails = null,
    };
}
