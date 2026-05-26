# Rec Room 2020 Client RecNet API Sweep

This is a first-pass audit of the decompiled March and December 2020 client
material, focused on RecNet URLs, API route fragments, request wrappers, and
response DTO shapes.

## Sources

- December C# declarations:
  `dist/RecRoom-2020.12.18-dump/DiffableCs`
- December native/ISIL body dump:
  `dist/RecRoom-2020.12.18-isil/IsilDump`
- March archive:
  `C:/Users/you/Documents/Recnet-archive/decompiled`

The December readable C# dump preserves class names, fields, enum members, and
method signatures, but most method bodies are empty. For actual URL strings and
call context, the ISIL dump is the useful source. The March archive exposes a
large `Il2CppDump/stringliteral.json`; that is best for literal comparison, but
it does not always preserve call-site context.

Generated companion files:

- `docs/recroom-2020-client-endpoint-inventory.csv`: raw endpoint/string
  inventory with build, type, method, file, line, and literal string.
- `docs/recroom-2020-client-dto-inventory.md`: public field/property inventory
  for the December `Assembly-CSharp/RecNet` DTO namespace.

## Central RecNet Stack

The central December network wrapper is the obfuscated global class
`BPHGKAEDBPE`.

Important declarations found in
`dist/RecRoom-2020.12.18-dump/DiffableCs/Assembly-CSharp/BPHGKAEDBPE.cs`:

- `NameServerResponse` fields: `RecNetStatus`, `Auth`, `API`, `WWW`,
  `Notifications`, `Images`, `CDN`, `Commerce`, `Matchmaking`, `Storage`,
  `Chat`, `Leaderboard`, `Accounts`, `Link`, `RoomComments`, `Clubs`, `Rooms`.
- request queue item `IPJHBGCCNGH`: stores `HTTPMethods`, service enum,
  relative URI, retry flag, request configuration callback, and a promise for
  `HOOILIHKMEG`.
- public wrapper methods cover GET/POST-style calls with JSON strings, typed
  request objects, URL encoded forms, multipart forms, and arbitrary request
  configuration callbacks.

The service enum is `LDCMJEDOFCO`:

- `Auth`
- `API`
- `Commerce`
- `Matchmaking`
- `Notifications`
- `Images`
- `CDN`
- `Storage`
- `Chat`
- `Leaderboard`
- `Accounts`
- `Link`
- `RoomComments`
- `Clubs`
- `Rooms`

The March literal table includes the bootstrap URL
`https://ns.rec.net/?v=2`. The December ISIL confirms the same name-server
shape through `BPHGKAEDBPE.NameServerResponse`.

## Confirmed Route Families

These route families were found in the December ISIL dump. Many are assembled
from multiple adjacent string literals, for example a base like
`api/inventions/` plus a format string like
`{0}v1/details?inventionId={1}`.

### Account, Login, And Bootstrap

- `https://ns.rec.net/?v=2` in March literals.
- `https://rec.net/password/recover` in March literals.
- `.rec.net` host suffix in March literals.
- `account/bulk`, `account/bulk?`, and `account/bulk/` in December ISIL.
- RecNet connectivity test string:
  `http://www.google.com/generate_204`.

### Player State

- `api/players/v2/objectives`
- `/api/players/v1/progression/{0}`
- `/api/players/v1/progression/bulk`
- `/api/playerReputation/v1/{0}`
- `/api/playerReputation/v1/bulk`
- `api/avatar/v1/lockeditems?`

The response/request DTO coverage here is outside the small `RecNet` folder in
many cases, so the raw CSV should be used with `rg` follow-up on the owning
obfuscated class names.

### Rooms

