# RecNet DTO inventory

Generated from the December 2020 readable C# dump under `Assembly-CSharp/RecNet`. Methods are frequently empty in this dump, but fields/properties are preserved and describe request/response JSON shapes used around RecNet calls.

## `AddVersionInventionRequestDTO.cs`

Declaration: `internal class AddVersionInventionRequestDTO`

- field `long inventionId`
- field `int instantiationCost`
- field `int lightsCost`
- field `int aiCost`
- field `long creationRoomId`
- field `string inventionDataFilename`
- field `List<Int64> referencedInventions`

## `BugReporting.cs`

Declaration: `private class BugReportDTO`

- field `string Summary`
- field `string Description`
- field `string TestCaseKey`
- field `string BuildVersion`
- field `long BuildTimestamp`
- field `Nullable<Int32> BundleVersionCode`
- field `static string BBBLGFHBMGA`

## `BulkInviteRequest.cs`

Declaration: `public class BulkInviteRequest`

- field `long PlayerEventId`
- field `List<Int32> InvitedPlayerIds`

## `ChatMessage.cs`

Declaration: `public class ChatMessage : IFAIJAGLDFK, IEquatable<ChatMessage>`

- no public fields/properties captured

## `CheerRequest.cs`

Declaration: `public class CheerRequest`

- field `long InventionId`
- field `bool Cheer`

## `DeleteMessagesRequestDTO.cs`

Declaration: `public class DeleteMessagesRequestDTO`

- field `List<Int64> MessageIds`

## `DeleteResponseRequest.cs`

Declaration: `public class DeleteResponseRequest`

- field `long PlayerEventId`

## `Elo.cs`

Declaration: `public class Elo : MonoBehaviour`

- field `int Team`
- field `long PlayerId`
- field `int GameScore`
- field `string ActivityLevel`
- field `List<PlayerScoreUpdate> PlayerScoreUpdates`
- enum `Activity`
- enum `ActivityLevel`

## `GetEventsForClubsRequest.cs`

Declaration: `public class GetEventsForClubsRequest`

- field `List<Int64> Id`

## `GetLeaderboardRequestDTO.cs`

Declaration: `public class GetLeaderboardRequestDTO`

- field `MIAJPBPGHOC ObjectiveType`
- field `bool SortAscending`
- field `int Limit`

## `GetNearbyScoresRequestDTO.cs`

Declaration: `public class GetNearbyScoresRequestDTO : GetRankRequestDTO`

- field `int WindowSize`

## `GetRankRequestDTO.cs`

Declaration: `public class GetRankRequestDTO`

- field `int PlayerId`
- field `int StatChannel`
- field `long RoomId`
- field `GHJPLMCGKIH FilterType`
- field `JBGJIODAFBA Timeframe`
- field `bool SortAscending`

## `GetRanksRequestDTO.cs`

Declaration: `public class GetRanksRequestDTO : GetRankRequestDTO`

- field `int RankStart`
- field `int RankEnd`

## `InventionBatchRequest.cs`

Declaration: `public class InventionBatchRequest`

- field `List<Int64> InventionIds`

## `KickPlayerDTO.cs`

Declaration: `public class KickPlayerDTO`

- field `long GameSessionId`
- field `List<Int32> PlayerIds`

## `Matchmaking.cs`

Declaration: `internal enum GIJOGKJCEKG`

