namespace DorkNet.Server.Data.Entities;

/// <summary>
/// An identity from the external sign-in provider (OpenID Connect) linked to a
/// DorkNet account. One row per (Issuer, Subject): the provider's stable user
/// id, never the email, which can change or be reused. A player proves they own
/// the account once, with its password, when the link is made
/// (<c>ExternalSignInController.Link</c>); after that the provider alone signs
/// them in to the site and, for admins, the admin panel.
/// </summary>
public class ExternalLoginEntity
{
    public long Id { get; set; }

    public long PlayerId { get; set; }

    /// <summary>The provider's issuer URL (the <c>iss</c> claim).</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The provider's user id (the <c>sub</c> claim).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The email the provider reported when the link was made or last
    /// used. Shown to the player; never used to find the account.</summary>
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