- `rooms/{0}`
- `rooms/bulk`
- `rooms/base`
- `rooms/createdby/me`
- `rooms/ownedby/me`
- `rooms/moderatedby/me`
- `rooms/cheeredby/me`
- `rooms/favoritedby/me`
- `rooms/visitedby/me`
- `rooms/createdby/{0}`
- `rooms/rro_ids`
- `rooms/{0}/subrooms/{1}/datahistory`
- `rooms/{0}/roles`
- `rooms/{0}/roles/{1}`
- `rooms/recommendations`
- `rooms/search`
- `rooms/hot`
- `featuredrooms/current`
- `rooms/{0}/clone`
- `rooms/{0}/name`
- `rooms/{0}/description`
- `rooms/{0}/image`
- `rooms/{0}/tags`
- `api/rooms/v1/roomRolePermissions`
- `api/rooms/v4/saveData`
- `api/rooms/v2/report`
- `api/rooms/v1/verifyRole`
- `api/rooms/v1/filters`
- `rooms/{0}/subrooms/{1}/data`

The DorkNet server already implements many of these surfaces under
`Controllers/API/Rooms/V2`, `Controllers/Rooms`, `Controllers/Storage`, and
`Controllers/Cdn`.

### Matchmaking

December `RecNet/Matchmaking` ISIL contains:

- `goto/room/`
- `room/{0}/instances`

The client expects matchmaking and goto responses to contain room instance
identity, room/sub-room ids, Photon room ids, privacy/full/in-progress state,
and location-like fields. This matches the DorkNet notes about
`GoToController`, `MatchController`, `GameSessionService`, and
`PrivateInstanceService`.

### Inventions

The invention API is one of the clearest route clusters.

Base:

- `api/inventions/`

Fragments:

- `{0}v1?inventionId={1}`
- `{0}v1/update?inventionId={1}&name={2}`
- `{0}v1/update?inventionId={1}&description={2}`
- `{0}v1/update?inventionId={1}&imgName={2}`
- `{0}v1/update?inventionId={1}&permission={2}`
- `{0}v1/details?inventionId={1}`
- `{0}v1/versions?inventionId={1}`
- `{0}v1/delete?inventionId={1}`
- `{0}v3/publish?inventionId={1}&permissionLevel={2}`
- `{0}v1/unpublish?inventionId={1}`
- `{0}v1/download?inventionId={1}`
- `{0}v1/version?inventionId={1}&version={2}`
- `api/inventions/v4/save` in December
- `api/inventions/v3/addversion` in December
- `api/inventions/v1/fulllineageowner?`
- `/invention/{0}` link route

March literals show similar routes, but older save/publish strings include:

- `api/inventions/v3/save`
- `api/inventions/v3/addversion`
- `{0}v2/publish?inventionId={1}&permissionLevel={2}`

That suggests December moved at least the save path from `v3` to `v4` and the
publish path from `v2` to `v3`.

Relevant December DTOs:

- `NewInventionRequestDTO`
- `AddVersionInventionRequestDTO`
- `NewRoomKeyRequestDTO`
- `UpdateRoomKeyRequestDTO`
- `InventionBatchRequest`
- `CheerRequest`
- `UpdatePriceRequest`
- `ReportRequest`

See `docs/recroom-2020-client-dto-inventory.md` for the field list.

### Room Keys

- `api/roomkeys/v1/create`
- `api/roomkeys/v1/update`
- `api/roomkeys/v1/mine`

Relevant DTOs:

- `NewRoomKeyRequestDTO`
- `UpdateRoomKeyRequestDTO`

### Player Events

- `api/playerevents/v1/deleteResponse`
- `api/playerevents/v1/bulkInvite`
- `api/playerevents/v1/all`
- `api/playerevents/v1/report`
- `api/playerevents/v1/searchlive?`
- `api/playerevents/v1/search?`
- `api/playerevents/v1/tagfilters`

Relevant DTOs:

- `GetEventsForClubsRequest`
- `BulkInviteRequest`
- `DeleteResponseRequest`
- `PlayerEventDTOPage`

### Messages And Notifications

- `api/messages/v3/delete`
- `api/messages/v1/IOSSaveDeviceToken`
- `api/messages/v1/IOSClearDeviceToken`
- `api/messages/v1/IOSResetNotificationPreferencesBadgeCount`
- `api/messages/v1/IOSModifyNotificationPreferences`

Relevant DTO:

- `DeleteMessagesRequestDTO`

The `NameServerResponse` includes `Notifications` and `Chat` service bases, so
SignalR and chat routes are likely partly constructed outside the RecNet DTO
namespace.

