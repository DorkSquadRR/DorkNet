using System.Text.Json.Serialization;

namespace DorkNet.Models.GameSessions;

public class GameSession
{
    [JsonPropertyName("GameSessionId")]
    public long GameSessionId { get; set; }

    [JsonPropertyName("RegionId")]
    public string RegionId { get; set; } = "us";

    [JsonPropertyName("RoomId")]
    public string RoomId { get; set; } = string.Empty;

    [JsonPropertyName("RoomName")]
    public string RoomName { get; set; } = string.Empty;

    [JsonPropertyName("ActivityLevelId")]
    public string ActivityLevelId { get; set; } = string.Empty;

    [JsonPropertyName("EventId")]
    public long? EventId { get; set; }

    [JsonPropertyName("Private")]
    public bool Private { get; set; }

    [JsonPropertyName("GameInProgress")]
    public bool GameInProgress { get; set; }

    [JsonPropertyName("MaxCapacity")]
    public int MaxCapacity { get; set; } = 8;

    [JsonPropertyName("IsFull")]
    public bool IsFull { get; set; }

    [JsonPropertyName("PlayerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("PhotonRoomName")]
    public string PhotonRoomName { get; set; } = string.Empty;
}

public class JoinRandomGameSessionRequest
{
    [JsonPropertyName("RoomId")]
    public string? RoomId { get; set; }

    [JsonPropertyName("ActivityLevelId")]
    public string? ActivityLevelId { get; set; }

    [JsonPropertyName("RegionId")]
    public string RegionId { get; set; } = "us";
}

public class JoinGameSessionResponse
{
    [JsonPropertyName("Result")]
    public JoinGameErrorCode Result { get; set; } = JoinGameErrorCode.Success;

    [JsonPropertyName("GameSession")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GameSession? GameSession { get; set; }
}

public enum JoinGameErrorCode
{
    Success = 0,
    Full = 1,
    NotFound = 2,
    Banned = 3,
    Error = 4,
}