- field `bool avoidJuniors`
- field `string requestUri`
- field `int accountId`
- field `int playerIdToNotify`
- enum `PublicMatchmaking`
- enum `PublicNewInstance`
- enum `PrivateNewInstance`
- field `IReadOnlyList<Int32> accountIds`
- enum `Success`
- enum `FailedToConnectToRegion`
- enum `FailedToConnectToRoom`
- enum `FailedToSpawnPlayer`
- field `long roomId`
- field `int id`
- field `FCIKKFJOMNO Platform`
- field `string PlatformId`
- field `string LockToken`
- field `float startTime`
- field `LDCMJEDOFCO service`
- field `string relativeUri`
- enum `UnknownError`
- enum `Success`
- enum `NoSuchGame`
- enum `PlayerNotOnline`
- enum `InsufficientSpace`
- enum `EventNotStarted`
- enum `EventAlreadyFinished`
- enum `BlockedFromRoom`
- enum `JuniorNotAllowed`
- enum `Banned`
- enum `AlreadyInBestInstance`
- enum `InsufficientRelationship`
- enum `UpdateRequired`
- enum `AlreadyInTargetInstance`
- enum `UGCNotAllowed`
- enum `NoSuchRoom`
- enum `RoomIsNotActive`
- enum `RoomBlockedByCreator`
- enum `RoomIsPrivate`
- enum `RoomInstanceIsPrivate`
- enum `DeviceClassNotSupported`
- enum `DeviceClassNotSupportedByRoomOwner`
- enum `MovementModeNotSupportedByRoomOwner`
- enum `EventIsPrivate`
- enum `RoomInviteExpired`
- enum `NoAvailableRegion`
- enum `NotorietyTooPoor`
- enum `BannedFromRoom`
- enum `NoSuchRoomPlaylist`
- enum `RoomPlaylistIsNotActive`
- enum `RoomPlaylistIsPrivate`
- enum `NoSuchClub`
- enum `ClubHasNoClubhouse`
- enum `ClubIsNotActive`
- enum `NotAMemberOfClub`
- enum `BannedFromClub`
- enum `InstanceJoinNotPermitted`
- field `Promise<EFAJBHGDIDD> promise`
- field `Action<Int32> onDelayedDetailsLoadedCallback`
- field `static ActionEvent<Int32> LEJANLMCKDD`
- field `static ActionEvent<CIIBGMBOFEI> BBDPBMMAPPO`
- field `static ActionEvent<Int64> AIFBHJBFKJL`
- field `static ActionEvent DOKMINNFCGC`
- field `static ActionEvent EBLJHBJGDFB`
- field `static ActionEvent PMBHKNGODOG`
- field `static ActionEvent LMJHPPCDGKC`
- field `static ActionEvent OLGAMNGEKON`
- field `static ActionEvent OLCJJNMEGPP`
- field `static ActionEvent<String> LHEICOLNLJL`

## `ModifyTagsRequest.cs`

Declaration: `public class ModifyTagsRequest`

- field `long InventionId`
- field `List<String> AutoTags`
- field `List<String> CustomTags`

## `NewInventionRequestDTO.cs`

Declaration: `internal class NewInventionRequestDTO`

- field `string name`
- field `string description`
- field `string imageName`
- field `int instantiationCost`
- field `int lightsCost`
- field `int aiCost`
- field `long creationRoomId`
- field `string inventionDataFilename`
- field `List<Int64> referencedInventions`
- field `LMLJHMJEIGM creatorAccountRole`

## `NewRoomKeyRequestDTO.cs`

Declaration: `internal class NewRoomKeyRequestDTO`

- field `long roomId`
- field `string name`
- field `string description`
- field `int price`

## `PlayerEventDTOPage.cs`

Declaration: `public class PlayerEventDTOPage : IFAIJAGLDFK`

- no public fields/properties captured

## `ReportRequest.cs`

Declaration: `public class ReportRequest`

- field `long InventionId`
- field `string Details`
- field `FDEGHHFBJJO ReportCategory`

## `RoomComment.cs`

Declaration: `public class RoomComment : IFAIJAGLDFK, IEquatable<RoomComment>`

- enum `Feedback`
- enum `Idea`
- enum `BugReport`

## `SetStatRequestDTO.cs`

Declaration: `public class SetStatRequestDTO`

- field `int StatChannel`
- field `long RoomId`
- field `int StatValue`

## `UnreadRoomComments.cs`

Declaration: `public class UnreadRoomComments : IFAIJAGLDFK`

- no public fields/properties captured

## `UpdatePriceRequest.cs`

Declaration: `public class UpdatePriceRequest`

- field `long InventionId`
- field `int Price`

## `UpdateRoomKeyRequestDTO.cs`

Declaration: `internal class UpdateRoomKeyRequestDTO`

- no public fields/properties captured

