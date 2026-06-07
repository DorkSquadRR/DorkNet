using System.Text.Json.Serialization;

namespace DorkNet.Models.Rooms;

/// <summary>
/// Flat June-2018 room object. The 2018 client (RecNet Room.Deserialize,
/// RecNet.cs:78244) reads these keys directly off the room JSON — there is NO
/// 2020-style RoomDetails/Scenes wrapper; DataBlobName + PersonalDetails are
/// embedded right here. Required keys throw (Util.GetKey) if missing, so every
/// field below must be present except the genuinely-optional ones noted.
///
/// Notes:
///  • CreatorPlayerId is read as an int client-side; all account ids fit int.
///  • Date keys are ISO-8601 (client DateTimeParse.ParseISO8601). Use real
///    dates, never year-9999 (overflow — see WireDates).
///  • ActivityLevelId parse failure is a non-fatal warning client-side, so ""
///    is safe for non-activity rooms.
///  • CoOwners/Hosts are int arrays (empty ok); PersonalDetails is an optional
///    object (null ok).
/// </summary>
public class Room2018
{
    [JsonPropertyName("RoomId")] public long RoomId { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("CreatorPlayerId")] public long CreatorPlayerId { get; set; }
    [JsonPropertyName("ImageName")] public string ImageName { get; set; } = string.Empty;
    [JsonPropertyName("DataBlobName")] public string DataBlobName { get; set; } = string.Empty;
    [JsonPropertyName("ActivityLevelId")] public string ActivityLevelId { get; set; } = string.Empty;
    [JsonPropertyName("IsSandbox")] public bool IsSandbox { get; set; }
    [JsonPropertyName("Instanced")] public bool Instanced { get; set; } = true;
    [JsonPropertyName("MaxPlayers")] public int MaxPlayers { get; set; } = 20;
    [JsonPropertyName("Accessibility")] public int Accessibility { get; set; } = 1;
    [JsonPropertyName("AccessibilityLocked")] public bool AccessibilityLocked { get; set; }
    [JsonPropertyName("VisitorCount")] public int VisitorCount { get; set; }
    [JsonPropertyName("VisitCount")] public int VisitCount { get; set; }
    [JsonPropertyName("CheerCount")] public int CheerCount { get; set; }
    [JsonPropertyName("ReportCount")] public int ReportCount { get; set; }
    [JsonPropertyName("State")] public int State { get; set; }
    [JsonPropertyName("StateModifiedAt")] public DateTime StateModifiedAt { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("ModifiedAt")] public DateTime ModifiedAt { get; set; }
    [JsonPropertyName("LastVisitedAt")] public DateTime LastVisitedAt { get; set; }
    [JsonPropertyName("DataModifiedAt")] public DateTime DataModifiedAt { get; set; }
    [JsonPropertyName("CoOwners")] public List<int> CoOwners { get; set; } = [];
    [JsonPropertyName("Hosts")] public List<int> Hosts { get; set; } = [];
    [JsonPropertyName("PersonalDetails")] public object? PersonalDetails { get; set; }
}
