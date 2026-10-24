# 2023 client notifications (realtime push)

The contract between DorkNet and the 2023-03-21 client's notification hub.
Sources: the client's RecNet.Runtime ISIL (`C:\tmp\recroom-2023-03-21-isil`,
obfuscated names given as found there), cross-checked against RecFlare
(build 20230414) where useful. Code: `DorkNet.Server/Hubs/NotifyHub.cs`,
`Services/NotificationService.cs`, `Services/PresenceBroadcastService.cs`,
`Services/SystemNotificationService.cs`. Tests:
`DorkNet.Server.Tests/NotificationContractTests.cs`.

## Connection

- The hub is `{notify host}/hub/v1` (nameserver key `Notifications`). The
  client opens it once, at login.
- **Negotiate**: `POST …/hub/v1/negotiate`, no `negotiateVersion`. The client reads
  `connectionId` and `availableTransports`, and accepts only `WebSockets`.
- **WebSocket**: `…/hub/v1?id=<connectionId>`, JSON protocol
  (`{"protocol":"json","version":1}`), no MessagePack.
- **Auth**: `Authorization: Bearer` on both negotiate and the upgrade. No
  `access_token` query string.
- **Keep-alive**: the client pings every 15 s from its main thread and has
  no server timeout of its own. The server pings every 10 s and allows 180 s
  of client silence, because a long room-load hitch pauses the client's
  pings.
- **Reconnects: at most 3 per login.** The client's attempt counter is never
  reset after a successful reconnect. Once the budget is spent the hub stays
  dead, with no message, until the next login. The server must never close a
  healthy connection.

## Envelope

Every push invokes the hub method **`Notification`** with **one string
argument**, whose content is JSON:

```json
{"Id":"<key>","Msg":{...}}
```

- **`Id`** is a JSON string. It is looked up case-sensitively in the
  client's handler table, and a miss drops the push silently.
  - Handlers registered through the enum overload use the decimal string of
    the id (`"2"`).
  - The rest use a name (see the table below).
  - `NotificationService.WireName` maps `PushNotificationId` to the key.
- **`Msg`** is an inline JSON object of the handler's DTO, never
  JSON-in-a-string.
  - It is parsed strictly. Keys may be PascalCase, camelCase or all
  lowercase, and unknown keys are skipped.
  - A value of the wrong type (a number where a string is expected, a
    string where an enum number is expected) throws, and the whole push is
    dropped.
  - A key that is missing takes its default value, which usually means the
    push "works" but does nothing.

## Hub calls (client → server)

| Method | Payload | Server |
|---|---|---|
| `SubscribeToPlayers` | `{PlayerIds: List<int>}`: the client's relationships, the players around it, and itself. Sent on every (re)connect and whenever the set changes, always the complete set. | Replaces the connection's subscriptions. The connection is put in a `presence:{id}` group per player (own id skipped), and `PresenceBroadcastService` pushes those players' presence to those groups. Only presence, never the subject's other pushes. An unreadable payload keeps the current set. |
| `SubscribeTo` / `UnsubscribeFrom` | `{Channels: List<string>}`, always empty in this build | logged only |

There is no `UnsubscribeFromPlayers`.

## Handler table

These are the handlers the client registers. "Sent" names what DorkNet sends
today.

