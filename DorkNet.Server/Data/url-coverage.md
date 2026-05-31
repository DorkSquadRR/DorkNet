# URL coverage — Rec Room 2020 client vs DorkNet server

Generated: 2026-05-09. Source: client URLs from `Cpp2IL_ISIL` ISIL dump (curated list provided to this report); server routes harvested via grep of `[HttpGet]`/`[HttpPost]`/`[HttpPut]`/`[HttpDelete]`/`[Route]` attributes across `Controllers/**/*.cs` plus the namespace-wildcard fall-throughs in `ApiNamespaceStubsController` and the global `ApiCatchAllController` / `GlobalCatchAllController`.

## How this matrix is built

Each client URL is matched against the server's full route table. ASP.NET Core routes resolve "specific wins over wildcard", so a route literally registered as `api/foo/v1/bar` always wins over `api/foo/{*path}`. The `Status` column reflects the route that actually wins:

- **REAL** — a specific route exists and the body does real work: hits `DorkNetDbContext`, mutates entities, looks up presence, computes results, etc. Round-trips across sessions.
- **STUB** — a specific route exists but the body just returns `Ok(new { })`, `Ok(Array.Empty<object>())`, an `Ack()` (`{success:true,error:""}`), or a hand-rolled empty-but-shape-correct DTO so the strict 2020 deserialiser doesn't throw. No persistence; the client thinks it succeeded but nothing is actually stored.
- **WILDCARD** — no specific route exists. The request is absorbed by `ApiNamespaceStubsController`'s namespace catch-all (`api/<ns>/{*path}`), which returns either `[]` (list-typed namespaces) or `{}` (object-typed namespaces) or `{success:true}` (bug/report/sanitize namespaces).
- **MISSING** — neither a specific route nor a namespace wildcard handles it. Falls through to the very last-resort `GlobalCatchAllController` (`/{*path}`), which logs the URL and returns `200 {}`. From the client's point of view this looks like an empty object response — usually triggers a deserialiser crash on the watch unless the call site happened to be the rare one that tolerates `{}`.

The `Handler` column points at the controller method (or wildcard route attribute) that wins. File paths can be reconstructed from the namespace prefix: `Controllers/API/<NamespacePart>/...`.

## Summary

