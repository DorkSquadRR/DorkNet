using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A user-creatable Rec Room "room" — has a name, a creator, a hub scene
/// (the AGRoomRuntimeConfig.Locations entry it spawns into), and metadata
/// for the watch UI's room browser.
///
/// Naming maps: in the wire JSON the client expects mostly PascalCase
/// (Room.Deserialize at RVA 0x114E430). Property names match.
///
/// Two-way relationship with PlayerEntity for ownership / bookmark / role
/// browsing — kept on the room side as ID columns rather than a navigation
/// to keep PlayerEntity unchanged.
/// </summary>
public class RoomEntity
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The player who created the room (NOT the dorm owner — every
    /// real Rec Room has a creator player id; for seeded "RecRoomOriginal"
    /// rooms we set this to 1 / the system account).</summary>
    public long CreatorPlayerId { get; set; }

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>RoomState enum (int): 0 = Active, 1 = Archived.</summary>
    public int State { get; set; } = 0;

    /// <summary>RoomAccessibility enum (int): 0 = Private, 1 = Public,
    /// 2 = LimitedAccessibility / FriendsOnly.</summary>
    public int Accessibility { get; set; } = 1;

    public bool SupportsLevelVoting { get; set; } = false;

    /// <summary>True for AG (user-built) rooms, false for "Rec Room
    /// Original" rooms. The watch tabs `#community` (AG) and
    /// `#recroomoriginal` (RR Originals) filter on this.</summary>
    public bool IsAGRoom { get; set; } = true;

    public bool IsDormRoom { get; set; } = false;
    public bool IsStudioRoom { get; set; } = false;
    public bool IsRoomLinkedToRecRoomStudio { get; set; } = false;

    [MaxLength(128)]
    public string StudioSessionId { get; set; } = string.Empty;

    public bool CloningAllowed { get; set; } = false;
    public bool SupportsVRLow { get; set; } = true;
    public bool SupportsMobile { get; set; } = false;
    public bool SupportsScreens { get; set; } = true;
    public bool SupportsWalkVR { get; set; } = true;
    public bool SupportsTeleportVR { get; set; } = true;
    public bool AllowsJuniors { get; set; } = true;
    public bool AllowNewUsers { get; set; } = true;
    public int MinLevel { get; set; } = 0;
    public int MaxPlayerCalculationMode { get; set; } = 0;

    /// <summary>Max players the room advertises per instance — flows into the
    /// matchmaking RoomInstance.MaxCapacity and the v4/details synthesized
    /// Scenes[0].MaxPlayers. Admin-adjustable. NOTE: the 2020.12 client
    /// doesn't hard-enforce this (it never sets Photon RoomOptions.MaxPlayers),
    /// so today it's the advertised/intended cap; true enforcement needs a
    /// ClientMod Photon patch (tracked as a follow-up).</summary>
    public int MaxCapacity { get; set; } = 8;

    public int RoomWarningMask { get; set; } = 0;

    [MaxLength(512)]
    public string CustomRoomWarning { get; set; } = string.Empty;

    public bool DisableMicAutoMute { get; set; } = false;

    /// <summary>RoomScene.RoomSceneLocationId GUID — foreign-keys to a
    /// Locations[] entry in resources.assets. This is what
    /// AGRoomSettings.TryGetRoomSceneLocationById matches.
    /// Default: DormRoom location GUID extracted from the asset.</summary>
    [MaxLength(64)]
    public string LocationReplicationId { get; set; } = "76d98498-60a1-430c-ab76-b54a29b7a163";

    /// <summary>Comma-separated tags. The 2020 watch supports a small set
    /// (#community, #recroomoriginal, #featured, etc.) — keeping it as a
    /// flat string avoids a join table for the v0 watch implementation.</summary>
    [MaxLength(1024)]
    public string TagsCsv { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string LoadScreensJson { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string PromoImagesJson { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string PromoExternalContentJson { get; set; } = string.Empty;

    public int CheerCount { get; set; } = 0;
    public int FavoriteCount { get; set; } = 0;

    /// <summary>Total joins across all players (every <c>/goto</c>
    /// counts; same player rejoining bumps it again). Mirrors
    /// official Rec.Net <c>Stats.VisitCount</c>.</summary>
    public int VisitCount { get; set; } = 0;

    /// <summary>Distinct players who've ever visited (each player
    /// counts once no matter how many times they rejoin). Mirrors
    /// official Rec.Net <c>Stats.VisitorCount</c>. Bumped only when
    /// <see cref="RoomVisitEntity"/> inserts a new row for the
    /// (room, player) pair.</summary>
    public int VisitorCount { get; set; } = 0;

    /// <summary>Score the watch uses to sort the "Hot" tab. Higher = more
    /// visible. Seeded rooms get bumped above empty user rooms so they show
    /// up first in the browser.</summary>
    public double HotScore { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True keeps the room out of room browse / search /
    /// "RR Originals" feeds while leaving <c>/goto</c>, clone, and
    /// admin-tool access intact. Used for rooms that exist in the seed
    /// (MakerRoom, EventRoom, paintball sub-maps merged into the
    /// Paintball lobby, etc.) but shouldn't show up to players because
    /// either there's a unified room hosting their scenes elsewhere or
    /// they're admin-only utility rooms.</summary>
    public bool HiddenFromBrowse { get; set; } = false;

    /// <summary>Name of the latest <see cref="RoomDataBlobEntity"/> row
    /// for this room — also what we put in
    /// <c>RoomDetails.Scenes[0].DataBlobName</c> so the client knows which
    /// blob to download. Empty for AG-Original baked rooms (rec center,
    /// paintball, etc.) which have no per-room save bytes; the dorm and
    /// user-built / cloned rooms point at their latest save.</summary>
    [MaxLength(128)]
    public string CurrentDataBlobName { get; set; } = string.Empty;
}