| Key | Msg | Sent by |
|---|---|---|
| `1` RelationshipChanged | `{PlayerID, RelationshipType, Muted, Ignored, Favorited}`: the recipient's own view, the same row `api/relationships/v2/get` lists | friend request / accept / remove / add / add-with-code (both players); favorite / mute / ignore and their reverses (caller only) |
| `2` MessageReceived | `{Id, FromPlayerId (Int32), SentTime, Type, Data (string), RoomId, PlayerEventId}`, the same row `api/messages/v2/get` lists | DMs and game invites; `SystemNotificationService` (cheers, friend request Type 4, accepted Type 40, "wants to join" Type 10, coach message Type 100, event invite text) |
| `3` MessageDeleted | `{Id}` | not sent |
| `5` RefreshLogin | none | not sent |
| `6` Logout | `{Reason}`: a **number**, enum Unknown 0, UserInitiated 1, SessionTakeover 2, ForciblyLoggedOut 3, Banned 4 | `NotificationService.LogoutAsync`: second-session takeover (2), account deleted or password reset (3), bans (4) |
| `20` / `ModerationQuitGame` | none | not sent |
| `22` ModerationKick | **flat** `{ReportCategory, Duration, GameSessionId, IsHostKick, Message, PlayerIdReporter, IsBan, IsVoiceModAutoban, IsWarning, VoteKickReason, TimeoutStartedAt}` | `KickPlayerAsync`: admin kick, close instance, in-game instant kick, join timeout |
| `ModerationUnkick` | same as 22 | not sent |
| `23` ModerationKickAttemptFailed | `{ReportCategory, YesVotes, NoVotes, PlayerIdReported}` | not sent |
| `25` ServerMaintenance | `{StartsInMinutes}`; 0 cancels the countdown | admin Broadcast, when a countdown is given |
| `30` / `31` GiftPackageReceived(Immediate) | the full gift package, as `api/avatar/v2/gifts` lists it (`AvatarGiftsController.ToWire`) | gifts, store gift purchases, free gifts, admin gifts |
| `50` RelationshipsInvalid | none (the client refetches) | admin "everyone is friends" toggle |
| `60` / `61` / `62` StorefrontBalance Add / Update / Purchase | 61 `{Balance, CurrencyType, Platform}`; 62 `{BalanceAddType, Delta, Balance, Platform, CurrencyType}` | `LevelService.GrantCurrencyAsync` (61), store purchase (62) |
| `70` / `71` ConsumableMapping Added / Removed | `{Id, ConsumableItemDesc (string), Count, InitialCount, CreatedAt, ActiveDurationMinutes, IsActive, IsTransferable}` | transfer (70 to the recipient), consume (always 71, with the remaining Count) |
| `80` / `81` PlayerEvent Created / Updated | the full player event, as `api/playerevents/v2/{id}` returns it | invites (80 to each invitee); every edit route and the broadcast setting (81 to attendees) |
| `82` / `84` PlayerEvent Deleted / ResponseDeleted | `{PlayerEventId}` | event delete (82 to attendees), RSVP removed (84 to the creator) |
| `83` PlayerEventResponseChanged | `{PlayerEvent, PlayerEventResponse}` | RSVP (to the creator) |
| `100` InventionModerationStateChanged | none | not sent |
| `AccountUpdate` / `SelfAccountUpdate` | the public / self account DTOs the account REST routes return | every profile edit, including profile image (`NotificationService.AccountChanged`) |
| `PresenceUpdate` | `{playerId, statusVisibility, deviceClass, vrMovementMode, roomInstance, isOnline, appVersion (string)}` | `PresenceBroadcastService`, to friends and subscribers, **never to the subject** (a frame about the local player moves them) |
| `RoomInstanceUpdate` | the room instance (`RoomInstanceDto`) | admin force-join / instance pull. It only refreshes the cached instance; the move itself comes from the next heartbeat. |
| `RoomUpdate` | the **full room** (`FGCPNAACHIK`, `RoomsController.BuildRoomServerDetails`), not the 2020 `{Room, Scenes}` wrapper | admin role / owner changes (everyone in the room plus the changed player), admin force-join |
| `ChatMessageReceived` | `{chatMessageId, chatThreadId, senderPlayerId, timeSent, contents}` | chat sends, to every member including the sender |
| `PlayerLeftChat` | the same chat-message shape; only `chatThreadId` and `senderPlayerId` (the leaver) are read | leaving a group chat |
| `PlayerProgressionLevelUpdate` | `{PlayerId, Level, XP}`, as `api/players/v2/progression` returns it | every XP award (`LevelService.AwardXpAsync`) |
| `CommunityBoardUpdate` | community board | community board edits |
| `ReputationUpdate`, `RoomCurrency*`, `KeepsakeInstance*`, `PhotonAccessToken`, `GoToFailure`, `AnnouncementUpdate/Delete`, `ClubMembershipUpdate`, `GameConfig.Refresh`, `PlayerSettings.Refresh`, … | see the contract notes in `recroom-2023-client-api-complete.md` | not sent |

**Not registered at all** (a push with these keys is dropped): numeric 4, 11,
12, 13, 15, 21, 24 (ModerationRoomBan), 40, 85, 90, 95.

## Kicks versus logouts

A ModerationKick only sends the player to their dorm, still signed in.

The kick handler builds the dialog text like this:
- It always starts from its own "Kicked to Dormroom".
- It appends "Reason: …" from `VoteKickReason` only when `ReportCategory`
  is 10 and `IsHostKick` is false. `Message` is never shown.
- When `GameSessionId` is the kicked player's current room instance, it also
  announces the kick to that room.
- A `Duration` above 0 starts a timeout follow-up.

Anything that must sign the player out uses Logout instead: bans, account
deletion, password resets and second-session takeover. The ban check then
refuses the banned player's next login.

## Admin broadcast

`POST api/admin/v1/broadcast` `{Message?, StartsInMinutes?}`:
- `Message` becomes a coach message (MessageReceived Type 100 from the
  system account) in every online player's notifications.
- `StartsInMinutes` starts the client's own maintenance countdown (25).

ServerMaintenance has no text field. The old `{Message}` payload was never
shown, and it cancelled any running countdown.

## Not yet done

- **No offline queue.** A push sent while the player has no connection is lost.
  Messages are stored, so they still appear in `api/messages/v2/get`.
  RecFlare keeps the newest 500 per player and flushes them on connect.
- The 2020 save and restore routes (`api/rooms/v4/saveData`,
  `…/restoredata`, `api/rooms/v1/datahistory/restore`) still push the 2020
  `RoomUpdate` wrapper and a self PresenceUpdate. The 2023 client never
  calls them; its save (`subrooms/{n}/data`) and publish routes push
  nothing and return the room in the HTTP reply.
- `sendfriendintroductions` stores Type 30 text instead of FriendIntroduction
  (130), and pushes nothing. The format of the 130 `Data` field is unknown.
