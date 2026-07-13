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
