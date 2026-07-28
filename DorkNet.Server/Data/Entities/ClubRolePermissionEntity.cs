using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// What one club role is allowed to do, as set by
/// <c>PUT club/{clubId}/permissions/{membershipType}</c>.
///
/// The 2023-03-21 client sends six booleans for a given role
/// (RecNet.Runtime/IKMMOCKDKAF_NestedType_BOIMHOCCOEI.txt:173, :189, :205,
/// :221, :237, :253) and reads them back on the club details envelope as the
/// <c>CoownerPermissions</c> / <c>ModeratorPermissions</c> /
/// <c>MemberPermissions</c> objects.
///
/// One row per (club, role). A club with no row for a role falls back to the
/// derived default policy in <c>ClubsController.PermissionsForRole</c>, so
/// existing clubs keep behaving exactly as they did before this table existed.
///
/// <see cref="MembershipType"/> is the WIRE enum value (Member=10,
/// Moderator=20, CoOwner=30), not the stored permission bitmask — the client
/// addresses roles by the wire value and so does the route.
/// </summary>
public class ClubRolePermissionEntity
{
    public long Id { get; set; }

    public long ClubId { get; set; }

    /// <summary>Wire membership-type enum: 10=Member, 20=Moderator, 30=CoOwner.</summary>
    public int MembershipType { get; set; }

    public bool EditDetails { get; set; }
    public bool ApproveMember { get; set; }
    public bool CreateEvent { get; set; }
    public bool PostAnnouncement { get; set; }
    public bool EditPermissionSettings { get; set; }
    public bool BanUnban { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One image in a club's gallery, addressed by slot.
///
/// The client sets a slot with <c>PUT club/{clubId}/additionalimage/{slot}</c>
/// and clears it with the DELETE twin, then reads the set back as the details
/// envelope's <c>AdditionalImages</c> rows
/// (<c>{ImageName, Slot}</c> — recnet-runtime-decomp/HIKCHBLAMLP.cs:9, :27).
/// Without this table the envelope could only ever send an empty array, so the
/// gallery rendered blank no matter what the creator uploaded.
/// </summary>
public class ClubAdditionalImageEntity
{
    public long Id { get; set; }

    public long ClubId { get; set; }

    /// <summary>Gallery position, taken from the route.</summary>
    public int Slot { get; set; }

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
