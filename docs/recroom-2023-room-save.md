# 2023 client room-save flow

Wire contract for the 2023.03.21 client's room save, reverse-engineered
from `OGPDOMCNIFM.UploadRoomDataBlobAndSyncReload`
(RecRoom.RoomLoading.Runtime) and verified against dev server traffic
on 2026-07-07. Diagnosed while fixing "Failed to save room" /
"An error occurred" save failures.

## Sequence

1. **Upload room metadata** — `POST https://storage.<apex>/upload`,
   multipart (`File`=file.bin, `FileType=6`, optional `References`).
   FileType **6 = RoomMetadata** is new in 2023 (2020 enum stopped at
   5=Invention). Small payload (~4 bytes observed for a dorm). Response:
   `{"filename": "roommeta_p<player>_<guid>.bin"}`.
2. **Upload scene save** — same endpoint, `FileType=1` (RoomSave),
   payload is the scene blob. Response:
   `{"filename": "<blob>.dat"}` (e.g. `dorm_p<id>_v<N>.dat`).
   The upload response DTO is `{Filename, Hash, OwnershipProof}` — the
   client accepts both `Filename`/`filename` casings; `Hash` and
   `OwnershipProof` may be null.
3. **Commit** — `POST https://rooms.<apex>/rooms/{roomId}/subrooms/{subRoomId}/data`
   with JSON:

   ```json
   {
     "UnityAssetId": null,
     "RoomData":    { "Filename": "roommeta_….bin", "Hash": null, "OwnershipProof": null },
     "SubRoomData": { "Filename": "dorm_p…_vN.dat", "Hash": null, "OwnershipProof": null }
   }
   ```

   `SubRoomData.Filename` is the scene save and must become the room's
   `CurrentDataBlobName` (and the dorm-state row for dorms).
   `RoomData.Filename` is the FileType=6 metadata blob.

   **Response contract** (deserializer `NEOPBOMGIOG`, mapper
   `KMBHAKAHNGH`): legacy `{success, value, error}` envelope where
   `value` MUST contain BOTH keys, non-null:

   ```json
   {
     "success": true,
     "value": {
       "Room":            { …same DTO as GET rooms/{id} (BuildRoomServerDetails)… },
       "SubRoomDataSave": { "SubRoomDataSaveId": 0, "SubRoomId": 0,
                            "UnityAssetId": "", "DataBlob": "<blob>.dat",
                            "SavedByAccountId": 1, "SavedOnPlatform": 7,
                            "SavedOnDeviceClass": 2, "CreatedAt": "…", … }
     },
     "error": ""
   }
   ```

   The client parses each field then runs a **Dispose walk**
   (`NEOPBOMGIOG.FKDDCNLJOLF`, reached from the SaveRoom commit
   continuation) that dereferences the mapped children — a missing
   nested object or scalar leaves a null it NREs on, and the save
   persists server-side but the watch shows "Failed to save room". So
   the `Room` DTO must carry every key the `FGCPNAACHIK` mapper
   (`GLEGPFFPDBE`) reads, including the ones the include-masked
   `GET rooms/{id}` path gets away without: **DataBlob**,
   **DataBlobHash**, **MaxPlayers**, **ToxmodEnabled**, and a non-null
   **RankingContext** object, plus SubRooms/Roles/Tags/Stats.
   `SubRoomDataSave` keys per the `JKIFFPPAJNK` mapper (`PELEHJLMKJO`):
   SubRoomDataSaveId, SubRoomId, UnityAssetId, DataBlob, **DataBlobHash**,
   SavedByAccountId, SavedOnPlatform, SavedOnDeviceClass, **Description**,
   CreatedAt.

Server handling: `StorageController.Upload` (FileType 6 →
`UploadGenericAsync("roommeta")`, CDN-servable) and
`RoomsController.SubRoomData` → `ReadSaveRoomSceneRequestAsync` (nested
2023 JSON via `BlobRefDto`) → `SaveDataCore`.

## Failure modes seen

| Symptom (client) | Cause |
|---|---|
| `NDIKGKCFOCG: An error occurred` at `UploadRoomDataBlobAndSyncReload`, upload frame (`KPLOPGMJOLE`) in stack | `storage.<apex>` host not routed at the edge (Traefik `404 page not found`) — the save dies before reaching DorkNet. Probe `https://storage.<apex>/healthz`. |
| `NDIKGKCFOCG: Failed to save room`, no upload frame in stack | Commit POST rejected — historically 400 `missing_room_data_filename` because the server parsed only the flat 2020 body and missed `SubRoomData.Filename`. |
| `NDIKGKCFOCG: Failed to save room` preceded by a `NullReferenceException` at `NEOPBOMGIOG.FKDDCNLJOLF` | Commit returned 200 but `value` wasn't `{Room, SubRoomDataSave}` — the save actually persisted; only the response parse failed. |

## Room clone (2023)

The in-room "copy room" flow (`RoomModel.CopyRoom` →
`RecNet.Runtime NLDBPDCNNCF.GDHIIAHCBMN`) POSTs
`roomserver/rooms/{id}/clone` with an x-www-form-urlencoded `name=…`
body — the same `roomserver/` prefix as every other room mutation from
that client. A bare-only `rooms/{id}/clone` route 404s with an empty
body, which the client's promise layer surfaces as a message-less
`Failed to copy room: Exception of type 'CEMNLBKJABA' was thrown`.
The response must be the FULL room-details object (`FGCPNAACHIK`,
same shape as `GET roomserver/rooms/{id}`), not a status wrapper.

