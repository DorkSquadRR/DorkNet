using DorkNet.Models.Auth;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Single projection from <see cref="PlayerEntity"/> to the account wire
/// DTOs. Every account read (account/me, account/{id}, account/bulk,
/// account/search, account/create) and the AccountUpdate /
/// SelfAccountUpdate pushes go through here so a profile field written by
/// one endpoint is visible on all of them — previously each call site built
/// its own DTO and the 2023 profile fields were never emitted anywhere.
/// </summary>
public static class AccountDtos
{
    public static RecNetAccount Public(PlayerEntity p) => Fill(new RecNetAccount(), p);

    public static RecNetSelfAccount Self(PlayerEntity p)
    {
        var a = Fill(new RecNetSelfAccount(), p);
        a.Email = p.Email ?? string.Empty;
        a.Phone = p.Phone ?? string.Empty;
        a.Birthday = p.Birthday ?? new DateTime(2000, 1, 1);
        a.JuniorState = p.IsJunior ? 1 : 0;
        a.ParentAccountId = null;
        return a;
    }

    private static T Fill<T>(T a, PlayerEntity p) where T : RecNetAccount
    {
        a.AccountId = (int)p.Id;
        a.RawUsername = p.Username;
        a.Username = p.Username;
        a.DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Username : p.DisplayName;
        a.ProfileImage = p.ProfileImageName ?? string.Empty;
        a.TreatAsJunior = p.IsJunior;
        a.HasBirthday = true;
        a.Platforms = 1;
        a.CreatedAt = p.CreatedAt;
        a.DisplayEmoji = p.DisplayEmoji ?? string.Empty;
        a.BannerImage = p.BannerImageName ?? string.Empty;
        a.PersonalPronouns = p.PersonalPronouns;
        a.IdentityFlags = p.IdentityFlags;
        return a;
    }
}
