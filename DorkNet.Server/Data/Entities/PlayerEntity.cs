using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

public class PlayerEntity
{
    public long Id { get; set; }

    // Stable per-installation identifier the client sends as `deviceId` in
    // platformlogin / account-create form bodies. We deliberately use this
    // (not `platformId`) so accounts are NOT keyed to Steam IDs — anyone
    // running the client gets a unique persistent account based on the
    // hardware-derived device hash, regardless of which Steam emulator
    // (Goldberg, none, real Steam) is in front. Indexed unique below.
    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    // Last-seen platform info — informational, NOT part of account identity.
    public int LastPlatform { get; set; } = 0;

    [MaxLength(128)]
    public string LastPlatformId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Bio { get; set; } = string.Empty;

    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;
    public int Reputation { get; set; } = 0;
    public bool IsVerified { get; set; } = false;

    // New accounts default to non-developer. Admins grant the badge
    // privilege via the SPA's Players → Profile flags tab on a
    // per-player basis (used to default to true while we were
    // bootstrapping the emulator; flipped off now that the build is
    // stable and we don't want every walk-in to spawn with the dev
    // console unlocked).
    public bool IsDeveloper { get; set; } = false;
    // Mirrors IsDeveloper for the overhead badge slider — the 2020 watch
    // gates the in-settings "Developer Display Mode" slider on a single
    // role check (`role/developer/{id}`), and the slider's positions
    // render as "Community Team" / "Developer" above the player's head
    // (verified in Cpp2IL_ISIL/.../PlayerUI.txt:9085-9099 — slider value
    // 1 → "Community Team", 2 → "Developer"). Keep this field separate
    // from IsDeveloper so admins can mark someone as community-team
    // without granting full dev privileges; the role endpoint ORs both.
    public bool IsCommunityTeam { get; set; } = false;
    public bool CanReceiveInvites { get; set; } = true;
    public bool IsJunior { get; set; } = false;

    [MaxLength(256)]
    public string? ProfileImageName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // ── Phase 1 account hardening ─────────────────────────────────────────
    // Optional contact email. Nullable because the 2020 client never asks
    // for one; reserved for future password-reset / admin-recovery flows.
    [MaxLength(256)]
    public string? Email { get; set; }

    // When non-null AND `> DateTime.UtcNow`, BanCheckMiddleware short-circuits
    // every authenticated request from this account with 401. Setting to a
    // past timestamp acts as a "previously banned" audit marker without
    // blocking access.
    public DateTime? BannedUntil { get; set; }

    // Privilege flag for `[AdminOnly]` endpoints (Phase 5). Defaults false;
    // PlayerService bumps this to true when the very first account on a
    // fresh DB is created so there's always at least one root admin.
    public bool IsAdmin { get; set; } = false;

    // Last IP that authenticated against this account. Updated by the
    // request-tracing pipeline so we don't write IPs from anonymous paths.
    [MaxLength(64)]
    public string? LastIp { get; set; }

    // The deviceId that originally created this account. Immutable after
    // creation — exists for the audit trail in case the live DeviceId
    // rotates (e.g., user re-installs and the hardware hash shifts; the
    // account stays bound to whatever they typed in /account/create the
    // first time).
    [MaxLength(128)]
    public string? CreatedFromDeviceId { get; set; }

    // BCP-47 locale tag (e.g. "en-US"). Optional; informs future
    // localisation work and is currently set from the client's accept-
    // language header at login.
    [MaxLength(16)]
    public string? Locale { get; set; }

    /// <summary>BCrypt hash of the account password. Null when the
    /// account has never set one — the in-game settings screen calls
    /// /account/me/haspassword and prompts the user to create one
    /// before sensitive operations. Format: BCrypt's standard
    /// `$2a$&lt;cost&gt;$&lt;salt&gt;&lt;hash&gt;` so the verifier
    /// reads cost + salt directly, no separate column needed.</summary>
    [MaxLength(128)]
    public string? PasswordHash { get; set; }

    /// <summary>Player's birthday (date only; no timezone). Set during
    /// signup and used to derive <see cref="IsJunior"/> on Junior cutoff
    /// boundary crossing. Nullable for legacy accounts created before
    /// the field existed.</summary>
    public DateTime? Birthday { get; set; }

    /// <summary>Optional phone number — used by the watch's
    /// account-recovery flow. Stored verbatim, never validated.</summary>
    [MaxLength(32)]
    public string? Phone { get; set; }

    // Navigation
    public AvatarEntity? Avatar { get; set; }
    public List<PlayerSettingEntity> Settings { get; set; } = [];
    public List<RelationshipEntity> Relationships { get; set; } = [];
}