Blob semantics on clone (`RoomsModerationController.BareClone`):

- **First-party template source** (IsAGRoom + system-owned, e.g. the
  RecCenter seed): the clone keeps the copied scenes but gets a
  **fresh empty blob** (`CurrentDataBlobName`/scene `DataBlobName`
  cleared). Any blob on the template row is a MakerPen overlay against
  the shared baked scene and must not leak into clones; AG room
  details also require an empty `DataBlobName` on the wire.
- **User-owned source** (including AG-flagged clones of RROs): blobs
  are copied so "copy room" carries the player's MakerPen edits.

Clones are created **private** (`Accessibility=0`) regardless of the
source's visibility; the owner publishes via
`rooms/{id}/accessibility` when ready.

### Sub-room saves list is a PAGED OBJECT

`GET rooms/{id}/subrooms/{sub}/saves` (and `…/saves/no_unity_assets`)
must return `{"Results":[…],"TotalResults":N}` —
`PagedResultsDTO<SubRoomDataSaveDTO>` per the Studio decompile
(`RRStudio-decomp/...RecNet/PagedResultsDTO.cs`); the 2023 client's
`GetSubRoomDataSaves` sends `skip`/`take` and its strict reader throws
`expected:'{', actual:'['` on a bare array. Because callers run inside
larger flows, the bare-array bug surfaced as "Failed to copy room:
Exception of type '…' was thrown" (clone) and a dead private-instance
button — not as a saves error.

### Private rooms: one instance per sub-room

Private rooms (`Accessibility=0`) allow exactly ONE instance per
sub-room. `GoToController.ApplyNewInstanceAsync` funnels every join
mode (plain matchmake, JoinMode=1 new-public, JoinMode=2 new-private)
into the same deterministic invite-gated instance
(`PrivateInstanceService.EnsureForPrivateRoomAsync`, id marker
`0xEE0000`, keyed on room+subroom — dorms keep their own `0xDD0000`
flow). Joins by players who are neither the creator, an accepted
role-holder (co-owner/mod/host), nor an instance invitee return
`errorCode=4` (RoomDoesNotExist) so private rooms don't leak their
existence. Tests: `PrivateRoomInstanceTests.cs`.

## Baked Rec Room Originals and data blobs (Rec Royale)

Baked RRO rooms are seeded with empty `DataBlobName` / `CurrentDataBlobName`,
which the details builders and matchmaking resolvers pass through as an
empty `DataBlob`. The 2023 client treats that as "no room data": it never
downloads a blob, so the room has no room roles (no Maker Pen even though
the CDN overlays permissive roles for AG rooms), no CV2 room-settings
chips, no mood engine and nothing to save into. `IsRRO` (= `IsAGRoom`) is
independent of the blob, so a baked room CAN carry one: the client loads
the baked map from its own assets and layers the blob's persistence views
on top — exactly how Rec Center behaves.

Rec Royale needs this to be a game at all: the Frontier scene has no baked
game rule (contrast `PaintballTeamBattle_GameRuleWrapperCircuit`, which is
baked into the paintball scene). Its rule is the Maker Pen chip
`RecRoyaleSolos_GameRuleWrapperCircuit` / `RecRoyaleSquads_GameRuleWrapperCircuit`
(`BattleRoyaleGameRuleWrapper : GameRuleCircuitWrapperTool`), which real
Rec Room delivered inside the room blob. Without it there is no
`BattleRoyaleManager` in the scene.

Stub shape matters. Until something is saved the CDN synthesises the blob,
and the client fetches it through `RecNet.Rooms.GetRoomData` and rejects
one it deems unusable — a clone surfaces that as the bare "Failed to copy
room", a baked room as a silent load with no room roles (no Maker Pen).
Compared against Rec Center's working `room_100_v1.dat`, the old stub was
stamped `PersistedRoomVersion` 19 (client ceiling 16; only the S3-hit path
clamped) and lacked the save skeleton (`last_save_time`, `activity_id` =
LocationReplicationId, connectable graph, scene settings, object model,
`room_mood_*`). `RoomDataBlobService.BuildRroEditableBlob` now emits that
skeleton per room at version 16.

Admin switch: `POST api/admin/v1/rooms/{id}/subrooms/{sub}/blob/enable`
stamps `RoomService.SyntheticDefaultRoomDataBlobName(id)` on the scene and
room; `.../blob/reset` clears it. See `docs/admin.md` → "RRO room data
blob" for the full procedure (enable → spawn the game-rule chip → save).

## Related 2023 quirks fixed alongside

- **Play-menu search** (`IBEOONPEELF.SearchRooms`): calls
  `rooms/search_rooms/{query}&skip={n}&take={n}` — `rooms/`-prefixed
  AND paging embedded in the final path segment. Same segment style on
  `hot_rooms`, `hot_roomsandplaylists`, `search_roomsandplaylists`
  (parsed by `RoomsController.ParsePagedPathSegment`).
- **Room photo gallery** (`KLJOGJHBONK`, "Could not show images for
  room"): `api/images/v{2-5}/room/{id}?sort=CheerCount_Desc&filter=PublicOnly`
  — `sort`/`filter` arrive as enum NAMES; binding them as `int` makes
  `[ApiController]` auto-400. Bound as strings, mapped in
  `ImagesController.ParseSort`.

Tests: `DorkNet.Server.Tests/RoomSave2023Tests.cs`,
`DorkNet.Server.Tests/RoomBrowse2023Tests.cs`.
