using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// OAuth device-authorization grant as the 2023 client drives it.
///
///   1. The device that wants to sign in calls <c>connect/deviceauthorization</c>
///      and shows the player the <c>user_code</c> (reader
///      <c>RecNet.Runtime/AMPFGJPKHOD.txt:399-598</c>: device_code, user_code,
///      verification_uri(_complete), expires_in, interval).
///   2. It polls <c>connect/token</c> with
///      <c>grant_type=urn:ietf:params:oauth:grant-type:device_code</c> and
///      <c>device_code</c> (<c>LBNJFPOLCDL_NestedType_DDNJDALGIME.txt:444-451</c>).
///      A 400 whose <c>error</c> is <c>authorization_pending</c> or
///      <c>slow_down</c> means "keep polling"
///      (<c>LBNJFPOLCDL_NestedType_JAEKFNKDNCP.txt:97-116</c>); anything else
///      ends the attempt.
///   3. A signed-in client approves the code with
///      <c>account/me/remoteauth</c> (form field <c>code</c>).
///
/// State lives in one PlayerSettings row per authorization, owned by the system
/// account: key <c>deviceauthorization:{device_code}</c>, value
/// <c>{user_code}|{expiresAtUtc:O}</c>, plus <c>|{approverAccountId}</c> once
/// approved. Redeeming deletes the row, so a device code is single-use.
/// </summary>
public static class DeviceAuthorizations
{
    public const string KeyPrefix = "deviceauthorization:";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public enum RedeemStatus { Invalid, Expired, Pending, Approved }

    public sealed record Created(string DeviceCode, string UserCode, int ExpiresInSeconds);

    public static async Task<Created> CreateAsync(DorkNetDbContext db, DateTime nowUtc)
    {
        var deviceCode = Guid.NewGuid().ToString("N");
        var userCode = deviceCode[..8].ToUpperInvariant();
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = RelationshipQueries.SystemAccountId,
            Key = KeyPrefix + deviceCode,
            Value = $"{userCode}|{(nowUtc + Lifetime):O}",
        });
        await db.SaveChangesAsync();
        return new Created(deviceCode, userCode, (int)Lifetime.TotalSeconds);
    }

    /// <summary>Bind the pending authorization whose user code is
    /// <paramref name="userCode"/> to <paramref name="approverId"/>. False when
    /// no live, unapproved authorization carries that code.</summary>
    public static async Task<bool> ApproveAsync(
        DorkNetDbContext db, string userCode, long approverId, DateTime nowUtc)
    {
        var code = NormaliseUserCode(userCode);
        if (code.Length == 0) return false;

        var prefix = code + "|";
        var rows = await db.PlayerSettings
            .Where(s => s.Key.StartsWith(KeyPrefix) && s.Value.StartsWith(prefix))
            .ToListAsync();
        foreach (var row in rows)
        {
            var parts = row.Value.Split('|');
            if (parts.Length < 2 || !TryParseExpiry(parts[1], out var expires) || expires < nowUtc)
                continue;
            if (parts.Length >= 3 && parts[2].Length > 0) continue; // already approved
            row.Value = $"{parts[0]}|{parts[1]}|{approverId}";
            await db.SaveChangesAsync();
            return true;
        }
        return false;
    }

    /// <summary>One poll from the device. Approved authorizations are consumed
    /// and yield the approving account.</summary>
    public static async Task<(RedeemStatus Status, long AccountId)> RedeemAsync(
        DorkNetDbContext db, string? deviceCode, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(deviceCode)) return (RedeemStatus.Invalid, 0);
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.Key == KeyPrefix + deviceCode.Trim());
        if (row is null) return (RedeemStatus.Invalid, 0);

        var parts = row.Value.Split('|');
        if (parts.Length < 2 || !TryParseExpiry(parts[1], out var expires) || expires < nowUtc)
        {
            db.PlayerSettings.Remove(row);
            await db.SaveChangesAsync();
            return (RedeemStatus.Expired, 0);
        }

        if (parts.Length < 3 || !long.TryParse(parts[2], out var approver))
            return (RedeemStatus.Pending, 0);

        db.PlayerSettings.Remove(row);
        await db.SaveChangesAsync();
        return (RedeemStatus.Approved, approver);
    }

    private static string NormaliseUserCode(string? raw) =>
        new string((raw ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static bool TryParseExpiry(string raw, out DateTime expiresUtc) =>
        DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out expiresUtc);
}