### Clubs

- `members/bulk`
- `members/bulk?`
- `api/clubreporting/v1/report`

The `NameServerResponse` includes `Clubs`, and the DorkNet server has club/event
adjacent models. A second pass should focus on `JDJGIBLMFKK` and related
club/member UI/data classes.

### Store, Economy, Rewards

- `api/gamerewards/v1/pending`
- `api/gamerewards/v1/select`
- `/consume`

The purchase error strings include token support email text, and invention
purchase flows are wired through the invention/store UI.

### Config And Announcements

- `api/config/`
- `api/config/v1/freegiftbutton`
- `/config/{0}`
- `api/announcement/v1/get`
- `api/activities/charades/v1/words`

### User Reporting

- `https://userreporting.cloud.unity3d.com`
- `https://userreporting.cloud.unity3d.com/api/userreporting/projects/{0}/ping`
- `{0}/api/userreporting`
- `api/PlayerReporting/v1/voteToKickReasons`

This is partly Unity user reporting, partly RecNet player reporting.

## Request And Response Shape Notes

The December `RecNet` C# namespace gives the best directly-readable request DTOs.
Examples:

- `AddVersionInventionRequestDTO`: `inventionId`, costs, `creationRoomId`,
  `inventionDataFilename`, `referencedInventions`.
- `NewInventionRequestDTO`: name/description, costs, room id, data/image names,
  and referenced inventions.
- `UpdatePriceRequest`: price-style request object for paid inventions.
- `GetRankRequestDTO`, `GetRanksRequestDTO`, `GetNearbyScoresRequestDTO`, and
  `GetLeaderboardRequestDTO`: leaderboard/ranking inputs.
- `BulkInviteRequest`, `DeleteResponseRequest`, `GetEventsForClubsRequest`:
  player-event request inputs.
- `DeleteMessagesRequestDTO`: message deletion body.
- `ReportRequest`: generic report payload.

`Matchmaking.cs` and room model classes contain response model declarations, but
the most important server-facing shape is already known from DorkNet behavior:
room instances must include room instance id, room id, sub-room id, location,
Photon region and room id, capacity/full/private/in-progress flags, and enough
room data URL information to make the client download binary room blobs.

## March Vs December

Observed March literals include:

- `https://ns.rec.net/?v=2`
- `https://rec.net`
- `https://rec.net/password/recover`
- `.rec.net`
- `api/inventions/v3/save`
- `api/inventions/v3/addversion`
- `{0}v2/publish?inventionId={1}&permissionLevel={2}`
- `api/rooms/v4/saveData`
- the same player progression and reputation routes as December.

Observed December ISIL includes:

- `api/inventions/v4/save`
- `api/inventions/v3/addversion`
- `{0}v3/publish?inventionId={1}&permissionLevel={2}`
- the larger rooms route cluster listed above.

The safest server strategy is to support both March and December variants for
invention save/publish, and to keep room save/data endpoints tolerant of both
legacy room API and `api/rooms/v4/saveData`.

## Important Caveats

This is not yet a literal function-by-function human annotation of every call
site. The reason is practical: the December C# decompile has declarations but
empty bodies, while the ISIL dump has bodies but obfuscated names and low-level
instructions. The generated CSV gives the call-site line numbers needed for that
next pass.

For any route that matters to gameplay, verify all three things before changing
DorkNet behavior:

- the service base selected by `LDCMJEDOFCO`;
- the route string assembled around the literal fragments;
- the DTO or response class used by the promise continuation.

Best next search commands:

```powershell
rg -n "api/inventions|rooms/|goto/room|playerevents|roomkeys|messages" dist/RecRoom-2020.12.18-isil/IsilDump/Assembly-CSharp
rg -n "class .*Request|class .*Response|public .*;" dist/RecRoom-2020.12.18-dump/DiffableCs/Assembly-CSharp/RecNet
rg -n "https://ns.rec.net|api/inventions|api/rooms|playerevents|roomkeys" C:/Users/you/Documents/Recnet-archive/decompiled/Il2CppDump/stringliteral.json
```