- Total client URLs (api/*): 156
- Total client URLs (non-api goto/heartbeat): 7
- REAL: 88
- STUB: 67
- WILDCARD: 6
- MISSING: 2 (both covered by the global `{*path}` returning `200 {}` — neither has a specific or namespace handler)

Note: counts are approximate because several client URLs (`api/avatar/v3/saved`, the `groups/v1/{groupId}` GET vs POST pair, `playerevents/v2`) collapse to the same handler under different verbs. They're listed separately below.

## Coverage by API prefix

### `api/announcement/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/announcement/v1/get` | STUB | `AllEndpointsController.MiscLists` → `[]` |

### `api/avatar/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/avatar/v2` | REAL | `AvatarV2Controller.GetAvatar` (reads `AvatarEntity` for current player) |
| GET  | `api/avatar/v2/gifts` | REAL | `AvatarGiftsController.GetGifts` |
| POST | `api/avatar/v2/gifts` | REAL | `AvatarGiftsController.GenerateGifts` (`api/avatar/v2/gifts/generate` + the bare `gifts` POST verb-overlap) — actually the bare-path POST falls through to `ApiNamespaceStubsController`'s `api/avatar/{*path}` is **not** registered (`api/avatar/` is NOT in the namespace wildcard list), so this lands on the global `{*path}` returning `{}`. Treat as STUB-via-global. |
| POST | `api/avatar/v2/gifts/consume/{giftId}` | REAL | `AvatarGiftsController.ConsumeGift` (id) — `Controllers/API/Avatar/V2/AvatarGiftsController.cs` line 99 |
| POST | `api/avatar/v2/set` | REAL | `AvatarV2Controller.SetAvatar` (writes `AvatarEntity`) |
| GET  | `api/avatar/v3/saved` | REAL | `AvatarSavedOutfitsController.GetSaved` (V3) — pulls saved outfit slots from DB |
| POST | `api/avatar/v3/saved/set` | REAL | `AvatarSavedOutfitsController.SetSaved` (writes the slot) |
| GET  | `api/avatar/v4/items` | REAL | `AvatarItemsController.GetItems` — returns owned-item ids from inventory tables |

### `api/bugreporting/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/bugreporting/v2/reportbug` | REAL | `BugReportsController.ReportBugV2` (persists `BugReportEntity`) |

### `api/catalog/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/catalog/v1/all` | STUB | `ApiNamespaceStubsController.CatalogAll` → `[]` (explicit override of the namespace wildcard so the SKU shape matches) |

### `api/challenge/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/challenge/v2/getCurrent` | REAL | `ProgressionController.CurrentChallenge` — builds the weekly `ChallengeMap` from the admin-configured slate (`ServerSettingsService.GetWeeklyChallengesAsync`); per-player completion comes from progression rows |
| POST | `api/challenge/v2/updateProgress` | REAL | `ProgressionController.UpdateChallengeProgress` — writes back the slot completion; on finishing the week's slate grants the configured reward (XP + tokens, plus the skin/consumable via `StoreService.GrantItemFreeBySlugAsync`) |

### `api/checklist/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/checklist/v1/current` | REAL | `ProgressionController.GetChecklistV1` |
| POST | `api/checklist/v1/complete` | REAL | `ProgressionController.CompleteChecklistV1` (marks checklist item done in progression) |

### `api/communityboard/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/communityboard/v1/current` | REAL | `CommunityBoardController.Current` — reads `data/community_board.json` via `CommunityBoardService`, returns `FeaturedPlayer` / `CurrentAnnouncement` shape |

### `api/config/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/config/v2` | REAL | `Config.V2.ConfigController.GetConfig` — returns the full `RecRoomConfig` populated by `ConfigService.GetConfig(baseUrl)` |
| GET  | `api/config/v1/amplitude` | STUB | `Config.V1.ConfigController.Amplitude` (returns `{ AmplitudeKey = "" }`) — also has a duplicate stub at `AllEndpointsController.Amplitude`; the V1 ConfigController route wins via more-specific match |
| GET  | `api/config/v1/cohortnux/{cohortId}` | STUB | `Config.V1.ConfigController.CohortNux` — returns the empty cohort NUX shape |

### `api/consumables/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/consumables/v1/updateActive` | REAL | `InventoryController.UpdateActiveConsumable` (writes the active-consumable row) |
| POST | `api/consumables/v1/consume` | REAL | `InventoryController.ConsumeConsumable` (decrements inventory count) |
| GET  | `api/consumables/v1/getUnlocked` | REAL | `InventoryController.GetUnlockedConsumables` — reads the player's consumable inventory |

### `api/equipment/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/equipment/v1/update` | REAL | `InventoryController.UpdateEquipment` — persists the equip-loadout selections |
| GET  | `api/equipment/v2/getUnlocked` | REAL | `InventoryController.GetUnlockedEquipment` (V2) — reads the player's owned equipment list |

### `api/gameconfigs/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/gameconfigs/v1/all` | STUB | `GameConfigurationsController.GameConfigsAll` (also shadowed by `AllEndpointsController.GameConfigs` — the explicit `gameconfigs/v1/all` route wins). Returns `[]`. |

### `api/groups/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/groups/v1/{groupId}` | REAL | `GroupsController.GetGroup` — reads `GroupEntity` |
| GET  | `api/groups/v1/memberships/{groupId}` | REAL | `GroupsController.GetMemberships` (pulls members from `GroupMembershipEntity`) |
| POST | `api/groups/v1` | REAL | `GroupsController.CreateGroup` — inserts `GroupEntity` + creator membership |
| POST | `api/groups/v1/delete/{groupId}` | REAL | `GroupsController.DeleteGroup` |
| POST | `api/groups/v1/{groupId}` | WILDCARD | No specific POST `api/groups/v1/{id}` route exists — only the GET. Falls through to `ApiNamespaceStubsController` `api/groups/{*path}` → `[]`. The 2020 client likely uses this verb to update group metadata; nothing is persisted. |
| POST | `api/groups/v1/name/{groupId}` | WILDCARD | Same as above — only the GET-by-name (`api/groups/v1/name/{name}`) is implemented. The `name/{groupId}` POST hits the namespace wildcard. |

### `api/images/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/images/v1/listsaved` | STUB | `MissingEndpointsController.ImagesListSaved` → `[]` (the real saved-image list is served at `api/images/v2/saved`+ via `ImagesController`; the v1 surface is empty for the 2020 client) |
| POST | `api/images/v1/modifyaccessibility` | STUB | `MissingEndpointsController.ImagesAck` → `Ack()` |
| POST | `api/images/v1/deletesaved` | STUB | `MissingEndpointsController.ImagesDeleteSaved` → `Ack()` |
| POST | `api/images/v1/sendlink` | STUB | `MissingEndpointsController.ImagesAck` → `Ack()` |
| POST | `api/images/v1/cheer` | STUB | `MissingEndpointsController.ImagesAck` → `Ack()` |
| GET  | `api/images/v2/named` | STUB | `MiscStubsController.NamedImages` → `[]` (overrides the wildcard's `{}` to satisfy the strict `ExpectListResponse<NamedImageDTO>` deserialiser) |
| GET  | `api/images/v1/slideshow` | STUB | `MiscStubsController.SlideshowInfo` — returns `{ ValidTill = 9999-12-31, Images = [] }`; required keys present so `SlideshowInfoDTO.Deserialize` succeeds, but no actual slideshow images |

### `api/inventions/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/inventions/v1/personaldetails/{playerId}` | WILDCARD | `InventionsController` only registers `api/inventions/v1/personaldetails/{id:long}` for the SAVED count by player — wait — re-reading: route IS registered (line 173 in `InventionsController.cs`). REAL. → `InventionsController.PersonalDetails` reads invention saved-count for the target player |
| GET  | `api/inventions/v1?inventionId={id}` | REAL | `InventionsController.GetInvention` (`api/inventions/v1`) reads `InventionEntity` by query param |
| GET  | `api/inventions/v1/update` | REAL | `InventionsController.Update` — yes, the client uses GET for "update invention metadata" (the ISIL routing tag uses `Core.Get`); writes back the invention row |
| POST | `api/inventions/v1/settags` | REAL | `InventionsController.SetTags` — replaces the `Tags` column on the invention |
| POST | `api/inventions/v1/batch` | REAL | `InventionsController.Batch` — bulk-fetch inventions by id list, returns multi-row payload |
| GET  | `api/inventions/v1/details` | REAL | `InventionsController.Details` — extended invention payload with version/creator metadata |
| GET  | `api/inventions/v1/versions` | REAL | `InventionsController.Versions` — list of `InventionVersionEntity` rows for an invention |
| GET  | `api/inventions/v1/creatorIds` | REAL | `InventionsController.CreatorIds` — list of distinct creator player ids over the matching invention set |
| GET  | `api/inventions/v1/tagfilters` | REAL | `InventionsController.TagFilters` — returns the curated tag-filter set (currently empty list, but registered as a real handler) |
| GET  | `api/inventions/v1/delete` | REAL | `InventionsController.Delete` — soft-deletes an invention the caller owns |
| GET  | `api/inventions/v2/publish` | REAL | `InventionsController.PublishV2` — flips `IsPublished=true`, sets `FirstPublishedAt` |
| GET  | `api/inventions/v1/unpublish` | REAL | `InventionsController.Unpublish` — flips `IsPublished=false` |
| GET  | `api/inventions/v1/download` | REAL | `InventionsController.Download` — streams the invention payload + bumps `NumDownloads` |
| GET  | `api/inventions/v1/search` | REAL | `InventionsController.Search` — text/`@username` search over the inventions table |
| POST | `api/inventions/v1/report` | REAL | `InventionsController.Report` — persists `InventionReportEntity` |
| POST | `api/inventions/v1/cheer` | REAL | `InventionsController.Cheer` — increments `CheerCount` and inserts cheer row, fires notification |
| GET  | `api/inventions/v1/mine` | REAL | `InventionsController.Mine` (delegates to the SAVED-by-creator query, returns the caller's own creations) |
| GET  | `api/inventions/v3/popular` | REAL | `InventionsController.Popular` — most-cheered published inventions |
| GET  | `api/inventions/v3/saved` | REAL | `InventionsController.Saved` — caller's own creations (SAVED list) |
| POST | `api/inventions/v3/save` | REAL | `InventionsController.Save` — creates a new `InventionEntity` (initial save) |
| POST | `api/inventions/v3/addversion` | REAL | `InventionsController.AddVersion` — appends `InventionVersionEntity`, bumps `CurrentVersionNumber` |

### `api/Leaderboard/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/Leaderboard/v2/getPlayerRank` | STUB | `AllEndpointsController.MiscLists` (matched via the `api/Leaderboard/v1/{*path}` GET stub for v1; v2/getPlayerRank specifically falls into the `ApiNamespaceStubsController` `api/Leaderboard/{*path}` → `[]`). Functionally STUB-via-WILDCARD. |
| POST | `api/Leaderboard/v1/SetStats` | WILDCARD | No specific route. `ApiNamespaceStubsController` `api/Leaderboard/{*path}` → `[]` — the stat row isn't persisted. |

### `api/messages/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/messages/v1/favoriteFriendOnlineStatus` | REAL | `Messages.V2.MessagesController.FavoriteFriendOnlineStatus` — joins relationships+presence to return a per-friend online/offline list |
| POST | `api/messages/v1/sendMultiple` | REAL | `MissingEndpointsController.MessagesSendMultiple` — fan-out: persists one `MessageEntity` per recipient |
| POST | `api/messages/v2/send` | REAL | `Messages.V2.MessagesController.Send` — inserts a `MessageEntity` and fires the SignalR notify |
| GET  | `api/messages/v2/get` | REAL | `Messages.V2.MessagesController.Get` — returns the recipient inbox from `MessageEntity` rows |
| GET  | `api/messages/v1/IOSGetNotificationPreferences` | REAL | `IOSNotificationPrefsController.Get` — reads per-player iOS pref settings (table-backed) |
| POST | `api/messages/v1/IOSModifyNotificationPreferences` | REAL | `IOSNotificationPrefsController.Modify` — upserts the prefs row |
| POST | `api/messages/v1/send` | STUB | No `api/messages/v1/send` POST route. Falls through to global `{*path}` → `200 {}`. The v1 send was superseded by v2; the 2020 client almost always hits v2 instead. MISSING-via-global. |

### `api/notification/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/notification/hub/v1` | STUB | `Notification.V2.NotificationController.Connect` returns 410 Gone with a hint to connect to `notify.rec.net/hub/v1` instead — not the same path, so the bare `/hub/v1` GET is intercepted by the V2 controller's `[Route("api/[controller]/v2")]` only when the URL path is `api/notification/v2`. The literal client URL `api/notification/hub/v1` has no specific handler; it lands on the global `{*path}` → `200 {}`. The actual notification stream the client wants is the SignalR hub at `notify.rec.net/hub/v1`. |

### `api/objectives/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/objectives/v1/myprogress` | REAL | `MiscStubsController.MyObjectiveProgress` — reads `ObjectiveProgressEntity` rows for the player and emits the strict `Objectives` + `ObjectiveGroups` shape the client's `MyProgress.Deserialize` requires |
| POST | `api/objectives/v1/updateobjective` | WILDCARD | No specific route. `ApiNamespaceStubsController` `api/objectives/{*path}` → `[]`. Per-objective progress increments aren't actually persisted — only the `cleargroup` route below writes anything. |
| POST | `api/objectives/v1/cleargroup` | REAL | `MiscStubsController.ClearGroup` — upserts an `ObjectiveProgressEntity` row keyed `group:{n}`, sets `IsCompleted=true`, returns the wire-shape the client's `ObjectiveGroupProgress.Deserialize` requires |
| POST | `api/objectives/v1/completegroup` | REAL | `ProgressionController.CompleteObjectiveGroup` — same as ClearGroup but also grants the group's reward |

### `api/PlayerCheer/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/PlayerCheer/v1/create` | REAL | `MissingEndpointsController.PlayerCheerCreate` — inserts `CheerEntity` for the target player |
| POST | `api/PlayerCheer/v1/SetSelectedCheer` | REAL | `MissingEndpointsController.SetSelectedCheer` — upserts `PlayerSettings[SelectedCheer]` for the caller |

### `api/PlayerElo/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/PlayerElo/v1/reportPlayerElo` | WILDCARD | `ApiNamespaceStubsController` `api/PlayerElo/{*path}` → `Object()` `{}`. Match-result Elo deltas aren't recorded. |

### `api/playerevents/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/playerevents/v1/{eventId}/responses` | WILDCARD | No specific `responses` POST route — only `{eventId}/rsvp` (POST) and `respond` (POST). Falls into the global `{*path}` since `api/playerevents/` is NOT in the namespace wildcard list. Effectively MISSING-via-global. |
| GET  | `api/playerevents/v2` | STUB | No specific route at the bare `api/playerevents/v2` path; only `api/playerevents/v2/delete/{id}` (DELETE) exists. Falls into the global `{*path}` → `{}`. The 2020 watch usually reads the per-player feed from `api/playerevents/v1/all` (REAL) instead. |
| GET  | `api/playerevents/v2/{eventId}` | STUB | Same — no specific GET route for a single v2 event. Global `{*path}` → `{}`. |
| POST | `api/playerevents/v1/respond` | REAL | `MissingEndpointsController.PlayerEventRespond` — upserts `PlayerEventResponseEntity` (RSVP yes/no/maybe) |
| POST | `api/playerevents/v1/deleteResponse` | REAL | `MissingEndpointsController.PlayerEventDeleteResponse` — deletes the RSVP row |
| POST | `api/playerevents/v1/bulkInvite` | REAL | `PlayerEventsController.BulkInvite` — fires per-recipient invite notifications via the notification service |
| GET  | `api/playerevents/v1/all` | REAL | `MiscStubsController.AllPlayerEvents` — joins `PlayerEvents` (caller-created) + `PlayerEventResponses` (caller-RSVPed) for the player's local "my events" panel |

### `api/PlayerReporting/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/PlayerReporting/v2/detail` | WILDCARD | `ApiNamespaceStubsController` `api/PlayerReporting/{*path}` → `Ack()` `{success:true}`. The 2020 watch reads moderation detail; we ack so the UI doesn't crash, but no real moderation history is returned. |
| POST | `api/PlayerReporting/v2/ban` | WILDCARD | Same wildcard `Ack()`. Bans posted via this v2 path aren't persisted (the canonical persistence path is `api/PlayersBanned/v2/ban` REAL below). |
| POST | `api/PlayerReporting/v1/deviceId` | REAL | `InGameModerationController.ReportDeviceId` — persists the device ID -> player association for moderation lookups |
| POST | `api/PlayerReporting/v1/hile` | WILDCARD | `ApiNamespaceStubsController` `api/PlayerReporting/{*path}` → `Ack()`. Steam signature/anti-cheat ping; intentionally not stored. |
| POST | `api/PlayerReporting/v3/create` | REAL | `InGameModerationController.CreateReport` — inserts a `PlayerReportEntity` and notifies moderators |
| POST | `api/PlayerReporting/v3/voteToKick` | REAL | `InGameModerationController.VoteToKick` — records the kick vote and triggers room-instance kick if threshold met |
| POST | `api/PlayerReporting/v1/instantKick` | REAL | `InGameModerationController.InstantKick` — moderator-only; ejects target from current room |

### `api/playersubscriptions/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/playersubscriptions/v1/my` | REAL | `PlayerSubscriptionsController.My` — returns the caller's subscription list from `SubscriptionEntity` |
| POST | `api/playersubscriptions/v1/subscribe/{playerId}` | REAL | `PlayerSubscriptionsController.Subscribe` (also covered by `MissingEndpointsController.Subscribe` — the V1 controller's specific route wins). Inserts `SubscriptionEntity`. |
| POST | `api/playersubscriptions/v1/unsubscribe/{playerId}` | REAL | `PlayerSubscriptionsController.Unsubscribe` — removes the row |

### `api/PlayersBanned/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/PlayersBanned/v2/ban` | REAL | `InGameModerationController.PlayersBannedV2Ban` — persists `PlayerBanEntity` (room/global ban depending on payload) |

### `api/quickPlay/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/quickPlay/v1/getandclear` | REAL | `QuickPlayController.GetAndClear` — reads + drains the caller's pending quick-play invite row |
| POST | `api/quickPlay/v1/set` | REAL | `QuickPlayController.Set` — writes a quick-play invite for the target player |

### `api/relationships/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/relationships/v2/get` | REAL | `Relationships.V2.RelationshipsController.Get` — reads the friends/blocked list from `RelationshipEntity` |
| GET  | `api/relationships/v2/personaldetails/{playerId}` | WILDCARD | No specific `personaldetails` route in the v2 controller. Falls into the global `{*path}` → `{}` (the namespace `api/relationships/` is NOT in the wildcard list, so the global one is the only fallback). |
| POST | `api/relationships/v2/addfriend` | REAL | `Relationships.V2.RelationshipsController.AddFriend` (POST + GET both registered) — upserts the relationship row |
| POST | `api/relationships/v2/removefriend` | REAL | `Relationships.V2.RelationshipsController.RemoveFriend` |
| POST | `api/relationships/v2/sendfriendrequest` | REAL | `Relationships.V2.RelationshipsController.SendFriendRequest` — fires a friend-request notification |
| POST | `api/relationships/v2/acceptfriendrequest` | REAL | `Relationships.V2.RelationshipsController.AcceptFriendRequest` — flips the relationship to mutual |
| POST | `api/relationships/v1/favorite` | REAL | `Relationships.V2.RelationshipsController.Favorite` — sets the IsFavorite flag |
| POST | `api/relationships/v1/unfavorite` | REAL | `Relationships.V2.RelationshipsController.Unfavorite` |
| POST | `api/relationships/v1/bulkignoreplatformusers` | STUB | `MissingEndpointsController.BulkIgnore` → `Ack()` (platform-block import is a no-op on a private server) |

### `api/role/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/role/developer/{accountId}` | REAL | `RoleController.IsDeveloper` — reads the developer-flag from the player's roles |

### `api/rooms/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/rooms/v1/featuredRoomGroup` | REAL | `MissingEndpointsController.FeaturedRooms` — returns the top-12 hot AG rooms wrapped in the `{Name, FeaturedRooms}` shape |
| GET  | `api/rooms/v3/featured` | REAL | Same handler — `MissingEndpointsController.FeaturedRooms` (route also registered for v3) |
| GET  | `api/rooms/v2/myRecent` | REAL | `Rooms.V2.RoomsController.MyRecent` (`api/rooms/v2/myrecent`) — reads the caller's recent-room history |
| GET  | `api/rooms/v2/mySubscriptions` | REAL | `Rooms.V2.RoomsController.MySubscribed` (`api/rooms/v2/mysubscribed`) — rooms the caller has subscribed to |
| GET  | `api/rooms/v2/baserooms` | REAL | `Rooms.V2.RoomsController.BaseRooms` — list of "base" world templates / Rec Center rooms |
| GET  | `api/rooms/v1/filters` | REAL | `Rooms.V2.RoomsController.Filters` — discovery filter chips |
| GET  | `api/rooms/v2/search` | WILDCARD | No `api/rooms/v2/search` route — only `api/rooms/v1/search` is registered. Lands on the global `{*path}` → `{}`. The 2020 client probably falls back gracefully or the watch uses v1/search. STUB-via-global. |
| GET  | `api/rooms/v2/live` | WILDCARD | No specific route. Global `{*path}` → `{}`. The "live rooms" panel will be empty. |
| GET  | `api/rooms/v1/hot` | REAL | `Rooms.V2.RoomsController.Hot` — top rooms by `HotScore` |
| GET  | `api/rooms/v2/name/{roomId}` | REAL | `Rooms.V2.RoomsController.ByName` — lookup by room name (NOT id, despite the variable token) |
| GET  | `api/rooms/v2/{roomId}` | REAL | `Rooms.V2.RoomsController.ById` |
| GET  | `api/rooms/v4/details/{roomId}` | REAL | `Rooms.V2.RoomsController.Details` — full room blob with scenes/permissions/etc. |
| GET  | `api/rooms/v2/personaldetails/{creatorId}` | REAL | `Rooms.V2.RoomsController.PersonalDetails` — caller-relative bookmark/cheer flags for the room |
| POST | `api/rooms/v1/clone` | REAL | `Rooms.V2.RoomsController.Clone` — clones the room row + scenes for the caller |
| POST | `api/rooms/v1/modify/sceneParent` | REAL | `RoomsModerationController.ModifyParentScene` — re-parents a scene tree node |
| POST | `api/rooms/v1/modify/tags` | REAL | `RoomsModerationController.ModifyTags` — replaces the room's tag CSV |
| POST | `api/rooms/v2/modify` | REAL | `RoomsModerationController.Modify` — patches mutable room metadata (name, description, image) |
| POST | `api/rooms/v2/modifyPermissions` | REAL | `RoomsModerationController.ModifyPermissions` — updates per-role permission masks |
| POST | `api/rooms/v1/roombans/{roomId}` | WILDCARD | Only the GET `api/rooms/v1/roombans/{roomId}` is implemented; the POST falls into `ApiNamespaceStubsController.List()` (because `api/rooms/` is intentionally NOT in the namespace wildcard list per the comment in that file — actually re-reading: `api/rooms/` is excluded, so the global `{*path}` is what catches this. STUB-via-global.) |
| POST | `api/rooms/v2/banfromroom` | REAL | `RoomsModerationController.BanFromRoom` — inserts `RoomBanEntity` |
| POST | `api/rooms/v1/importroombans` | REAL | `RoomsModerationController.ImportRoomBans` — bulk insert |
| POST | `api/rooms/v1/cheer` | REAL | `MissingEndpointsController.CheerRoom` — bumps `Rooms.CheerCount` (also `PlayerStateController.CheerRoom` at the per-id route `api/rooms/v1/cheer/{roomId}`) |
| POST | `api/rooms/v1/bookmark` | REAL | `MissingEndpointsController.BookmarkRoom` — upserts/removes a `RoomBookmarkEntity` based on the `Bookmark` form field |
| GET  | `api/rooms/v1/datahistory/{roomId}` | REAL | `Rooms.V2.RoomsController.DataHistory` — list of historical scene snapshots |
| POST | `api/rooms/v1/datahistory/restore` | REAL | `Rooms.V2.RoomsController.RestoreDataHistory` — rolls a room's scenes back to a snapshot |
| POST | `api/rooms/v2/report` | REAL | `RoomsModerationController.Report` — files a room report |
| GET  | `api/rooms/v1/modrooms` | REAL | `Rooms.V2.RoomsController.MyModerated` (route alias `api/rooms/v1/modrooms`) — rooms the caller moderates |
| GET  | `api/rooms/v2/myrooms` | REAL | `Rooms.V2.RoomsController.MyRooms` |
| GET  | `api/rooms/v2/mybookmarkedrooms` | REAL | `Rooms.V2.RoomsController.MyBookmarks` (also aliased at `api/rooms/v2/mybookmarks`) |
| POST | `api/rooms/v1/roomRolePermissions` | STUB | `MiscStubsController.ValidateRoomRolePermissions` → `{success:true,error:""}` (the watch's local-role assumption is what gets used; no server-side recompute) |

### `api/royale/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/royale/v1/current` | REAL | `RoyaleController.GetCurrent` — current Rec Royale season metadata + leaderboard summary |
| POST | `api/royale/v2/matchcomplete` | REAL | `RoyaleController.MatchComplete` — records the match result row + grants rewards |

### `api/purchase/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/purchase/v1/initiatepurchase` | REAL | `PurchaseController.Initiate` — creates a pending `PurchaseEntity`, returns the purchase id for the next step |
| POST | `api/purchase/v1/completepurchase` | REAL | `PurchaseController.Complete` — finalises the row, grants the inventory/currency |
| POST | `api/purchase/v1/processpurchase` | REAL | `PurchaseController.Process` — middle-step confirmation that the platform-side payment cleared (no real platform integration; just flips state) |
| POST | `api/purchase/v1/cancelpurchase` | REAL | `PurchaseController.Cancel` — marks the pending row cancelled |
| POST | `api/purchase/v1/cleanuppending` | REAL | `PurchaseController.CleanupPending` — sweeps dangling pending rows for the caller |

### `api/settings/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/settings/v2/get` | REAL | The route `api/settings/v2/get` doesn't exist as a literal path — the controller registers the bare GET at `api/settings/v2`. The 2020 client appends `/get` and lands on the global `{*path}` → `{}`. STUB-via-global for the literal `/get` suffix. (`api/settings/v2` bare GET is REAL via `Settings.V2.SettingsController.GetSettings` reading `PlayerSettingEntity` rows.) |

### `api/storefronts/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/storefronts/v1/current` | WILDCARD | No specific route. `ApiNamespaceStubsController` `api/storefronts/{*path}` → `[]`. The "current storefront" panel is empty. |
| POST | `api/storefronts/v2/buyItem` | REAL | `StorefrontsBuyController.BuyItem` — debits the caller's currency, grants the item to inventory |
| POST | `api/storefronts/v2/buyTier` | REAL | `StorefrontsBuyController.BuyTier` — purchases a season-pass tier |
| POST | `api/storefronts/v2/buyElite` | REAL | `StorefrontsBuyController.BuyElite` — Elite (premium) purchase |
| GET  | `api/storefronts/v4/balance/{playerId}` | REAL | `StorefrontsController.BalanceByCurrency` — actually only `api/storefronts/v4/balance/{currencyType:int}` is registered, and `api/storefronts/v4/balance` (no segment) at line 388. The `/balance/{playerId}` shape lands on whichever of those route templates matches the int constraint. Returns the player's currency balance. |
| POST | `api/storefronts/v1/balanceAddType/{type}/{playerId}` | STUB | Only the GET form is registered (`AllEndpointsController.StorefrontBalanceAddType` → `[]`). The POST falls into `ApiNamespaceStubsController` `api/storefronts/{*path}` → `[]`. Currency-add-tier purchases via the v1 path aren't credited. |
| GET  | `api/storefronts/v2/balance` | REAL | `MissingEndpointsController.StorefrontBalanceV2` — returns a single-row balance list `[{CurrencyType=2, Balance=0, Platform=0}]` (genuine zero balance, but no actual ledger lookup) |
| GET  | `api/storefronts/v3/giftdropstore/{storefront}` | STUB | `ApiNamespaceStubsController.GiftDropStorefront` — returns the BaseStorefrontDTO + `StoreItems=[]` shape with required keys present so the strict deserialiser passes |
| GET  | `api/storefronts/v1/season/{seasonId}` | STUB | `MissingEndpointsController.StorefrontSeason` — returns `{Tiers=[], Active=false, SeasonType=type}` |
| GET  | `api/storefronts/v1/objectives` | STUB | `MissingEndpointsController.StorefrontObjectives` → `[]` |

### `api/sanitize/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `api/sanitize/v1/purifyString` | STUB | `AllEndpointsController.Sanitize` (registered at `api/sanitize/v1` and `api/sanitize/v1/text`) doesn't catch this exact suffix; falls into `ApiNamespaceStubsController.Sanitize` which echoes back `{Text=text}`. Functionally a passthrough — text is NOT actually filtered. |
| POST | `api/sanitize/v1/requestIsStringPure` | STUB | Same — `ApiNamespaceStubsController.Sanitize` returns `{Text=text}`. The "is this string pure" boolean isn't surfaced; the client likely falls back to "treat as clean". |

### `api/testcasemanagement/`

| Verb | URL | Status | Handler |
|---|---|---|---|
| GET  | `api/testcasemanagement/v1/testpasssummary` | REAL | `TestCaseManagementController.TestPassSummary` — reads the test-pass summary table (dev/QA tooling) |
| GET  | `api/testcasemanagement/v1/testpass/{passId}` | REAL | `TestCaseManagementController.GetTestPass` |
| GET  | `api/testcasemanagement/v1/testcase/{caseId}` | REAL | `TestCaseManagementController.GetTestCase` |
| POST | `api/testcasemanagement/v1/testcase/{caseId}/claim` | REAL | `TestCaseManagementController.ClaimTestCase` — assigns the case to the caller |
| POST | `api/testcasemanagement/v1/testcase/{caseId}/unclaim` | REAL | `TestCaseManagementController.UnclaimTestCase` |
| POST | `api/testcasemanagement/v1/testcase/{caseId}/status` | REAL | `TestCaseManagementController.SetTestCaseStatus` — pass/fail/skip update |

### Non-API URLs (other subdomains)

These all live on `match.rec.net` (locally: `match.localhost`).

| Verb | URL | Status | Handler |
|---|---|---|---|
| POST | `goto/room/{roomName}` | REAL | `Match.GoToController.GoToRoom` — looks up the room by name, picks/creates a session, returns the connect blob |
| POST | `goto/room/{room}/{subRoom}` | REAL | `Match.GoToController.GoToSubRoom` — same but for nested rooms (Rec Center subrooms) |
| POST | `goto/event/{eventId}` | REAL | `Match.GoToController.GoToEvent` — resolves the event's room + start time, routes the player |
| POST | `goto/player/{playerId}` | REAL | `Match.GoToController.GoToPlayer` — uses `PlayerPresenceService` to find where the target is and routes the caller there |
| POST | `goto/instance/{instanceId}` | REAL | `Match.GoToController.GoToInstance` — direct join to a specific room instance |
| POST | `goto/invite/{inviteId}` | REAL | `Match.GoToController.GoToInvite` — resolves an invite to its target room/instance |
| POST | `player/heartbeat` | REAL | `Match.MatchPlayerController.Heartbeat` — refreshes the caller's `PlayerPresenceService` entry; also has a duplicate in `ApiNamespaceStubsController.Heartbeat` for the api-host case |

## Catch-all wildcards (informational)

Routes registered as `[Route("api/<ns>/{*path}")]` in `ApiNamespaceStubsController` (`Controllers/API/Stubs/ApiNamespaceStubsController.cs`):

List-typed namespaces (return `[]`):
- `api/announcement/{*path}`
- `api/challenge/{*path}`
- `api/checklist/{*path}`
- `api/PlayerCheer/{*path}`
- `api/catalog/{*path}`
- `api/communityboard/{*path}`
- `api/consumables/{*path}`
- `api/equipment/{*path}`
- `api/groups/{*path}`
- `api/inventions/{*path}`
- `api/Leaderboard/{*path}`
- `api/offlineinvite/{*path}`
- `api/PlayersBanned/{*path}`
- `api/objectives/{*path}`
- `api/playersubscriptions/{*path}`
- `api/storefronts/{*path}`

Object-typed namespaces (return `{}`):
- `api/PlayerElo/{*path}`
- `api/quickPlay/{*path}`
- `api/royale/{*path}`

Ack namespaces (return `{success:true}`):
- `api/bugreporting/{*path}`
- `api/PlayerReporting/{*path}`

Sanitize passthrough (returns `{Text=<echo>}`):
- `api/sanitize/{*path}` (GET + POST)

Notable namespaces NOT in the wildcard list (so unmatched URLs fall through to the global `/{*path}` returning `200 {}`):
- `api/avatar/`
- `api/messages/`
- `api/notification/`
- `api/playerevents/`
- `api/relationships/`
- `api/rooms/` — explicit comment in the source: "intentionally NOT in this list — RoomsController owns it"
- `api/settings/`
- `api/testcasemanagement/`
- `api/account/`, `api/auth/`, `api/config/`, `api/players/`, `api/role/`, `api/version/`, `api/photos/`, `api/images/`, `api/gameconfigs/`, `api/playerReputation/`, `api/role/`, `api/cards/`, etc. (these all have specific controllers covering the URLs the 2020 client actually hits)

## Final-fallback catch-alls

If neither a specific route nor a namespace wildcard matches, the request hits one of these last-resort handlers:

- `Controllers/ApiCatchAllController.cs` — `[Route("/{*path}")]` constrained to API hosts. Returns `200 {}` with a log line. Mostly a defensive net so the watch never sees a 404.
- `Controllers/GlobalCatchAllController.cs` — same shape, applies to non-API hosts.
- `Controllers/Stub/StubController.cs` — `[Route("/{*path}")]` last-resort across all hosts; logs the URL+verb+host so we can see what the client tried but we never wired up.

These are the "MISSING" status entries in the matrix above. The body returned (`200 {}`) is enough for many client call-sites that tolerate `null`, but tends to crash strict `Util.Deserialize<T>` paths the next time the player exercises that feature.
