using System.Security.Cryptography;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Backs the admin-issued signup-code flow. While the server has signups
/// disabled, the only path to a new account is redeeming one of these on
/// the public <c>/join</c> page. Codes are single-use, carry an admin
/// descriptor, and can have an optional expiry. Redemption mints a normal
/// username/password account.
/// </summary>
public class SignupCodeService(DorkNetDbContext db, PlayerService players)
{
    // Unambiguous alphabet — no 0/O/1/I/L so codes are easy to read aloud
    // and retype. Format: XXXX-XXXX.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public async Task<SignupCodeEntity> GenerateAsync(string descriptor, DateTime? expiresAt, long createdByPlayerId)
    {
        string code;
        do { code = NewCode(); }
        while (await db.SignupCodes.AnyAsync(c => c.Code == code));

        var entity = new SignupCodeEntity
        {
            Code = code,
            Descriptor = (descriptor ?? string.Empty).Trim(),
            CreatedByPlayerId = createdByPlayerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        db.SignupCodes.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<List<SignupCodeEntity>> ListAsync(int take = 200) =>
        await db.SignupCodes
            .OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync();

    /// <summary>Revoke an unused code. Already-redeemed codes can't be
    /// revoked (the account already exists); returns false in that
    /// case.</summary>
    public async Task<bool> RevokeAsync(long id)
    {
        var code = await db.SignupCodes.FirstOrDefaultAsync(c => c.Id == id);
        if (code is null || code.RedeemedByPlayerId is not null) return false;
        if (code.Revoked) return true;
        code.Revoked = true;
        await db.SaveChangesAsync();
        return true;
    }

    public sealed record RedeemResult(bool Ok, string? Error, long? PlayerId, string? Username);

    /// <summary>Redeem a code: validate it, then mint a password account
    /// with the requested username. The code is consumed atomically with
    /// account creation.</summary>
    public async Task<RedeemResult> RedeemAsync(string? rawCode, string? rawUsername, string? rawPassword)
    {
        var code = NormalizeCode(rawCode);
        if (string.IsNullOrEmpty(code)) return new(false, "missing_code", null, null);

        var username = (rawUsername ?? string.Empty).Trim();
        if (!IsValidUsername(username)) return new(false, "invalid_username", null, null);

        var password = rawPassword ?? string.Empty;
        if (string.IsNullOrEmpty(password)) return new(false, "missing_password", null, null);
        if (password.Length < 8) return new(false, "password_too_short", null, null);

        var entity = await db.SignupCodes.FirstOrDefaultAsync(c => c.Code == code);
        if (entity is null) return new(false, "invalid_code", null, null);
        if (entity.Revoked) return new(false, "code_revoked", null, null);
        if (entity.RedeemedByPlayerId is not null) return new(false, "code_used", null, null);
        if (entity.ExpiresAt is { } exp && exp <= DateTime.UtcNow) return new(false, "code_expired", null, null);

        if (await db.Players.AnyAsync(p => p.Username == username))
            return new(false, "username_taken", null, null);

        var player = await players.CreateNewAccountAsync(
            deviceId: $"join-{Guid.NewGuid():N}",
            platform: 0,
            platformId: null,
            displayName: username);
        player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

        entity.RedeemedByPlayerId = player.Id;
        entity.RedeemedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return new(true, null, player.Id, player.Username);
    }

    /// <summary>Record a device that was refused account creation while
    /// signups are disabled, so the /join page can surface it (matched by
    /// IP) instead of making the player hunt for their device id.
    /// Upserts by device id.</summary>
    public async Task RecordPendingDeviceAsync(string? deviceId, int platform, string? platformId, string? ip)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        var existing = await db.PendingDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            db.PendingDevices.Add(new PendingDeviceEntity
            {
                DeviceId = deviceId,
                Platform = platform,
                PlatformId = platformId ?? string.Empty,
                LastIp = ip,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
        }
        else
        {
            existing.Platform = platform;
            if (!string.IsNullOrEmpty(platformId)) existing.PlatformId = platformId;
            existing.LastIp = ip;
            existing.LastSeenAt = now;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Devices recently refused from the given IP, newest first —
    /// the /join picker shows these so the player recognises their own.</summary>
    public async Task<List<PendingDeviceEntity>> RecentPendingByIpAsync(string? ip, int take = 10)
    {
        if (string.IsNullOrWhiteSpace(ip)) return new();
        return await db.PendingDevices
            .Where(d => d.LastIp == ip)
            .OrderByDescending(d => d.LastSeenAt)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync();
    }

    /// <summary>Best-effort real client IP. The game client and the
    /// player's browser both sit behind the same Cloudflare edge / tunnel,
    /// so <c>RemoteIpAddress</c> collapses every caller to one address;
    /// prefer the CF-Connecting-IP / X-Forwarded-For header so the /join
    /// picker can match a player to the device their own client
    /// reported.</summary>
    public static string? ClientIp(HttpContext ctx)
    {
        var cf = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd)) return fwd.Split(',')[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    private static string NewCode()
    {
        Span<char> buf = stackalloc char[9];
        for (var i = 0; i < 9; i++)
            buf[i] = i == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(buf);
    }

    private static string NormalizeCode(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsValidUsername(string username) =>
        username.Length is >= 2 and <= 24
        && username.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-');
}
