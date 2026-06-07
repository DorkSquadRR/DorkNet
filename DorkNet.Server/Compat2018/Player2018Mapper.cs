using DorkNet.Models.Auth;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Compat2018;

/// <summary>
/// Maps a <see cref="PlayerEntity"/> to the June-2018 wire player object
/// (<see cref="Player2018"/>). Lives in the server (not DorkNet.Models) because
/// the DTO project can't reference the data-layer entity. Shared by the
/// platformlogin/v1, players/v1, and presence controllers so the 2018 player
/// shape is produced in exactly one place.
/// Keys verified against RecNet.EEBPLECPEGD.Deserialize (RecNet.cs:64632).
/// </summary>
public static class Player2018Mapper
{
    public static Player2018 From(PlayerEntity p) => new()
    {
        Id = p.Id,
        Username = p.Username,
        DisplayName = string.IsNullOrEmpty(p.DisplayName) ? p.Username : p.DisplayName,
        Bio = p.Bio ?? string.Empty,
        XP = p.XP,
        Level = p.Level,
        Developer = p.IsDeveloper,
        CanReceiveInvites = p.CanReceiveInvites,
        ProfileImageName = p.ProfileImageName ?? string.Empty,
        JuniorProfile = p.IsJunior,
        HasBirthday = p.Birthday.HasValue,
    };
}
