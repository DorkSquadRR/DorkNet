# Rec Room 2023-03-21 client — complete API contract

_Generated 2026-07-27 from the March-2023 client binary._

Rec Room shut down, so the client can no longer be observed against a live
server. Everything here was recovered statically from the shipped build and
cross-checked against the DorkNet server source. This is the reference for
making DorkNet answer every request the game makes.

## Sources

| What | Where |
|---|---|
| Client ISIL disassembly (contains the string literals) | `C:\tmp\recroom-2023-03-21-isil\IsilDump` |
| Client decompiled C# (Il2CppInterop proxies, no literals) | `C:\tmp\recroom-2023-03-21-decompiled` |
| Client package / metadata | `C:\tmp\recroom-2023-03-21-devdork-package`, `global-metadata.dat` |
| Nearest dump with real type layout (2023-06-21) | `Recnet-old\dist\RecRoom-2023.06.21-steam\il2cppdump\dump.cs` |
| Server implementation | `DorkNet.Server/Controllers/**`, `DorkNet.Services.*` |

The API client layer lives in the `RecNet.Runtime` assembly. Route literals
appear there only in the ISIL disassembly — the decompiled C# has the type and
field layout but no strings, so both are needed.

## How to read the client binary

### Route literals

Routes are moved into an argument register just before the call into the shared
HTTP dispatcher:

```
076 Move r8, "rooms/bulk"
```

Some routes are `String.Format` templates (`"role/{0}/{1}"`), so the literal is a
shape rather than a path. Grepping for the literal alone will mis-classify those
as missing — check the call site.

### HTTP verbs

The small integer moved into the neighbouring argument register is a
`BestHTTP.HTTPMethods` ordinal. Confirmed from the static constructor of
`RecNet.Runtime/HNLCIDLIIBO`, which builds a `Dictionary<HTTPMethods,String>`:

| Ordinal | Verb |
|---|---|
| 0 | GET |
| 1 | HEAD |
| 2 | POST |
| 3 | PUT |
| 4 | DELETE |
| 5 | PATCH |
| 6 | MERGE |

A verb can be chosen at runtime. `rooms/bulk` preloads 2 (POST) and
conditionally swaps in 0 (GET) when the id count is under 100, so that one route
legitimately needs both verbs. Implementing only one silently breaks the other.

### Response shapes

The issuing method signature is the response contract. `Task<List<X>>` means a
JSON array; `FGLDKEJLAKB<System.Int32>` means the body is a **bare integer**, not
an object wrapping one. Bare-scalar endpoints are a recurring source of defects —
returning `{"Limit":1000}` where the client reads `1000` makes the deserialize
throw and the feature silently fail.

Watch for a projection between the wire type and the method return type. 
`comments/unreadcounts` returns `Dictionary<long,uint>` from the method, but the
wire payload is `List<UnreadRoomComments>` — a `Func<List<...>, Dictionary<...>>`
runs client-side over the deserialized array.

### JSON key names

Most `RecNet` DTO properties are obfuscated and their wire names come from
attribute metadata that the dumps do not render, so exact key spelling is often
**not recoverable statically**. Two things make this tractable:

- Json.NET matches member names case-insensitively and ignores unknown members,
  so casing is forgiving and emitting extra alias keys is inert.
- Some DTOs are *not* obfuscated. `RecNet.KickPlayerDTO` has real public fields
  (`GameSessionId`, `PlayerIds`) and is serialized with Unity `JsonUtility`,
  which writes field names verbatim.

Where a key could not be proven, this document says so rather than guessing.

## Coverage summary

- **565** route literals traced across **13** subsystems 
  (**518** are real HTTP routes; the rest are cache keys, deeplink
  prefixes, MIME types or notification topics).
- **527** endpoints diffed against the DorkNet server.
- **315** defects found: 180 degraded, 106 breaks-gameplay, 25 cosmetic, 4 none.

Defect classes:

| Status | Count | Meaning |
|---|---|---|
| `OK` | 210 | handler exists, verb and shape match |
| `SHAPE_MISMATCH` | 183 | wrong JSON shape, keys or body binding |
| `MISSING` | 82 | no handler — the client gets 404 |
| `VERB_MISMATCH` | 27 | route exists under a different verb — 405 |
| `STUB` | 16 | handler returns placeholder data or drops the write |
| `UNKNOWN` | 5 | could not be proven from the binary |
| `CASING_MISMATCH` | 2 | right shape, wrong key casing |
| `NOT_A_ROUTE` | 2 | literal is not an HTTP request |

## Realtime (SignalR) surface

Not every request is HTTP. The client opens a SignalR hub at `{0}hub/v1`
(`RecNet.Runtime/OMHPBIEHOHN`, negotiate + `hub/v1`).

- Server→client messages arrive on the hub method named **`"Notification"`**
  (singular) carrying a **single `string`** argument whose content is JSON
  `{"Id":..., "Msg":...}`. The handler is registered as `Action<string>`.
  Sending `ReceiveNotification`, or passing an object instead of a string,
  makes every push silently vanish.
- Client→server: `SubscribeToPlayers` / `UnsubscribeFromPlayers`, plus
  `"SubscribeTo"` / `"UnsubscribeFrom"` prefixes composed at runtime.
- Presence pushes are routed by the friend graph server-side, so the
  subscription calls are advisory rather than load-bearing.

### Push dispatch keys — the numeric-vs-name trap

The envelope's `Id` is looked up in a dispatch dictionary the client built
at startup. Handlers are registered two ways, and the difference decides
what the server must put in `Id`:

- **Enum overload** — the client calls `Int32.ToString()` on the id and
  registers ONLY that numeric string (`RecNet.Runtime/OMHPBIEHOHN.txt:662`
  and `:733`). There is no enum-name companion key.
- **String overload** — registers exactly one bespoke name.

Lookup is a plain case-sensitive `Dictionary.TryGetValue` that returns
silently on a miss (`OMHPBIEHOHN.txt:4052-4237`). A wrong key is not an
error anywhere — on the server, on the wire, or in the client log. The push
just never arrives, which makes this class of bug very easy to miss.

So **send the number**, except for these six, which are string-keyed:

| Id | Wire key |
|---|---|
| 11 SubscriptionUpdateProfile | `AccountUpdate` |
| 12 SubscriptionUpdatePresence | `PresenceUpdate` |
| 13 SubscriptionUpdateGameSession | `RoomInstanceUpdate` |
| 15 SubscriptionUpdateRoom | `RoomUpdate` |
| 90 ChatMessageReceived | `ChatMessageReceived` |
| 95 CommunityBoardUpdate | `CommunityBoardUpdate` |

Ids the 2023 client registers NO handler for — sending them is a no-op:
4, 15 (numeric form), 21, **24 ModerationRoomBan**, 40, 85. Room bans must
therefore go out as `ModerationKick` (22) with `IsBan` set; the moderation
manager registers only 22, `ModerationUnkick` and 23
(`RecNet.Runtime/FPIBGPIAOBI.txt:310,323,335`).

Ids the client handles that this server never sends (dead client features,
available if wanted): 3, 5, 23, 32, 60, 110, 120, 121, plus a long tail of
string channels — `GoToFailure`, `PhotonAccessToken`, `AppVersionUpdate`,
`GameConfig.Refresh`, `ReputationUpdate`, `KeepsakeInstanceAdded/Removed`,
`RoomCurrencyCreated/Modified/Deleted`, `RoomCommentDeleted` and others.

### Hub methods

The client invokes exactly one server method: `SubscribeToPlayers`, with a
`{PlayerIds: List<int>}` payload, fire-and-forget. The `SubscribeTo`/
`UnsubscribeFrom` composition exists in the binary but has no callers in
this build, and `UnsubscribeFromPlayers` does not appear at all — the
server's extra hub methods are unused but harmless. Presence is routed
server-side by the friend graph, so these calls are advisory.

### Negotiate

`POST {notifyHost}/hub/v1/negotiate` with no `negotiateVersion` (protocol
version 0), so ASP.NET Core's default `MapHub` response satisfies it. Auth
is a standard `Authorization: Bearer` header on both negotiate and the
WebSocket upgrade — no query-string `access_token` handling is needed.

## Already fixed

These were found by this audit and corrected in the same pass. Each was
verified against the binary before the change, and the full test suite passes.

| Endpoint | Was | Now |
|---|---|---|
| `GET role/moderator/{id}` | 404 on every remote player | route added alongside role/developer |
| `GET comments/unreadcounts` | object body where the client reads an array | returns a JSON array of per-room rows |
| `GET api/banappeal/generateCode` | POST-only (405) + object body | GET added; body is a bare JSON string |
| `POST api/ageverification/generateCode` | object body where the client reads a string | body is a bare JSON string |
| `POST api/clubreporting/v1/report` | read `message`; client sends `details` | reads `details` first |
| `POST api/screensharereports/v1/report` | `ReportedPlayerId`/`ImageName` never bound | binds the client keys; image kept in the report text |
| `POST api/playerwarnings` | GET-only (405) | create handler added, moderator-gated |
| `POST api/playerwarnings/acknowledge` | 400 — demanded a warningId the client never sends | no-arg call acknowledges the newest warning |
| `POST api/PlayerReporting/v1/instantKick` | raw JSON body never parsed — kicked nobody | reads the KickPlayerDTO body |
| `PUT player/photonregionpings` | parsed only the 2020 pair shape | also reads one field per region name |
| `POST player/notifydisconnect` | bound 2020 `otherPlayerId` only | also binds `PlayerId`/`RoomInstanceId` |
| `PUT player/{statusvisibility,vrmovementmode,avoidjuniors}` | echoed and discarded | persisted per player and read back on heartbeat |
| `GET econ/customAvatarItems/v1/itemOwnershipLimit` | object body where the client reads an int | bare JSON integer |
| `GET api/customAvatarItems/v1/isCreationAllowedForAccount` | object body where the client reads a bool | bare JSON boolean |
| `GET api/customAvatarItems/v2/fromCreator/{id}` | bare array; client reads {Results,TotalResults} | paged container |
| `POST econ/customAvatarItems/v1/{id}/purchase` | 404 — route only existed under api/ | econ/ alias added |
| `GET api/ugcPurchasables/v1/items/room/{roomId}` | 404 — only the ?roomId= form existed | path form added |
| `POST api/equipment/v1/update` | array body failed model binding (400) | accepts an array or a single object |
| `POST api/consumables/v1/transfer` | JSON body never read | reads JSON as well as form/query |
| `GET api/consumables/v1/getTransferable/{id}` | 404 | implemented |
| `PUT api/roomconsumables/.../{consume,purchase/tokens,purchase/currency}` | POST-only (405) | PUT added — consumables are buyable again |
| `Room consumable descriptors` | nested PriceAndCurrency; client reads flat | flat Price/PurchaseCurrencyId/ModifiedAt |
| `Room currency + purchase-offer wires` | server-invented key names | client key names, legacy aliases retained |
| `GET api/roomcurrencies/v1/getBalance` | ignored `accountId`, so always answered for the caller | honours `accountId` |
| `api/roomcurrencies createCurrency/updateCurrency` | `Limit` dropped | `Limit` accepted |
| `GET api/roomkeys/v1/mine` | returned keys CREATED, not held | returns purchased keys (plus own) |
| `GET api/roomkeys/v1/purchased/{id}` | asked "did I buy it" — always false for the creator | asks "did anyone buy it" |
| `GET api/roomkeys/v1/owns` | ignored `playerId` | answers for the named player |
| `POST api/roomkeys/v1/owns/bulk` | JSON pair array never read | reads {RoomKeyId, AccountId} pairs |
| `PUT api/roomkeys/v1/update{All,Name,Description,Price}` | 405 — none registered | all four added |
| `13 club write endpoints` | 415 — [FromBody] vs the client's form bodies | shared form-or-JSON model binder |
| `PUT club/{id}/clubhouse` | read the query only, so setting a clubhouse CLEARED it | reads the form body |
| `PUT club/{id}/members/requesttojoin` | joinability 1 and 2 swapped | Open=0, InviteOnly=1, AskToJoin=2 |
| `POST thread/{id}/leave` | DELETE-only (405) | POST added |
| `PUT announcements/club/{cid}/{aid}` | bound to the DELETE handler — editing DELETED the post | its own edit handler |
| `POST announcements/club/{cid}` | 404 — no create route | implemented, returns the new id |
| `POST api/inventions/v4/addversion` | 400 — client sends `inventionDataFilename` | accepts it plus the extra cost fields |
| `POST api/inventions/v1/settags` | 400 — tag arrays bound to string | accepts string arrays |
| `POST api/images/v1/modifyaccessibility` | `Accessibility` never read; photos stayed private | reads the enum |
| `POST leaderboard/CheckAndSetStat` | 404 — every 2023 score write was lost | implemented with compare-and-set |
| `api/externalfriendinvite/v1/get*referrers` | objects where the client reads a bare id array | bare Int32 array |
| `GET account/search` | bound `query`; client sends `name` — always empty | accepts both |
| `PUT account/me/birthday` | POST-only (405) | PUT added |
| `DELETE cachedlogin/current` | GET-only (405) | DELETE added |
| `PUT api/players/v4/current/contact` | GET-only (405) | PUT added |
| `PUT roominstance/{id}/markprivate` | POST-only (405) | PUT added |
| `PUT roominstance/{id}/roomCode` | 405, and custom codes were discarded | PUT added; codes persist |
| `GET roomserver/rooms/base, roomserver/featuredrooms/current` | 404 — no roomserver/ twin | twins added |
| `GET featuredrooms/current` | no `RoomName` key — tile captions were null | `RoomName` emitted |
| `playlists name/description/image/tags` | 415 — [FromBody] vs form bodies | form/query/JSON reader |
| `playlists accessibility/levelvoting/restrictions/warning` | PUT 405, and values were discarded | PUT added and values persisted |
| `SignalR push `Id` (notify)` | enum NAME sent for most ids; client only registered numbers — pushes vanished | numeric by default, with the six string-keyed ids listed explicitly |
| `SignalR SubscriptionUpdateRoom` | sent as `SubscriptionUpdateRoom`; client listens on `RoomUpdate` | sent as `RoomUpdate` |
| `Room ban notification` | sent id 24, for which the client has no handler | sent as ModerationKick (22) with IsBan |
| `Presence on SignalR reconnect` | disconnect pushed offline; nothing ever pushed back online, so a reconnect greyed the player out for the rest of the session | connect re-pushes presence when a room is already known |

Everything else in the per-subsystem Defects sections below is still
outstanding.

## Subsystems

### Identity, accounts, login and version gate

`identity-account`

Identity-account diff for the 2023-03-21 client vs DorkNet (all claims verified by reading both the server source and the ISIL). 14 defects across 34 checked route/verb pairs. Hard failures (404/405): DELETE /account/me, PUT /account/me/birthday (server POST-only), account/me/{emoji,personalpronouns,identityflags,bannerimage,confirmphone} missing, POST account/bulk/{email,phone} missing, GET accounts/{id}/receives/{cat} missing, GET platformid/{id} missing (nearest route is wrong path + object-vs-bare-string + hardcoded 0), DELETE /cachedlogin/current (GET-only), PUT api/players/v4/current/contact (GET-only). Silent-empty/wrong-data defects: account/search binds 'query' but client sends 'name' → search always returns []; cachedlogin/forplatformids reads 'platformIds' but client sends repeated 'id' → always []; cachedlogin/migrate binds platform/platformId vs client's legacyPlatformId/newPlatformId → no-op; account/me/createlogintoken returns an object where the client requires a bare JSON string → reader throws; GET /account/me hardcodes email/phone/birthday and omits availableUsernameChanges/displayEmoji/bannerImage/personalPronouns/identityFlags; connect/token lacks login_token and device_code grants (both fall into the device-id fallback → wrong-account or approval-bypassed logins), and account/me/remoteauth validates against the wrong token store so passwordless approval always 401s. Clean: haspassword, changepassword, recoverpassword, bulk (repeated 'id' handled), {id}/bio, privacy settings GET/PUT, sanitize v1 + isPure (request field names UNKNOWN — JsonUtility, no literals), versioncheck/v4, isactivecreator, forplatformid, deviceauthorization shape, eac/challenge, namegen, parentalcontrol, and the username/displayname/bio PUTs. account/{id}/clubs works but omits MinLevel and ClubChatEnabled (defaults 0/false — club chat renders disabled).

**Client-side notes.** SERIALIZER MECHANICS (applies to every DTO above)
- The 2023 client deserializes API responses with Utf8Json-style generated formatters (writer type LMANJAHJEKC, reader type EILLEGCDDNJ, key automaton PIPOGPCBCNM). Each formatter's .ctor registers every property under THREE exact byte keys — PascalCase, camelCase and all-lowercase (e.g. AEGCKMKICFG.txt:855/866/874 -> "AccountId"/"accountId"/"accountid"). Matching is exact-byte per variant, so a fourth spelling does NOT match: DorkNet's `"username"` is fine (registered), but a hypothetical `"Username"` would NOT be (only "UserName"/"userName"/"username" are registered). Unknown keys are skipped; missing keys leave CLR defaults, so a missing key is safe but a wrong-cased key silently yields null/0.
- Danger cases: non-nullable value types. MIMFEOHELNC.CreatedAt and MDGJJCFGJNF.LastLoginTime are `System.DateTime` (not nullable) — emitting `null` for those keys will throw in the reader, whereas omitting the key is harmless.
- Request parameters go through BNDIAONDFFF.AFGEDDANEKP(name, value), which stores List<KeyValuePair<string,string>> and only decides the encoding at send time: GET => joined with "&" and appended after "?" (BNDIAONDFFF.txt:2806-2851), POST/PUT => HTTPFormBase.AddField (urlencoded/multipart) (BNDIAONDFFF.txt:3032/3099). Servers should accept both query and form for the POST/PUT routes.
- Verb enum is BestHTTP.HTTPMethods: 0=Get, 1=Head, 2=Post, 3=Put, 4=Delete, 5=Patch (validated against call sites: account/me DELETE=4, changepassword POST=2, bio/username PUT=3).
- Host is the SECOND ctor arg (GJDLNNLKDIJ) of BNDIAONDFFF..ctor(HTTPMethods, GJDLNNLKDIJ, String) — BNDIAONDFFF.txt:74. Observed ordinals in this subsystem: 0 for connect/*, cachedlogin/*, eac/challenge, account/me/{haspassword,changepassword,createlogintoken,remoteauth}, account/recoverpassword, platformid/{0} (auth service); 1 for api/* and the CBKANFIOBCF-based clients (api service, hard-coded at CBKANFIOBCF.txt:5174); 10 for account/*, accountprivacysettings/*, parentalcontrol/me, namegen/options (accounts service); 13 for account/{id}/clubs; 15 for accounts/{id}/receives/{category}. The ordinal->URL table is built at runtime from config (PAPLNIPKAMG.OICOPGCHJAG, Dictionary<GJDLNNLKDIJ,Uri> at PAPLNIPKAMG.txt:2044), so the literal hostnames are NOT in the binary; the family grouping is the evidence. DorkNet serves these routes host-agnostically today, so this only matters for hosts-file/DNS coverage.

DTO NOTE
- MIMFEOHELNC has 13 properties but only 12 serialized keys (AEGCKMKICFG.txt "Compare rax, 11" at :2665 confirms indices 0..11). The unserialized one is the extra ObscuredString at MIMFEOHELNC.txt:195 (HDLPBPJEJNC) — which property name it carries is UNKNOWN. Same pattern in MDGJJCFGJNF (6 keys, 7 properties; the trailing MIMFEOHELNC Account field is client-only).

GAPS vs the current DorkNet server (compared against the server's reflected route table)
1. `DELETE /cachedlogin/current` — client sends DELETE (LBNJFPOLCDL.txt:4190); server registers only GET (DorkNet.Server\Controllers\Auth\AuthController.cs:328) => 405 on sign-out.
2. `DELETE /account/me` — client sends DELETE (OPMAPIOEIFG.txt:8773); server only has GET (Controllers\Accounts\AccountsController.cs:54).
3. `GET /platformid/{accountId}` — not registered at all; the nearest server route is `/account/v1/{accountId}/platformid` and it returns an OBJECT `{ PlatformId = 0L }` (AccountsController.cs:684) while the client expects a BARE JSON STRING.
4. `GET /accounts/{accountId}/receives/{category}` — not registered anywhere => 404, the promise in AccountSpecificUIBehaviour rejects.
5. `POST /account/bulk/email` and `POST /account/bulk/phone` — not registered (server only has GET/POST /account/bulk).
6. `POST /cachedlogin/forplatformids` — field-name mismatch: client sends repeated `id=` (OPMAPIOEIFG.txt:11383), server reads `platformIds`/`PlatformIds` (AuthController.cs:289-311) => always returns [].
7. `POST /cachedlogin/migrate` — field-name mismatch: client sends `legacyPlatformId` + `newPlatformId` (LBNJFPOLCDL.txt:4469/:4479), server binds `[FromForm] platform, platformId` (AuthController.cs:342).
8. `account/me/` suffixes missing on the server: emoji, personalpronouns, identityflags, bannerimage, confirmphone (server has username/displayname/bio/birthday/email/phone/profileimage only) => "Failed to modify <x>".
9. account/me payload: DorkNet's RecNetSelfAccount (DorkNet.Models\Auth\RecNetResult.cs:48-115) emits accountId/username/displayName/profileImage/isJunior/hasBirthday/platforms/rawUsername/email/phone/birthday/juniorState/parentAccountId. Keys that exist for this client but are never emitted: availableUsernameChanges (Int32 — 0 means "no name changes left" in the UI), displayEmoji, bannerImage, treatAsJunior, personalPronouns, identityFlags, createdAt. `platforms` and `rawUsername` are ignored by the 2023 reader.
10. cachedlogin payloads: server's CachedLogin (DorkNet.Models\Auth\CachedLogin.cs) matches 5 of 6 keys; `refreshToken` is never emitted (harmless — String stays null).

FLAGGED NON-ROUTE / PREFIX LITERALS
- "account/me/" and "account/bulk/" are route PREFIXES, concatenated with a suffix before use (OPMAPIOEIFG.txt:8579 and :12121). They are real routes only in their concatenated forms, enumerated in the entries above.
- "account/{0}/bio" appears twice; the occurrence at OPMAPIOEIFG.txt:6239 is NOT an HTTP request — it builds a response-cache key passed to LBIIOPNIDAC.PBGNNIJLBDG (cache invalidation after PUT account/me/bio). The other occurrence (:10822) is a genuine GET, so the literal is still marked isRealHttpRoute=true.
- accounts/{0}/receives/{1}: the {1} segment is an enum boxed and String.Format'ed, i.e. the enum MEMBER NAME. The constant is 8 (LELAJKMOMIA.txt:693) but the CCOKJMJOIJF member names are not present as literals anywhere in the ISIL — the server should treat that segment as an opaque string.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| GET (0) when id count <= 100, POST (2) above that — chosen at runtime by ALHIJCJOLCB.JIECAFGCODK(count, threshold=100) | `account/bulk` | repeated "id" (Int32) — query params on GET, urlencoded form fields on POST | JSON array of MIMFEOHELNC (same shape as account/search) |
| POST (2) | `account/bulk/` | path is "account/bulk/" + segment, giving the two concrete routes account/bulk/email and account/bulk/phone. Body: repeated field named after the same word ("email" or "phone"), ea | JSON array of JFFGMHMHMCO = MIMFEOHELNC fields plus a leading ContactDetail(String): keys ContactDetail, AccountId(Int32), UserName, DisplayName, DisplayEmoji, ProfileImage, BannerImage, TreatAsJunior(Boolean), HasBirthd |
| GET (0) | `account/isactivecreator/me` | none | bare JSON boolean — Task<System.Boolean> |
| GET (HTTPMethods 0) — also DELETE (4) from a second call site | `account/me` | none (GET). DELETE variant also sends no body. | CAALNOGGDLG ("SelfAccount") object. Reader keys registered in formatter ctor, index order: Email(String), Phone(String), Birthday(Nullable<DateTime>), JuniorState(enum EJFPLBEEAMN as Int32), ParentAccountId(Nullable<Int3 |
| prefix only — concatenated with a suffix; PUT (3) for username/displayname/emoji/bio/personalpronouns/identityflags/bannerimage/birthday, POST (2) for email/phone/confirmphone | `account/me/` | Single form field (POST/PUT bodies are urlencoded/multipart; the field name equals the suffix except bio): "username", "displayname", "emoji", "bio", "personalpronouns", "identityf | body ignored — helper returns LDGADANDBIO (fire-and-forget promise); only HTTP status matters. Failure string "Failed to modify <suffix>". |
| POST (2) | `account/me/changepassword` | form fields: "oldPassword" (String), "newPassword" (String) | body ignored (LDGADANDBIO promise); status only |
| POST (2) | `account/me/createlogintoken` | none (no fields on either call site) | bare JSON string (the login token) — FGLDKEJLAKB<System.String> |
| GET (0) | `account/me/haspassword` | none | bare JSON boolean (true/false) — Task<Boolean> |
| POST (2) | `account/me/remoteauth` | form field "code" (String); request timeout overridden via BNDIAONDFFF.EHPIOGADENJ | body ignored (LDGADANDBIO); failure string "Failed to authorize login" |
| POST (2) | `account/recoverpassword` | form field "email" (String) | body ignored (LDGADANDBIO); failure string "ResetPassword failed" |
| GET (0) | `account/search` | query "name" (String) | JSON array of MIMFEOHELNC: [{AccountId(Int32), UserName(String), DisplayName(String), DisplayEmoji(String), ProfileImage(String), BannerImage(String), TreatAsJunior(Boolean), HasBirthday(Boolean), PersonalPronouns(Int32  |
| GET (0) | `account/{0}/bio` | {0} = accountId (Int32) | CCNKBIGMLGP object: {"AccountId": Int32, "Bio": String} (also accepted: accountId/accountid, bio) |
| GET (0) | `account/{0}/clubs` | {0} = accountId (Int32) | JSON array of FOIJDINBPFG: ClubId(Int64), Name(String), Description(String), MainImageName(String), State(enum IACDINKNHKB as Int32), CreatorAccountId(Int32), Category(String), Visibility(enum GJLKOMJKCFL as Int32), Join |
| PUT (3) | `accountprivacysettings/recenthistoryvisibility` | form field "isRecentHistoryVisible" (Boolean) | body ignored (LDGADANDBIO); failure string "Failed to modify accountprivacysettings/recenthistoryvisibility" |
| GET (0) | `accountprivacysettings/{0}` | {0} = accountId (Int32) | EBPCGLAICIH object: {"AccountId": Int32, "IsRecentHistoryVisible": Boolean} (Pascal/camel/all-lower all accepted). A second client method reads the same route and projects it to a bare Boolean. |
| GET (0) | `accounts/{0}/receives/{1}` | {0} = accountId (Int32). {1} = a CCOKJMJOIJF enum value boxed then String.Format'ed, i.e. the enum MEMBER NAME, not the number; the constant passed at this call site is value 8. Th | bare JSON boolean (Action<Boolean> continuation) |
| PUT (3) | `api/players/v4/current/contact` | query "email" (String) + raw JSON body of OPMAPIOEIFG/BIIIACIMPBM: {MarketingEmails: Boolean, InviteEmails: Boolean, GameClientTextInvites: Boolean, FriendRequestEmails: Int32-enum | body ignored (KDOPJCNKOOK = send-and-discard); failure string "Failed to update player marketing email prefs" |
| POST (2) | `api/sanitize/v1` | raw JSON body produced by UnityEngine.JsonUtility.ToJson(HPKNELJLNOJ/PurifyStringRequest). JsonUtility serializes FIELD names; those field names are not present as literals in the  | bare JSON string — the purified text (continuation is System.Action`1<String>, wrapped client-side into HPKNELJLNOJ/KKMLDOEOADJ(Boolean, String)) |
| POST (2) | `api/sanitize/v1/isPure` | raw JSON body from JsonUtility.ToJson(HPKNELJLNOJ/IsStringPureRequest) — four fields set at :481-489 (two reference fields + two int/bool fields). Field names UNKNOWN (JsonUtility  | PJLCFFHLNAE object: {"IsPure": Boolean} (also "isPure"/"ispure") |
| GET (0) | `api/versioncheck/v4` | query params: "v" (client version, from PAPLNIPKAMG version field), "p" (platform, Int32), "pid" (Int32) | HABAPHJMBEO/BFEAELMKAKM object: {"VersionStatus": Int32 (enum PHOGEIPMHOK), "UpdateNotificationStage": Int32 (enum MEHPMLBHDOJ)} — also versionStatus/versionstatus, updateNotificationStage/updatenotificationstage |
| DELETE (4) | `cachedlogin/current` | none | response body deserialized through the generic FDKKOPAPDGF path but discarded by the caller (method returns LDGADANDBIO, a value-less promise) — treat as "status only" |
| GET (0) | `cachedlogin/forplatformid/{0}/{1}` | {0} = platform as Int32 (enum HHJIBNMLOAC converted to int before boxing), {1} = platformId (String) | JSON array of LBNJFPOLCDL/MDGJJCFGJNF: {Platform: Int32 (enum HHJIBNMLOAC), PlatformId: String, AccountId: Int32, LastLoginTime: DateTime (non-nullable), RequirePassword: Boolean, RefreshToken: String} — each key also in |
| POST (2) | `cachedlogin/forplatformids` | repeated form field "id" (String) — one entry per platform id | JSON array of LBNJFPOLCDL/MDGJJCFGJNF (same shape as cachedlogin/forplatformid); mapped client-side into List<BLOJAFNJPJI> |
| POST (2) | `cachedlogin/migrate` | form fields: "legacyPlatformId" (String), "newPlatformId" (String) | body ignored (KDOPJCNKOOK); status only |
| POST (2, urlencoded form via PAPLNIPKAMG.NKGALKBJBJG) | `connect/deviceauthorization` | form: client_id="recroom", client_secret="VxZ53kgbbEaRoZAeMe00MagtgD12GLL2" | LBNJFPOLCDL/BENCODMLKEA object, write keys snake_case: {"device_code": String, "user_code": String, "verification_uri": String, "verification_uri_complete": String, "expires_in": Int32, "interval": Int32}. Reader also ac |
| POST (2, urlencoded form via PAPLNIPKAMG.NKGALKBJBJG) | `connect/token` | OAuth-ish form. Always added by LBNJFPOLCDL.CONAGEGDJAJ: client_id="recroom", client_secret="VxZ53kgbbEaRoZAeMe00MagtgD12GLL2", platform, platform_id, device_id, device_class, time | LBNJFPOLCDL/HBEJOJNIMBD object, write keys: {"Error": String, "error_description": String, "access_token": String, "refresh_token": String, "Key": String}; reader also accepts error/Error_description/Access_token/Refresh |
| GET (0) — issued through PAPLNIPKAMG.LDBMKNCNKGJ which passes verb 0 | `eac/challenge` | none | bare JSON string — the base64 EAC challenge (FGLDKEJLAKB<String>). Must be valid base64: the client feeds it to EACManager challenge-response generation. |
| GET (0) | `namegen/options` | none | OPMAPIOEIFG/EAHBKJEMHEM object: {"Nouns": List<String>, "Adjectives": List<String>} — also nouns/adjectives (2 casings only, camel==lower) |
| GET (0) | `parentalcontrol/me` | none | MFBCJHHEMKF object: {"AccountId": Int32, "DisallowInAppPurchases": Boolean} (also accountId/accountid, disallowInAppPurchases/disallowinapppurchases). One caller projects the same response down to a bare Boolean. |
| GET (0) | `platformid/{0}` | {0} = accountId (Int32); the client short-circuits and never sends the request when accountId <= 0 | bare JSON string — FGLDKEJLAKB<System.String> (the platform id). NOT an object. |

#### Defects

##### `GET account/me` — SHAPE_MISMATCH (degraded)

Handler exists and returns RecNetSelfAccount (DorkNet.Models\Auth\RecNetResult.cs:98). Global serializer PropertyNamingPolicy=null (Startup\ServiceCollectionExtensions.cs:380-381) with JsonPropertyName attrs, so it emits accountId, username, displayName, profileImage, isJunior, platforms, rawUsername, hasBirthday, createdAt, email, phone, birthday, juniorState, parentAccountId. All of those hit registered 2023 reader keys (accountId/username/displayName/profileImage/hasBirthday/createdAt/email/phone/birthday/juniorState/parentAccountId; 'isJunior' lands on the client's Nullable<bool> IsJunior). Keys the 2023 CAALNOGGDLG reader registers but the server NEVER emits: AvailableUsernameChanges (Int32 -> defaults 0 = UI shows no username changes remaining), DisplayEmoji, BannerImage, PersonalPronouns, IdentityFlags, TreatAsJunior (server's TreatAsJunior property serializes under the key 'isJunior', so the client's TreatAsJunior bool stays false — benign). Also the values are hardcoded: Email/Phone always string.Empty, Birthday always 2000-01-01, JuniorState always 0 — the real PlayerEntity columns (Email, Phone, Birthday set by SetEmail/SetPhone/SetBirthday in this same controller) are not read back, so the settings screen never shows what the player saved.

Handler: `DorkNet.Server\Controllers\Accounts\AccountsController.cs:54`

**Fix.** In AccountsController.GetMe, build the payload from the actual PlayerEntity (Email, Phone, Birthday, IsJunior) and add availableUsernameChanges (e.g. a large int), displayEmoji, bannerImage, personalPronouns (0), identityFlags (0) keys — extend RecNetSelfAccount or return an augmented DTO for the 2023 path.

##### `DELETE account/me` — VERB_MISMATCH (degraded)

Client's in-game account-deletion action sends DELETE /account/me (OPMAPIOEIFG.txt:8771-8773, verb 4). Server registers only [HttpGet] on /account/me (plus v1/v2 GET aliases) — DELETE returns 405 and the client logs 'Failed to delete local account'.

Handler: `DorkNet.Server\Controllers\Accounts\AccountsController.cs:54`

**Fix.** Add a [HttpDelete("/account/me")] action in AccountsController that (soft-)deletes/anonymizes the caller's PlayerEntity and returns 200; body is ignored by the client.

##### `PUT account/me/birthday` — VERB_MISMATCH (degraded)

The 2023 client sends PUT (verb 3 — verified at OPMAPIOEIFG.txt:7950-7954: suffix 'birthday', rcx=3) but the server registers only [HttpPost("/account/v1/birthday")] and [HttpPost("/account/me/birthday")] → 405, client shows 'Failed to modify birthday'. This breaks the junior/age-gate birthday prompt.

Handler: `DorkNet.Server\Controllers\Accounts\AccountsController.cs:341`

**Fix.** Add [HttpPut("/account/me/birthday")] to the existing SetBirthday action in AccountsController.

##### `PUT (emoji/personalpronouns/identityflags/bannerimage), POST (confirmphone) account/me/emoji, account/me/personalpronouns, account/me/identityflags, account/me/bannerimage, account/me/confirmphone` — MISSING (degraded)

None of these five concrete routes exist. server-routes.txt has no account/me/emoji|personalpronouns|identityflags|bannerimage|confirmphone; confirmphone exists only as POST /account/v1/confirmphone (AccountsController.cs:332) and api/account/v1/confirmphone — the 2023 client concatenates 'account/me/' + suffix (OPMAPIOEIFG.txt:8579), so it 404s. Result: profile-emoji / pronoun / identity-flag / banner-image edits and phone confirmation all fail with 'Failed to modify <suffix>'.

**Fix.** Add handlers in AccountsController: [HttpPut] /account/me/emoji (form 'emoji'), /account/me/personalpronouns (form 'personalpronouns', int), /account/me/identityflags (form 'identityflags', int), /account/me/bannerimage (form 'bannerimage'); [HttpPost] /account/me/confirmphone (route the existing ConfirmPhone action). Persist emoji/bannerimage/pronouns/flags on PlayerEntity so GET /account/me can echo them back (required for the account/me shape fix above).

##### `POST account/me/createlogintoken` — SHAPE_MISMATCH (degraded)

Client deserializes the response as a BARE JSON string (FGLDKEJLAKB<System.String>, LBNJFPOLCDL.txt:8382) — server returns an OBJECT { Success, Error, Token, LoginToken, ExpiresAt } (AccountsController.cs:575-582). The strict reader hits '{' where it expects '"' and throws → 'Failed to CreateLoginToken', so every 'open on the web' link (BHKIHEEBBNG builds auth.rec.net URL + token) fails.

Handler: `DorkNet.Server\Controllers\Accounts\AccountsController.cs:565`

**Fix.** Return the token as a bare JSON string: Content(JsonSerializer.Serialize(token), "application/json"). Keep the token-minting/PlayerSettings persistence as-is.

##### `GET account/search` — SHAPE_MISMATCH (degraded)

REQUEST param name mismatch: the 2023 client sends the search text as query key 'name' (verified in ISIL: OPMAPIOEIFG.txt:12493 'Move rdx, "name"' into AFGEDDANEKP), but the server binds [FromQuery] string? query — 'name' is never read, query stays null, and line 180 short-circuits to an empty array. Watch player search ALWAYS returns zero results for this client. Response shape itself is fine (List of RecNetAccount, camelCase keys all registered by MIMFEOHELNC's reader; DisplayEmoji/BannerImage/PersonalPronouns/IdentityFlags omitted → CLR defaults, cosmetic).

Handler: `DorkNet.Server\Controllers\Accounts\AccountsController.cs:175`

**Fix.** In Search, also accept the 'name' query key: e.g. add [FromQuery] string? name and use query ?? name (mirror the existing multi-alias pattern used elsewhere in the controller).

##### `POST account/bulk/email + account/bulk/phone` — MISSING (degraded)

The 2023 friend-finder contact import POSTs account/bulk/email and account/bulk/phone with repeated 'email'/'phone' form fields (OPMAPIOEIFG.txt:12121 concat, :12143 body key). No such routes exist — server only has /account/bulk (exact) and /account/v1/emails|phones — so both 404 and the contact-import flow dies. Response must be a JSON array of JFFGMHMHMCO = MIMFEOHELNC fields plus a leading ContactDetail(String) per matched account.

**Fix.** Add [HttpPost("/account/bulk/email")] and [HttpPost("/account/bulk/phone")] in AccountsController: read the repeated 'email'/'phone' form fields, match against Players.Email/Phone (logic already exists in Bulk()), and return objects of shape { ContactDetail = <matched email/phone>, ...BuildAccount fields }.

##### `GET accounts/{0}/receives/{1}` — MISSING (degraded)

No route with an 'accounts/' + 'receives' segment exists anywhere in DorkNet.Server (grep over Controllers confirms; no catch-all remains — GlobalCatchAllController was deleted). Client GETs accounts/{accountId}/receives/{enumMemberName} expecting a bare JSON boolean; the 404 rejects the promise in AGUI AccountSpecificUIBehaviour and blocks the notification-gated UI action.

**Fix.** Add GET /accounts/{accountId:long}/receives/{category} (category as an opaque {category} string segment — the enum member name is not recoverable, do NOT constrain it) returning Content("true","application/json"), in PlatformNotificationsController or AccountsController.

##### `DELETE cachedlogin/current` — VERB_MISMATCH (degraded)

Client sign-out / 'forget this account' sends DELETE /cachedlogin/current (LBNJFPOLCDL.txt:4190, verb 4); server registers only [HttpGet] → 405 and the forget-account promise rejects.

Handler: `DorkNet.Server\Controllers\Auth\AuthController.cs:328`

**Fix.** Add [HttpDelete("/cachedlogin/current")] — resolve the bearer's player, clear its LastPlatform/LastPlatformId (so GetCachedLoginsAsync stops returning it), return 200.

##### `POST cachedlogin/forplatformids` — SHAPE_MISMATCH (degraded)

REQUEST field mismatch: the 2023 client sends the platform ids as repeated form field 'id' (OPMAPIOEIFG.txt:11383) with NO 'platform' field; the server reads only Form/Query 'platformIds'/'PlatformIds' (AuthController.cs:300-311) → rawIds empty → always returns [] and Steam-friend → Rec Room account resolution silently yields nothing ('Failed to GetAccountsFromPlatformIds' path never even errors — it just gets an empty list).

Handler: `DorkNet.Server\Controllers\Auth\AuthController.cs:289`

**Fix.** Also read Form["id"]/Form["Id"] (ASP.NET comma-joins repeated fields; split on ',') in CachedLoginsForPlatformIds, defaulting platform to 0/any when absent.

##### `POST connect/token` — SHAPE_MISMATCH (degraded)

Route/verb/response OK: urlencoded POST accepted; BuildTokenResponse (AuthController.cs:36-62) emits access_token, refresh_token, Key/key — all registered by the HBEJOJNIMBD reader; error/error_description emitted on failures. Grant-type coverage is the defect: only cached_login, refresh_token and password(+device fallback) are implemented. (a) grant_type=login_token (+account_id, login_token from createlogintoken) falls through to the device-id fallback and issues a token for whatever account the deviceId maps to, IGNORING account_id and never validating the login token — wrong-account sign-in. (b) grant_type=urn:ietf:params:oauth:grant-type:device_code falls through the same way — the polling device gets a device-bound token immediately, bypassing the remote-approval flow entirely (and 'authorization_pending' retry semantics are never exercised). (c) grant_type=create_account also lands in the fallback, which happens to create-or-fetch by deviceId — acceptable behaviorally.

Handler: `DorkNet.Server\Controllers\Auth\AuthController.cs:92`

**Fix.** In AuthController.Token add explicit branches: login_token → validate against the remote_login_token PlayerSettings row for the supplied account_id and issue that account's pair; device_code → look up the deviceauthorization:{device_code} row, return {error:"authorization_pending"} (HTTP 400) until RemoteAuth marks it approved, then issue the approving account's pair.

##### `GET platformid/{0}` — MISSING (degraded)

GET /platformid/{accountId} is not registered anywhere (server-routes.txt has no bare 'platformid/' entry; no catch-all remains) → 404, and platform-native profile/invite actions from BaseAccountModel reject. The nearest route, GET /account/v1/{accountId:long}/platformid (AccountsController.cs:684-685), is BOTH the wrong path AND the wrong shape — it returns the object { PlatformId = 0L } where the client expects a BARE JSON string, and its hardcoded 0 marks it a stub besides.

**Fix.** Add [HttpGet("/platformid/{accountId:long}")] returning Content(JsonSerializer.Serialize(player.LastPlatformId ?? ""), "application/json") — a bare JSON string of the real stored platform id. Fix the /account/v1/.../platformid stub to return the real value too.

##### `GET account/{0}/clubs` — SHAPE_MISMATCH (cosmetic)

GET /account/{playerId:long}/clubs exists with a real ClubsForPlayerAsync query. ToWireClub (ClubsController.cs:937-953) emits PascalCase ClubId, Name, Description, MainImageName, State, CreatorAccountId, Category, Visibility, Joinability, AllowJuniors, MemberCount, IsRRO, ClubhouseRoomId, ClubType — all registered by FOIJDINBPFG's reader. Two registered keys are never emitted: MinLevel (Int32 → defaults 0, harmless) and ClubChatEnabled (Boolean → defaults false, so the profile Clubs tab renders every club with chat disabled).

Handler: `DorkNet.Server\Controllers\Clubs\ClubsController.cs:318`

**Fix.** Add MinLevel = 0 (or the entity value) and ClubChatEnabled = true/entity value to ToWireClub in ClubsController.cs.

##### `PUT api/players/v4/current/contact` — VERB_MISMATCH (cosmetic)

Server registers only [HttpGet] on /api/players/v4/current/contact; the 2023 client PUTs it (query 'email' + JSON prefs body) right after POST account/me/email → 405. The client discards the body (fire-and-forget) so the only visible symptom is the 'Failed to update player marketing email prefs' log line and the prefs never persisting.

Handler: `DorkNet.Server\Controllers\API\Players\V2\PlayersController.cs:24`

**Fix.** Add [HttpPut("/api/players/v4/current/contact")] (can share the action or a new no-op that returns 200; optionally persist the email query param / pref booleans).

##### `POST cachedlogin/migrate` — SHAPE_MISMATCH (cosmetic)

REQUEST field mismatch: client sends 'legacyPlatformId' + 'newPlatformId' (LBNJFPOLCDL.txt:4469/:4479); server binds [FromForm] platform, platformId → binds 0/null and TagPlatformAsync writes nothing meaningful. No migration happens. Cosmetic for the PC preservation target since the caller is IOSPlatformManager (mobile first-launch id change), but the handler is effectively a silent no-op for this client.

Handler: `DorkNet.Server\Controllers\Auth\AuthController.cs:342`

**Fix.** Bind [FromForm(Name="legacyPlatformId")] and [FromForm(Name="newPlatformId")]; re-tag rows whose LastPlatformId == legacyPlatformId to newPlatformId.

### Matchmaking, sessions and presence

`matchmaking-session`

All 20 client routes have real, verb-matching handlers on DorkNet (GoToController / MatchController / MatchPlayerController) and every matchmake/* response uses the camelCase MatchmakingResponseDto/RoomInstanceDto shape (GoToController.cs:1529-1634) that carries every key the 2023 client reads, plus the heartbeat PresenceDto with isOnline and appVersion "20230317" (MatchPlayerController.cs:485-511). No missing endpoints and no response-shape defects that break gameplay. Verified defects: (1) player/notifydisconnect binds 2020's otherPlayerId instead of 2023's PlayerId/RoomInstanceId form keys (log-only, cosmetic); (2) player/photonregionpings parses 2020's region=/ping= pair arrays, so 2023's region-name-keyed fields are silently dropped and pings are never recorded (degraded); (3) the three preference writers (statusvisibility, vrmovementmode, avoidjuniors) are shape-correct but discard the value — avoidjuniors GET is hardcoded false so the toggle resets every session (degraded stubs); (4) MaxPersistenceVersion, sent on every matchmake/* request, is bound nowhere in the repo (0 grep hits) — the server relies on the global admin clamp toggle instead of the client-declared cap; (5) ClientJoinData/AdditionalPlayersAutoFollow on sub-room hops and SubRoomId on code joins are ignored (spawn-target fidelity loss); (6) unknown-room paths at GoToController.cs:288/:324 emit "roomInstance": null (no null-ignore in serializer, ServiceCollectionExtensions.cs:380) while the sibling club/code handlers send an empty object — the 2020 deserializer the file's own header documents throws on non-object roomInstance; 2023's reflective reader behavior is UNKNOWN, cheap to make consistent.

**Client-side notes.** TRANSPORT/HOST: Every route in this subsystem is issued against the matchmaking host: BNDIAONDFFF..ctor(BestHTTP.HTTPMethods verb, GJDLNNLKDIJ host, string route) with host enum value 3 at every call site (BNDIAONDFFF.txt:74 for the signature; host base URLs come from the nameserver response — PAPLNIPKAMG has a NestedType_NameServerResponse). Verb enum is BestHTTP.HTTPMethods: 0=GET, 2=POST, 3=PUT — decoded from the rdx immediate at each ctor call. Params added via BNDIAONDFFF.AFGEDDANEKP are sent as an application/x-www-form-urlencoded (or multipart when binary present) BODY on POST/PUT via HTTPFormBase.AddField (BNDIAONDFFF.txt:3032,3099), and as a ?k=v&k=v QUERY string only on body-less verbs (the \"?\"/\"&\" concat path, BNDIAONDFFF.txt:3444-3489). DorkNet's [FromForm] bindings therefore match.
CENTRAL SENDER: All matchmake/* routes except matchmake/none funnel through Matchmaking.HHJFFJEHLOB (Matchmaking.txt:11330, original name GoToHelper): POST, always adds BypassMovementModeRestriction=bool, LoginLock=string, MaxPersistenceVersion=Int32 (from GJGGDBDBCMJ — clamp-relevant, see room-version-clamp work), repeated AdditionalPlayerIds=Int32; a per-route Action<BNDIAONDFFF> closure adds JoinMode / ClientJoinData / AdditionalPlayersAutoFollow / SubRoomId. Response: JSON -> Matchmaking/PCFNLCMMGKB {errorCode:HMIBNIEBEKK enum, roomInstance:HCHDEHIGEBE} -> mapped by <>c.<GoToHelper>b__114_2 to the HMIBNIEBEKK error value the UI consumes ('Matchmaking request failed ({0}): {1}' on failure). The exact JSON key casing of PCFNLCMMGKB/HCHDEHIGEBE is reflection/attribute-driven and NOT recoverable from ISIL; the camelCase errorCode/roomInstance shape served by DorkNet (GoToController.cs:1529-1620) is verified working live against this exact build (matchmake/dorm boot, room joins, sub-room hops all functioning on the march-2023 branch), so treat it as the wire truth.
2023-vs-2020 MISMATCHES FOUND (both benign, responses ignored, but fix for fidelity): (1) player/notifydisconnect — 2023 sends PlayerId + RoomInstanceId form keys; server binds 2020's otherPlayerId so the logged id is always null (MatchPlayerController.cs:117). (2) player/photonregionpings — 2023 sends one form field per region keyed by REGION NAME (us=42&eu=90...), not the region=/ping= pair arrays the server parses (MatchPlayerController.cs:239+); 2023 pings are silently dropped. Also note verb: 2023 uses PUT here (server already accepts GET/POST/PUT).
HEARTBEAT: response IS parsed (BDDJOGCKIJO); serve the current roomInstance (never null once in a room) and appVersion as the STRING \"20230317\" or the client reports presence out-of-sync / version mismatch. avoidjuniors GET must return a bare JSON boolean primitive.
COVERAGE: all 20 routes in groups.json are real HTTP routes, all are already registered on the DorkNet server (server-routes.txt:812-847); no missing endpoints, only the two request-binding mismatches above.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `matchmake/chatinvite/{0}/{1}` | route args {0},{1}=the two Int64 params (both boxed as Int64 for String.Format); the Int32 third param was not observed being sent on the wire (purpose UNKNOWN); form body: standar | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}} |
| POST | `matchmake/club/{0}` | route arg {0}=Int64 clubId (unboxed from ObscuredLong on the club DTO); form body: standard sender params only (no per-route closure) | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}}; server additionally sets a clubId on the roomInstance for the club UI |
| POST | `matchmake/code/{0}/{1}` | route args {0}=Int64 roomId, {1}=string code (custom matchmaking/room code); form body: SubRoomId=Nullable<Int64> (omitted when null) + standard sender params | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}}; server uses errorCode 40 for a bad/unknown code |
| POST | `matchmake/dorm` | form-urlencoded body (POST params go through HTTPFormBase.AddField): BypassMovementModeRestriction=bool, LoginLock=string (session GUID), MaxPersistenceVersion=Int32, optional repe | {"errorCode": Int32 (0=OK, 4=RoomDoesNotExist, 26=RoomInstanceIsPrivate...), "roomInstance": {"roomInstanceId":Int64, "roomId":Int64, "subRoomId":Int64, "location":string GUID, "photonRegionId":string, "photonRoomId":str |
| POST | `matchmake/event/{0}` | route arg {0}=event id from the HPIOAGDJHDH event DTO; form body: JoinMode=Int32 + standard sender params | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}} |
| POST | `matchmake/instance/{0}` | route arg {0}=Int64 roomInstanceId; form body: standard sender params only | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}} for that specific instance |
| POST | `matchmake/invite/{0}` | route arg {0}=first Int64 param (invite id); the second Int64 (roomId) is NOT in the route and no extra form key was observed — form body: standard sender params only | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}} pointing at the inviter's instance |
| POST | `matchmake/none` | form body: LoginLock=string only | Body IGNORED — client sends via BNDIAONDFFF.FPCPAJAAHME (raw Task<AAPIOIOAJKM>) and only error-status matters. 200 with any/empty body is fine (DorkNet returns a full MatchmakingResponseDto, which is harmless). |
| POST | `matchmake/player/{0}` | route arg {0}=Int32 accountId (player account ids are Int32 in this build); form body: standard sender params only | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}}; must mirror the target's live roomInstanceId/photonRoomId for the follow-join to land in the same Photon shard; errorCode 26 (RoomInstanceIsPrivate) when |
| POST | `matchmake/room/{0}` | route arg {0}=Int64 roomId; form body: JoinMode=Int32 (0 join shared instance / 1 new public / 2 new private) + standard sender params BypassMovementModeRestriction=bool, LoginLock | same MatchmakingResponse JSON as matchmake/dorm: {"errorCode":Int32, "roomInstance":{...camelCase RoomInstance...}} |
| POST | `matchmake/room/{0}/{1}` | route args {0}=Int64 roomId, {1}=Int64 subRoomId; form body: JoinMode=Int32, ClientJoinData=string (UnityEngine.JsonUtility.ToJson of RecNet.ClientJoinData — carries the optional s | MatchmakingResponse JSON {"errorCode":Int32,"roomInstance":{...}} — server must MUTATE roomInstanceId/photonRoomId/subRoomId/location/dataBlob for the target sub-room or the client's transition state machine never fires  |
| GET | `player/avoidjuniors` | GET: none. PUT (same route, verb enum 3): form body avoidJuniors=bool (boxed Boolean, camelCase key verbatim). | GET: bare JSON primitive boolean (true/false) — typed send BNDIAONDFFF<Boolean>.FDKKOPAPDGF; returning [] or an object breaks it ('Failed to get avoid juniors status'). PUT: body ignored (KDOPJCNKOOK). |
| POST | `player/exclusivelogin` | form body: LoginLock=string, TakeOverExclusiveSession=bool | Body unused; status-driven: 409 => already-logged-in-elsewhere path ('This account is already logged in somewhere else!'), 400 => 'Unable to login (code: 4/5)'. 200 = success. |
| POST | `player/heartbeat` | form body: LoginLock=string (both variants) | Presence JSON deserialized into BDDJOGCKIJO and compared with local state. DorkNet serves (verified live with this build): {"playerId":Int32, "statusVisibility":Int32, "deviceClass":Int32, "vrMovementMode":Int32, "roomIn |
| POST | `player/login` | form body: LoginLock=string (client-generated session GUID, static Matchmaking field) | Body unused. Client branches purely on HTTP status: 409 => 'This account is already logged in somewhere else!', 400 => generic 'Unable to login'. Return 200 with empty/any body for success. |
| POST | `player/logout` | Built directly: base match-host URL + 'player/logout', BestHTTP verb enum 2 (POST), Dictionary<string,string> form field LoginLock=string | Body ignored; fire-and-forget teardown. 200 empty is fine. |
| POST | `player/notifydisconnect` | form body: PlayerId=Int32 (the remote player who vanished), RoomInstanceId=Int64 (current room instance, read from HCHDEHIGEBE field +16). NOTE: keys are PascalCase 'PlayerId'/'Roo | Body ignored (plain Task via BNDIAONDFFF.KDOPJCNKOOK); 200 empty is fine. Error log 'Failed to request remote player disconnect'. |
| PUT | `player/photonregionpings` | form body: one field per measured region, key = region name string (e.g. us, eu, asia — the dictionary KEY is passed directly as the field name), value = Int32 ping ms. NOTE: this  | Body ignored (KDOPJCNKOOK); 200 empty fine. Error log 'Failed to upload photon region pings'. |
| PUT | `player/statusvisibility` | form body: statusVisibility=Int32 (enum boxed as Int32; camelCase key verbatim) | Body ignored (KDOPJCNKOOK plain Task); 200 empty fine. Error log 'Failed to set player status visibility'. |
| PUT | `player/vrmovementmode` | form body: vrMovementMode=Int32 (enum boxed as Int32; camelCase key verbatim) | Body ignored; 200 empty fine. Error log 'Failed to set VR movement mode'. |

#### Defects

##### `PUT player/photonregionpings` — SHAPE_MISMATCH (degraded)

Verb is fine (POST/PUT/GET all mounted, lines 239-241), but the parser reads 2020's repeated pair shape Request.Form["region"] / Request.Form["ping"] (lines 248-253). The 2023 client sends ONE form field per measured region, keyed by the region NAME with the ping as the value (us=42&eu=90...), so both arrays come back empty, pairs.Count stays 0, and presence.SetPhotonRegionPings is never called. Consequence: 2023 clients' region pings are silently dropped — the matchmaker's lowest-ping region bias never applies to them and instance creation always falls back to the configured default region. Client ignores the response, so nothing breaks visibly.

Handler: `DorkNet.Server/Controllers/Match/MatchPlayerController.cs:239`

**Fix.** In PhotonRegionPings, after the legacy pair parse, fall back to iterating Request.Form directly: for each (key, value) where key is not 'region'/'ping' and int.TryParse(value) succeeds, treat key as the region name and the value as the ping (same 0-2000 clamp), then feed the merged dictionary to presence.SetPhotonRegionPings.

##### `PUT player/statusvisibility` — STUB (degraded)

Verb and form binding are correct ([HttpPut], [FromForm(Name="statusVisibility")] — exact camelCase key the client sends), and the client ignores the response body, so no wire breakage. But the value is only echoed back and never persisted: no presence/db write exists, and Heartbeat/SetVrMovementMode hardcode StatusVisibility = 0 (lines 161, 193). The player's 'who can see my online status' choice is discarded — other players always see them as visible-to-Everyone, and the setting silently reverts.

Handler: `DorkNet.Server/Controllers/Match/MatchPlayerController.cs:168`

**Fix.** Persist the value per player (e.g. a field on PlayerPresenceService state or a Players column), read it back in Heartbeat/SetVrMovementMode responses, and honor it in the friend-presence read path.

##### `PUT player/vrmovementmode` — STUB (degraded)

Same pattern as statusvisibility: correct verb and camelCase form key 'vrMovementMode', client ignores the body, but the value is echoed and dropped — Heartbeat always reports vrMovementMode: 0 (line 163). If any server-side matchmaking logic is ever meant to gate on movement mode (the client sends BypassMovementModeRestriction on every matchmake/* call, implying the retail server matched comfort modes), it has no data to work from.

Handler: `DorkNet.Server/Controllers/Match/MatchPlayerController.cs:184`

**Fix.** Persist per player alongside statusVisibility and echo the stored value from Heartbeat.

##### `GET player/avoidjuniors` — STUB (degraded)

Shape is exactly right — GET returns a bare JSON boolean primitive (ActionResult<bool> Ok(false)) which the client's typed FDKKOPAPDGF reader requires, and PUT/POST (lines 219-223) binds the camelCase 'avoidJuniors' form key. But GET is hardcoded false and PUT discards the value (the code comment admits 'Stub for now'), so the 'avoid junior players' toggle appears to work in-session (the client caches its own write in a static) and then silently resets to Off on the next boot when the client re-reads it. Matchmaking also never actually avoids juniors since nothing is stored.

Handler: `DorkNet.Server/Controllers/Match/MatchPlayerController.cs:211`

**Fix.** Store the flag per player (Players table column or presence-service map), return the stored value from GetAvoidJuniors, and write it in SetAvoidJuniors. Optionally consult it in ApplyNewInstanceAsync/instance selection for actual junior-avoidance semantics.

##### `POST matchmake/* (cross-cutting): MaxPersistenceVersion form field` — SHAPE_MISMATCH (degraded)

The central sender adds MaxPersistenceVersion=Int32 to EVERY matchmake/* request (the client declaring the newest room-blob persistence version it can read), but a repo-wide grep for 'MaxPersistenceVersion' over *.cs returns zero hits — no handler binds it. The server instead relies on the global admin 'room version clamp' toggle (commits f5b5c14/d944a8f) to avoid handing 2023 clients blobs they can't parse. If the toggle is off, or a future mixed-version fleet connects, the server can serve a dataBlob whose persisted room version exceeds the requesting client's cap, triggering the 'update your client' room gate (room-header source; the CircuitsV2 graph-version source remains unfixable regardless).

**Fix.** Bind [FromForm(Name = "MaxPersistenceVersion")] int? in the matchmake handlers (or read it centrally in BuildResponseAsync via Request.Form) and clamp/normalize the served dataBlob per-request against the client-declared cap, making the per-request value the primary signal and the admin toggle the fallback.

##### `POST matchmake/room/{0} and matchmake/room/{0}/{1} (error path): roomInstance null vs empty object` — UNKNOWN (degraded)

The unknown-room branches return RoomInstance = null (MatchmakeRoom line 288, MatchmakeSubRoom line 324, GoToRoom line 91), and with no null-ignore condition configured (ServiceCollectionExtensions.cs:380-381 sets only PropertyNamingPolicy=null) the wire carries "roomInstance": null. GoToController's own header comment (lines 20-23, from the 2020 disassembly at RVA 0x1447DD0) documents that MatchmakingResponse.Deserialize THROWS 'Matchmaking response has no RoomInstance field' when the key is missing or non-object. The 2023 client uses a reflective attribute-driven reader (per the inventory) that likely tolerates null, but this is UNVERIFIED — and the sibling error paths (GoToClub :450, GoToCode :493/:506, GoToPlayer :887) all send an empty RoomInstanceDto object instead, so the codebase is internally inconsistent. If the 2023 reader shares the strict behavior, the friendly 'room not found' toast becomes a client-side deserialization exception.

Handler: `DorkNet.Server/Controllers/Match/GoToController.cs:288`

**Fix.** Change the three null-returning branches to RoomInstance = new RoomInstanceDto() to match the club/code/player error paths — one-line changes at GoToController.cs:91, :288, :324; alternatively verify the 2023 PCFNLCMMGKB reader tolerates null from the decompiled tree first.

##### `POST player/notifydisconnect` — CASING_MISMATCH (cosmetic)

Handler binds [FromForm(Name = "otherPlayerId")] — the 2020 wire shape. The 2023 client sends PascalCase 'PlayerId' (Int32) plus 'RoomInstanceId' (Int64), so otherPlayerId binds null on every 2023 call and the log line records other=null; RoomInstanceId is dropped entirely. Functionally harmless: the handler only logs, returns bare 200, and the client ignores the response — but the endpoint's diagnostic purpose (presence GC visibility) is defeated for 2023 clients.

Handler: `DorkNet.Server/Controllers/Match/MatchPlayerController.cs:113`

**Fix.** In MatchPlayerController.NotifyDisconnect add [FromForm(Name = "PlayerId")] int? playerId2023 and [FromForm(Name = "RoomInstanceId")] long? roomInstanceId, coalesce otherPlayerId ?? playerId2023 for the log, and log/use the instance id (e.g. presence GC of the reported player's row when it matches).

### Rooms, sub-rooms and room browsing

`rooms`

Diffed all 45 real HTTP routes of the 2023-03-21 client's rooms subsystem against DorkNet (branch march-2023). 14 routes are fully OK — notably the boot/save-critical paths: GET rooms/{id} (bare + roomserver, BuildRoomServerDetails), subroom data commit (envelope {success,value:{Room,SubRoomDataSave}}), subroom saves (paged), room clone, recommendations/similar/bulk/browse lists, filters, verifyRole, and the matchmaking instances/inprogress/reportjoinresult trio. The defects cluster into five systemic causes. (1) roomserver/ prefix gaps: live traffic (docs/recroom-2023-room-save.md:83-93) proves every NLDBPDCNNCF request arrives prefixed 'roomserver/', but ~20 routes are registered bare-only — name, description, image, tags, warning, restrictions, cloning, automute, comments, voice_chat_encryption, modify, all bans routes, roles (all 3), interactionby/me/cheer+favorite, playerdata/me, subrooms POST/DELETE, rooms/base, rooms/hot, rooms/search, rooms/rro_ids, featuredrooms/current — each is a silent 404 for the 2023 client (settings don't save, moderation broken). (2) Wrong mutation response shape: ApplyAndReturn and the subroom mutations return slim ToWireRoom/SceneWire objects, but the client re-parses full FGCPNAACHIK details after EVERY mutation and NREs on the missing SubRooms/Roles/Tags/Stats (the repo already fixed exactly this for clone — one ApplyAndReturn change fixes ~15 routes). (3) [FromBody] vs x-www-form-urlencoded: the client sends form bodies everywhere; handlers with [FromBody] params 415 before running (mechanism empirically documented in the repo itself), and several also read the wrong keys (tags: tag/autoTag vs Tags; automute: disable; warning: warningMask; restrictions: 4 supports* bools; bans: repeated id+banMask; subroom maxplayers/modify/move/accessibility: Value/NewIndex vs maxPlayers/newRoomId/accessibility; move even has wrong semantics — reorder vs move-to-other-room). (4) Verb gaps: roominstance markprivate (POST-only, client PUTs) and roomCode (GET/POST-only, client PUTs custom code), DELETE rooms/{id} (admin-prefixed only), DELETE loadscreen (absent). (5) Stubs: playerdata/me GET hardcodes Data="CAE=" with no PUT at all (CV2 per-room player persistence dead), comments/voice_chat_encryption are acknowledged no-ops, subroom permissions matrix is never stored. Shape-only extras: magic_door lacks the {Room,RefreshesAt,RefreshIntervalMinutes} wrapper; bans GET uses BannedPlayerId/CreatedAt instead of AccountId/BannedByAccountId/BanStartTime; featuredrooms Rooms rows lack RoomName; promo_external stores Type as string and its DELETE reads the wrong keys. rooms/requiring/{mode} element type remains UNKNOWN on the client side (server returns names).

**Client-side notes.** CROSS-CUTTING FINDINGS (rooms subsystem, client 2023-03-21):

1. VERB ENCODING: every request is a BNDIAONDFFF request object; its ctor (0x1830036A0) takes BestHTTP.HTTPMethods in rdx (0=GET, 2=POST, 3=PUT, 4=DELETE) and the route string in r9. In the NLDBPDCNNCF thin wrappers the verb shows up as the 'Move r8, N' immediately before the shared send helper. All verbs above were read off these constants, not guessed.

2. BODY ENCODING: mutating endpoints add fields via BNDIAONDFFF.AFGEDDANEKP(key, boxedValue) = x-www-form-urlencoded (verified against live traffic for clone: 'name=...' form body, docs/recroom-2023-room-save.md). The ONE JSON-body rooms endpoint is POST rooms/{id}/subrooms/{id}/data, which sets a raw serialized body via BNDIAONDFFF.FJLLPHFOOJJ and adds an X-On-Behalf-Of header. List-valued form fields repeat the key (id=..&id=.., tag=..&tag=..).

3. RESPONSE KEY CASING: every RecNet.Runtime DTO reader (files with 'DTO JFCMHHFNDFE' methods) matches each key against three literals — PascalCase, camelCase, all-lowercase — so PascalCase server output is always safe for these DTOs. The full DTO→reader→keyset map is saved at the audit working set (291 DTOs).

4. STRICTNESS: every room mutation (name/description/accessibility/roles/subroom ops/clone/...) expects the FULL FGCPNAACHIK room-details object back, and the client dispose-walk NREs on missing nested objects (Stats, SubRooms, Roles, Tags) — a partial response makes the UI show failure even though the server persisted. RankingContext is parsed by LHGPEEIOPHN as an opaque STRING (String getter/setter only) — sending a JSON object for it broke every room-details parse (see DorkNet.Server.Tests/RoomSave2023Tests.cs comment re befd590 revert).

5. HOST/PREFIX: NLDBPDCNNCF targets the rooms host; the same routes are also issued with a 'roomserver/' path prefix depending on service config (verified for clone in docs/recroom-2023-room-save.md). DorkNet serves every bare route in this subsystem (diffed against the server's reflected route table — zero bare gaps), but these roomserver/ twins are NOT registered and are potential silent-404s if the client picks the prefixed base: rooms/base, rooms/hot, rooms/search, rooms/rro_ids, rooms/{id}/{name,description,image,tags,warning,restrictions,cloning,comments,voice_chat_encryption,automute,modify}, rooms/{id}/roles[...], rooms/{id}/bans[...], rooms/{id}/interactionby/me/{cheer,favorite}, rooms/{id}/playerdata/me.

6. MATCHMAKING-SERVICE ROUTES: room/{0}/instances, roominstance/{0}/* and rooms/requiring/ are issued by RecNet.Matchmaking with service id 3 (r8=3 in the BNDIAONDFFF ctor) — the matchmaking host, not the rooms host. RoomInstance payloads elsewhere in matchmaking deserialize via HCHDEHIGEBE (reader NHGOFGABJIN.txt line 2590: ClubId, EncryptVoiceChat, EventId, IsFull, IsInProgress, IsPrivate, Location, MaxCapacity, Name, PhotonRegionId, PhotonRoomId, RoomCode, RoomId, RoomInstanceId, RoomInstanceType, SubRoomId).

7. NON-ROUTES: 7 of the 82 literals are not HTTP routes — hot_rooms/…, magic_door/{0}, my_visited_rooms/…, roominteraction/{0}, roomsbycreators/…, search_rooms/…, visited_rooms/… are cache keys minted by the string-returning methods of IBEOONPEELF_NestedType_GGDPFFDEDKN.txt, and 'room/' is a share-code deeplink prefix (RoomCode_NestedType_IDKJONDCJKN.txt).

8. UNKNOWNS: rooms/requiring/{mode} response element type could not be resolved from the ISIL metadata token (stored internally as a restricted-rooms list; server already implements rooms/requiring/{restriction}). The exact wire type of BNLPBMJJOMM.Value (subroom permissions) is a variant read through the shared value reader — keys are certain, scalar type is not.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| GET | `api/rooms/v1/filters` | none | AKCLLEJNFFD object: {"PinnedFilters": [String], "PopularFilters": [String], "TrendingFilters": [String]} — reader iterates exactly these 3 List<string> fields; accepts PascalCase/camelCase/lowercase variants of each key |
| POST | `api/rooms/v1/verifyRole` | x-www-form-urlencoded fields: roomId (Int64, current room from Matchmaking ObscuredLong), role (Int32 enum), context (String). Keys are camelCase. | none parsed — client ignores the body; return 200 |
| POST | `api/rooms/v2/report` | x-www-form-urlencoded fields (PascalCase!): RoomId (Int64), RoomKeyId (Nullable<Int64>), Details (String), ReportCategory (Int32 enum FPIBGPIAOBI/BDOGOIGCKMK) | none parsed — logged as 'Failed to report room' on error; return 200 |
| GET | `featuredrooms/current` | none | JBHJKCCFCIO: {"FeaturedRoomGroupId": Int64, "Name": String, "Rooms": [COGJIOGPNGD {"ImageName": String, "RoomId": Int64, "RoomName": String}]} (casing-tolerant) |
| GET | `room/{0}/instances` | none | JSON array: [PNDCMIMEJLD {"RoomInstanceId": Int64, "RoomId": Int64, "SubRoomId": Int64, "PlayerIds": [Int32], "IsFull": Boolean, "CreatedAt": DateTime string}] (casing-tolerant) |
| PUT | `roominstance/{0}/inprogress` | x-www-form-urlencoded field: inProgress (Boolean) | none parsed ('Failed to modify current room's game-in-progress state' on error); return 200 |
| PUT | `roominstance/{0}/markprivate` | none (empty body) | none parsed ('Failed to mark room instance {0} as private'); return 200 |
| POST | `roominstance/{0}/reportjoinresult` | x-www-form-urlencoded field: result (Int32 enum MEIJCIADDHJ) | none parsed ('Failed to report room join result'); return 200 |
| PUT | `roominstance/{0}/roomCode` | x-www-form-urlencoded fields: roomCode (String), forceChange (Boolean) | String (the accepted custom room code) — FGLDKEJLAKB<String> promise |
| GET | `rooms/base` | none | [NEMINAEBALC] — room summary, keys (PascalCase, casing-tolerant): RoomId Int64, Name String, Description String, ImageName String, CreatorAccountId Int64, CreatedAt DateTime, Accessibility Int32 enum, MaxPlayers Int32, M |
| GET | `rooms/bulk` | query params: repeated id (Int64) for the id overload; repeated name (String) for the name overload | [NEMINAEBALC] (same keyset as rooms/base) |
| GET | `rooms/cheeredby/me` | none | [NEMINAEBALC] |
| GET | `rooms/contestwinners` | none | [NEMINAEBALC] |
| GET | `rooms/createdby/me` | none | [NEMINAEBALC] |
| GET | `rooms/createdby/{0}` | none (accountId in path) | [NEMINAEBALC] |
| GET | `rooms/curated_playlists` | none | [Int64] — bare array of curated playlist ids |
| GET | `rooms/favoritedby/me` | none | [NEMINAEBALC] |
| GET | `rooms/fromcreators` | query params: repeated id (Int32 creator account ids), skip (Int32), take (Int32) | [NEMINAEBALC] |
| GET | `rooms/hot` | query params: repeated tag (String), skip (Int32), take (Int32) | FFCPFGBNLHN paged object: {"Results": [NEMINAEBALC], "TotalResults": Int32} |
| GET | `rooms/magic_door` | query param: partySize (Int32) | GAPJAGDNFEO: {"Room": NEMINAEBALC, "RefreshesAt": DateTime string, "RefreshIntervalMinutes": Int32} |
| GET | `rooms/moderatedby/me` | none | [NEMINAEBALC] |
| GET | `rooms/ownedby/me` | none | [NEMINAEBALC] |
| GET | `rooms/ownedby/{0}` | none | [NEMINAEBALC] |
| GET | `rooms/recommendations` | none | [OPGDCAELKMJ {"SeedRoom": NEMINAEBALC, "Rooms": [NEMINAEBALC]}] — list of seed-room groups, NOT a flat room list |
| GET | `rooms/requiring/` | none — mode enum name lowercased and concatenated onto the path: rooms/requiring/{mode} | Restricted-rooms list stored internally ('Failed to get restricted rooms list: ' on error). Element type not resolvable from ISIL metadata token — UNKNOWN (DorkNet serves rooms/requiring/{restriction}); likely an array o |
| GET | `rooms/rro_ids` | none | [Int64] — bare array of Rec Room Original room ids |
| GET | `rooms/search` | query params: query (String), skip (Int32), take (Int32) | FFCPFGBNLHN: {"Results": [NEMINAEBALC], "TotalResults": Int32} |
| GET | `rooms/topcreators` | none | [NEMINAEBALC] |
| GET | `rooms/visitedby/me` | query params: skip (Int32), take (Int32) | [NEMINAEBALC] |
| GET | `rooms/visitedby/{0}` | query params: skip (Int32), take (Int32) | [NEMINAEBALC] |
| GET (2 overloads) + DELETE | `rooms/{0}` | GET summary: none. GET details: query params include (Byte bitmask), unityAssetTarget (String), unityAssetVersion (Int32) from closure CAEBODLFAMA. DELETE: none. | GET summary → NEMINAEBALC. GET details → FGCPNAACHIK = NEMINAEBALC keys PLUS: "DataBlob" String, "DataBlobHash" String, "LoadScreens": [{"ImageName","Title","Subtitle" all String}], "PromoImages": [String], "PromoExterna |
| PUT | `rooms/{0}/accessibility` | x-www-form-urlencoded field: accessibility (Int32 enum JHAOFLCJNAL) | FGCPNAACHIK full room details (see rooms/{0}) — client re-parses the whole object; missing keys NRE |
| PUT | `rooms/{0}/allow_new_users` | form field: allowNewUsers (Boolean) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/automute` | form field: disable (Boolean) — NOTE key is 'disable', maps to DisableMicAutoMute | FGCPNAACHIK full room details |
| GET + POST | `rooms/{0}/bans` | GET: none. POST 'ban players': form fields id (repeated Int32 account ids) + banMask (Int32 enum DCDPJPHBHOA) | GET → [IBHAKOOKEEE {"AccountId": Int32, "BannedByAccountId": Int32, "BanStartTime": DateTime string}]. POST → none parsed. |
| POST | `rooms/{0}/bans/import` | form field: sourceRoomId (Int64) | none parsed; return 200 |
| DELETE | `rooms/{0}/bans/{1}` | form field: banMask (Int32 enum) | none parsed; return 200 |
| POST | `rooms/{0}/clone` | x-www-form-urlencoded field: name (String) — new room name | FGCPNAACHIK FULL room details of the clone (not a status wrapper); client 404→empty-body surfaces as message-less 'Failed to copy room' |
| PUT | `rooms/{0}/cloning` | form field: cloningAllowed (Boolean) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/comments` | form field: disable (Boolean) — key is 'disable', maps to DisableRoomComments | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/creator` | form field: accountId (Int32) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/description` | form field: description (String) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/image` | form field: imageName (String — pre-uploaded image blob name) | FGCPNAACHIK full room details |
| GET | `rooms/{0}/interactionby/me` | none | CNINIABILDI: {"Cheered": Boolean, "Favorited": Boolean, "LastVisitedAt": DateTime string} (casing-tolerant) |
| PUT + DELETE | `rooms/{0}/interactionby/me/cheer` | none (empty body) | none parsed; return 200 |
| PUT + DELETE | `rooms/{0}/interactionby/me/favorite` | none (empty body) | none parsed; return 200 |
| PUT + DELETE | `rooms/{0}/loadscreen` | PUT form fields: imageName (String), title (String), subtitle (String). DELETE form field: imageName (String). | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/max_player_calculation_mode` | form field: maxPlayerCalculationMode (Int32 enum DDIFIKACNBN) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/min_level` | form field: minLevel (Int32) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/modify` | form fields (camelCase): name, description (String), accessibility (Int32 enum), supportsJuniors, supportsScreens, supportsTeleportVR, supportsWalkVR, cloningAllowed, disableMicAut | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/name` | form field: name (String) | FGCPNAACHIK full room details |
| GET + PUT | `rooms/{0}/playerdata/me` | GET: none. PUT: form field data (String — opaque per-room player data blob). | GET → ILKMFMCOPPO: {"Data": String}. PUT → none parsed. |
| PUT + DELETE | `rooms/{0}/promo_external` | form fields: type (Int32 enum OJGHPAELGBO), reference (String) — same keys on both verbs | FGCPNAACHIK full room details |
| PUT + DELETE | `rooms/{0}/promo_images` | form field: imageName (String) on both verbs | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/restrictions` | form fields: supportsJuniors, supportsScreens, supportsTeleportVR, supportsWalkVR (Booleans) | FGCPNAACHIK full room details |
| GET | `rooms/{0}/roles` | none | [EFHPLDPNGIM {"AccountId": Int32, "Role": Int32 enum, "InvitedRole": Int32 enum, "LastChangedByAccountId": Int32}] (casing-tolerant) |
| GET + PUT | `rooms/{0}/roles/{1}` | GET: none. PUT: form field role (Int32 enum OMMBGJMJJPN). | GET → EFHPLDPNGIM single object. PUT → FGCPNAACHIK full room details. |
| PUT | `rooms/{0}/roles/{1}/invite` | form field: role (Int32 enum) | FGCPNAACHIK full room details |
| GET | `rooms/{0}/similar` | none | single OPGDCAELKMJ: {"SeedRoom": NEMINAEBALC, "Rooms": [NEMINAEBALC]} |
| POST | `rooms/{0}/subrooms` | form field: name (String) — new subroom name | FGCPNAACHIK full room details (with the new SubRooms entry) |
| DELETE | `rooms/{0}/subrooms/{1}` | none | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/subrooms/{1}/accessibility` | form field: accessibility (Int32 enum) | FGCPNAACHIK full room details |
| POST | `rooms/{0}/subrooms/{1}/clone` | none (empty body) | FGCPNAACHIK full room details |
| POST | `rooms/{0}/subrooms/{1}/data` | JSON body (raw body via BNDIAONDFFF.FJLLPHFOOJJ, not form): {"UnityAssetId": null\|String, "RoomData": {"Filename": String, "Hash": null, "OwnershipProof": null}, "SubRoomData": {" | legacy {success,value,error} envelope; value = NEOPBOMGIOG: {"Room": FGCPNAACHIK (FULL details — must include DataBlob, DataBlobHash, MaxPlayers, ToxmodEnabled, SubRooms/Roles/Tags/Stats or the client NREs in the respons |
| PUT | `rooms/{0}/subrooms/{1}/maxplayers` | form field: maxPlayers (Int32) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/subrooms/{1}/modify` | form fields: name (String), accessibility (Int32 enum), maxPlayers (Int32) | FGCPNAACHIK full room details |
| POST | `rooms/{0}/subrooms/{1}/move` | form field: newRoomId (Nullable<Int64> — target room) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/subrooms/{1}/name` | form field: name (String) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/subrooms/{1}/permissions` | serialized list of BNLPBMJJOMM permission entries; per-entry keys (from its serializer): Role (Int32), Type (Int32), Permission (Int32), Override (Boolean), Value (variant) | FGCPNAACHIK full room details |
| POST | `rooms/{0}/subrooms/{1}/publish_save` | form field: subRoomDataSaveId (Int64) | FGCPNAACHIK full room details |
| GET | `rooms/{0}/subrooms/{1}/saves` | query params: skip (Int32), take (Int32) | paged object GJDIKIMLCHA: {"Results": [JKIFFPPAJNK {"SubRoomDataSaveId" Int64, "SubRoomId" Int64, "UnityAssetId" String, "DataBlob" String, "DataBlobHash" String, "Description" String, "SavedByAccountId" Int64, "SavedOnP |
| PUT | `rooms/{0}/tags` | form fields: tag (repeated String), autoTag (repeated String) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/voice_chat_encryption` | form field: encryptVoiceChat (Boolean) | FGCPNAACHIK full room details |
| PUT | `rooms/{0}/warning` | form fields: warningMask (Int32 enum NGPHEBMBPIC), customWarning (String) | FGCPNAACHIK full room details |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `hot_rooms/{0}&skip={1}&take={2}` — Cache-key factory for the hot-rooms client cache
- `magic_door/{0}` — Cache-key factory
- `my_visited_rooms/skip={0}&take={1}` — Cache-key factory
- `room/` — Room share-code / deeplink construction
- `roominteraction/{0}` — Client cache invalidation after cheer/favorite mutations
- `roomsbycreators/{0}&skip={1}&take={2}` — Cache-key factory
- `search_rooms/{0}&skip={1}&take={2}` — Cache-key factory
- `visited_rooms/account_id={0}&skip={1}&take={2}` — Cache-key factory

#### Defects

##### `PUT roominstance/{0}/markprivate` — VERB_MISMATCH (breaks-gameplay)

Server registers POST only ([HttpPost] at :326). The 2023 client sends PUT (Matchmaking.txt:15483/15774, ctor verb constant 3=PUT) with an empty body → ASP.NET returns 405 Method Not Allowed, the client logs 'Failed to mark room instance {0} as private', and the host cannot close a public instance to private.

Handler: `DorkNet.Server/Controllers/Match/MatchController.cs:326`

**Fix.** Add [HttpPut("/roominstance/{instanceId:long}/markprivate")] to MarkPrivate (keep POST for the 2020 client).

##### `PUT roominstance/{0}/roomCode` — VERB_MISMATCH (breaks-gameplay)

Server registers GET+POST only; the 2023 client sets a custom room code via PUT with form fields roomCode + forceChange and expects the accepted code back as a raw JSON string → PUT gets 405 and the host cannot set/change a custom room code. Secondary: the existing handler ignores any submitted roomCode and always returns RoomCodeService.Generate(instanceId), so even after adding PUT the requested custom code would be discarded (the string response shape itself is correct: Content(JsonSerializer.Serialize(code))).

Handler: `DorkNet.Server/Controllers/Match/MatchController.cs:363`

**Fix.** Add [HttpPut] to the roomcode route; when the request carries a roomCode form field, validate/uniqueness-check it, persist it against the instance, and return it as a JSON string (honor forceChange).

##### `GET rooms/base` — MISSING (breaks-gameplay)

Bare rooms/base exists, but there is NO roomserver/rooms/base registration (routes list has roomserver/rooms — the by-name lookup — which does not match /roomserver/rooms/base). This route is issued by NLDBPDCNNCF whose requests all carry the roomserver/ prefix in this deployment (docs:83-93, RoomsController.cs:439-443), so the 2023 base/AG-rooms list request 404s. Secondary: rows are slim ToWireRoom objects missing NEMINAEBALC keys MaxPlayers/MinLevel/MaxPlayerCalculationMode/PersistenceVersion/ToxmodEnabled — tolerant reader defaults these, cosmetic only.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:307`

**Fix.** Add [HttpGet("roomserver/rooms/base")] to BaseRooms; optionally emit rows via BuildRoomServerListAsync for key parity with the other list endpoints.

##### `GET + DELETE rooms/{0}` — MISSING (breaks-gameplay)

GET is fine: bare (:390) and roomserver (:1445) both return BuildRoomServerDetails, and the include/unityAssetTarget/unityAssetVersion query params are numeric on the wire (closure CAEBODLFAMA boxes Int32/Nullable<Byte>/Nullable<Int32>) so the int? bindings work. DELETE is MISSING on the game path entirely — the only [HttpDelete("rooms/{id:long}")] lives in AdminController under [Route("api/admin/v1")] (AdminController.cs:25,442), so the client's owner delete-room (NLDBPDCNNCF.FPDBNHOHIAO, verb 4) gets 405 on rooms/{id} and 404 on roomserver/rooms/{id} — owners cannot delete rooms.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:390 (GET); none (DELETE)`

**Fix.** Add DELETE rooms/{roomId:long} + roomserver twin on the rooms controller: owner/co-owner gate, soft-archive (State=1) like AdminController.DeleteRoom, return 200 (client parses no body).

##### `PUT rooms/{0}/automute` — MISSING (breaks-gameplay)

No roomserver twin → 404 under the 2023 client's prefix. Additionally the handler binds [FromBody] BareBoolRequest + [FromForm(Name="DisableMicAutoMute")], but the client's form key is 'disable' — so even via the bare path the value is never read (no-op), and the [FromBody] parameter makes a form POST 415 (repo-documented mechanism). Response is ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:739`

**Fix.** Add roomserver twins; drop [FromBody], read form key disable (plus DisableMicAutoMute alias) via ReadBareBoolAsync; return full details.

##### `GET + POST rooms/{0}/bans` — SHAPE_MISMATCH (breaks-gameplay)

No roomserver twins for either verb (404 under prefix). GET response keys are Id/RoomId/BannedPlayerId/BannedByPlayerId/BanType/Until/Reason/CreatedAt (:245-255) but the client's IBHAKOOKEEE reader wants AccountId/BannedByAccountId/BanStartTime — zero overlap, so the banned-players screen shows default-zero entries. POST binds [FromBody] BareBanRequest{PlayerId,BanType} while the client sends form repeated 'id' + 'banMask' → 415 (form) and wrong keys anyway — in-room bans never persist.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:1167`

**Fix.** Add roomserver twins; GET: emit {AccountId=(int)BannedPlayerId, BannedByAccountId=(int)BannedByPlayerId, BanStartTime=CreatedAt}; POST: read form repeated id + banMask, create a ban row per id.

##### `PUT rooms/{0}/cloning` — MISSING (breaks-gameplay)

No roomserver twin → 404 under the 2023 prefix. Bare handler also mixes [FromBody]+[FromForm] (form POST likely 415 per the repo-documented [FromBody] behavior; the form Name="CloningAllowed" would match the client's 'cloningAllowed' case-insensitively if binding got that far) and returns ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:729`

**Fix.** Add roomserver twins; drop [FromBody] and use ReadBareBoolAsync("cloningAllowed",...); return full details.

##### `PUT rooms/{0}/description` — MISSING (breaks-gameplay)

No roomserver twin → the 2023 edit-description PUT 404s. Bare handler also carries a [FromBody] parameter (form POST at risk of 415) and returns ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:459`

**Fix.** Add roomserver twins; replace [FromBody]+[FromForm] with ReadStringValueAsync("description",...); return full details.

##### `PUT rooms/{0}/image` — MISSING (breaks-gameplay)

No roomserver twin → set-thumbnail 404s. Same [FromBody] 415 risk and ToWireRoom response as description.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:466`

**Fix.** Add roomserver twins; ReadStringValueAsync("imageName",...); full-details response.

##### `PUT + DELETE rooms/{0}/loadscreen` — MISSING (breaks-gameplay)

DELETE is not registered at all (bare or roomserver) — 'remove room loading screen' gets 405 → cannot remove load screens. PUT exists with twins and form support, but ReadJsonElementAsync serializes the whole form as ONE object and REPLACES LoadScreensJson (:845-847) instead of appending, so a room can never hold more than one load screen. Response is ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:834`

**Fix.** Register DELETE (bare + roomserver) reading form imageName and removing the matching entry; change PUT to append {ImageName,Title,Subtitle} to the existing list; return full details.

##### `PUT rooms/{0}/modify` — MISSING (breaks-gameplay)

No roomserver twin → the room-settings bulk save 404s. Bare BareModify also binds [FromBody] ModifyRoomRequest → 415 for the client's x-www-form-urlencoded body; the DTO is missing the client's fields supportsJuniors/cloningAllowed/disableRoomComments/encryptVoiceChat/accessibility-as-form; response is {Result, Room: ToWireRoom} — not the FGCPNAACHIK object.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:950`

**Fix.** Add roomserver twins; parse the 11 form keys (name/description/accessibility/supportsJuniors/supportsScreens/supportsTeleportVR/supportsWalkVR/cloningAllowed/disableMicAutoMute/disableRoomComments/encryptVoiceChat) directly from Request.Form; return BuildRoomServerDetails.

##### `PUT rooms/{0}/name` — MISSING (breaks-gameplay)

No roomserver/rooms/{id}/name twin (the SUBROOM rename got twins at :2658-2659, the room rename did not) → 2023 rename 404s. Bare handler also mixes [FromForm]+[FromBody RenameBody] (form PUT at risk of the repo-documented 415) and returns a minimal {Result,RoomId,Name,...} object instead of full FGCPNAACHIK details (no Stats/SubRooms/Roles/Tags) → client parse fails even on success.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2590`

**Fix.** Add roomserver twins; drop the [FromBody] parameter (read form name); return BuildRoomServerDetailsWithRolesAsync.

##### `GET + PUT rooms/{0}/playerdata/me` — STUB (breaks-gameplay)

PUT is MISSING everywhere (only GET registered) → saving per-room CV2 player data gets 405 and nothing persists. GET is a STUB: Data is hardcoded to "CAE=" (:2331) for every player/room, so persistent per-room player state always loads the same canned blob. Also GET has no roomserver twin → 404 under the 2023 prefix. The client contract is GET → {Data: string}, PUT form field data.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2307`

**Fix.** Add a PlayerRoomData table keyed (playerId, roomId); PUT (bare + roomserver) stores form 'data'; GET returns the stored blob (default empty/"CAE="); add roomserver twin for GET.

##### `PUT rooms/{0}/restrictions` — MISSING (breaks-gameplay)

No roomserver twin → 404 under prefix. Bare handler reads a single bool (form AllowsJuniors / body Value) but the client sends FOUR booleans: supportsJuniors, supportsScreens, supportsTeleportVR, supportsWalkVR — three of four settings are never read, and the [FromBody] parameter risks 415 on the form body. Response is ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:752`

**Fix.** Add roomserver twins; read all four form keys via ReadBareBoolAsync each and map to SupportsScreens/SupportsTeleportVR/SupportsWalkVR/AllowsJuniors; return full details.

##### `POST rooms/{0}/subrooms` — MISSING (breaks-gameplay)

Only the bare POST exists (the GET at :532 has a twin; the POST does not) → 'add new subroom' 404s under the 2023 prefix. The bare handler also binds [FromBody] CreateSubRoomRequest → 415 for the client's form 'name', and returns SceneWire instead of the full FGCPNAACHIK details the client re-parses.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3298`

**Fix.** Add [HttpPost("roomserver/rooms/{roomId:long}/subrooms")]; read form name; return BuildRoomServerDetailsWithRolesAsync.

##### `DELETE rooms/{0}/subrooms/{1}` — MISSING (breaks-gameplay)

Bare DELETE only (the roomserver/rooms/{id}/subrooms/{sub} registration at :554-555 is GET) → delete-subroom 404s under the prefix. Response {Result:0} is also not the expected full details.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3330`

**Fix.** Add roomserver DELETE twin; return full details.

##### `PUT rooms/{0}/subrooms/{1}/accessibility` — SHAPE_MISMATCH (breaks-gameplay)

Twins exist, but the handler binds [FromBody] SubRoomBoolRequest{Value:bool} while the client sends form 'accessibility' as an Int32 enum (JHAOFLCJNAL) → form body gets 415 (repo-documented [FromBody] behavior) and the key/type don't match anyway; response is SceneWire, not full details. Subroom accessibility can't be changed.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3383`

**Fix.** Drop [FromBody]; read form int accessibility (ReadBareIntAsync pattern) and map to CanMatchmakeInto/scene accessibility; return full details.

##### `PUT rooms/{0}/subrooms/{1}/maxplayers` — SHAPE_MISMATCH (breaks-gameplay)

Twins exist (added after the 404 was diagnosed), but the handler binds [FromBody] SubRoomIntRequest{Value} while the client sends form 'maxPlayers' → the form PUT 415s and even a JSON body would need key Value, so the max-players slider still can't persist. Response is SceneWire, not full details.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3375`

**Fix.** Drop [FromBody]; read form maxPlayers (int); return full details.

##### `PUT rooms/{0}/subrooms/{1}/modify` — SHAPE_MISMATCH (breaks-gameplay)

Twins exist, but [FromBody] SubRoomModifyRequest → 415 on the client's form body (keys name/accessibility/maxPlayers); 'accessibility' isn't even a property of the DTO. Response is SceneWire. Subroom settings bulk-save fails.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3525`

**Fix.** Drop [FromBody]; read the three form keys; map accessibility; return full details.

##### `PUT rooms/{0}/tags` — MISSING (breaks-gameplay)

No roomserver twin → edit-tags 404s under the 2023 prefix. Bare handler reads form key 'Tags' as CSV, but the client sends REPEATED form keys 'tag' and 'autoTag' → never read (no-op) and the [FromBody] parameter risks 415. Response ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:473`

**Fix.** Add roomserver twins; read Request.Form["tag"] + Request.Form["autoTag"] as lists, join into TagsCsv; return full details.

##### `PUT rooms/{0}/warning` — MISSING (breaks-gameplay)

No roomserver twin → 404 under the 2023 prefix. Bare handler is [FromBody] BareWarningRequest{RoomWarningMask,CustomRoomWarning} → 415 on the client's form body, whose keys are warningMask/customWarning anyway. Response ToWireRoom. Content warnings can't be saved.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:762`

**Fix.** Add roomserver twins; read form warningMask (int) + customWarning (string); return full details.

##### `POST api/rooms/v2/report` — SHAPE_MISMATCH (degraded)

Handler is [FromBody] RoomReportRequest{RoomId,Category,Message} (JSON only). The client sends x-www-form-urlencoded with PascalCase keys RoomId/RoomKeyId/Details/ReportCategory. A [FromBody] handler returns 415 for a form POST before the handler runs (mechanism empirically documented in this repo at RoomsModerationController.cs:963-968 and RoomSave2023Tests.cs:246-248). Even with JSON the field names differ (Category vs ReportCategory, Message vs Details). Result: room reports are never persisted; client logs 'Failed to report room' but UI is otherwise unaffected.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:267`

**Fix.** In Report(), drop [FromBody]; read Request.Form keys RoomId/RoomKeyId/Details/ReportCategory (case-insensitive) with a JSON fallback, map ReportCategory→Category and Details→Message.

##### `GET featuredrooms/current` — SHAPE_MISMATCH (degraded)

Two issues: (1) Rooms entries are RoomService.ToWireRoom objects (RoomId/Name/ImageName...) but the 2023 COGJIOGPNGD reader reads ImageName/RoomId/RoomName — there is no 'RoomName' key, so every featured tile's room name deserializes to null (reader is casing-tolerant and defaults missing keys, so no crash). (2) No roomserver/featuredrooms/current twin is registered; this route is called from NLDBPDCNNCF, the client whose every other request arrives with the roomserver/ prefix (docs/recroom-2023-room-save.md:83-93), so the 2023 call likely 404s outright.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2535`

**Fix.** Add RoomName (= room.Name) to each Rooms entry in FeaturedRoomsCurrent, and register [HttpGet("roomserver/featuredrooms/current")] on the same action.

##### `GET rooms/hot` — MISSING (degraded)

Paged {Results,TotalResults} wrapper is correct, but no roomserver/rooms/hot twin is registered → the NLDBPDCNNCF hot-rooms call 404s under the roomserver/ prefix (the play-menu 'hot' UI separately uses rooms/hot_rooms/{...} which IS served, so impact is limited to whatever surface uses NLDBPDCNNCF.FANOAMOPHBE). Secondary: [FromQuery] string? tag binds only the FIRST repeated tag value, and rows are slim ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:56`

**Fix.** Add [HttpGet("roomserver/rooms/hot")]; read Request.Query["tag"] as a list.

##### `GET rooms/magic_door` — SHAPE_MISMATCH (degraded)

Server returns the bare room-details object. The 2023 GAPJAGDNFEO reader expects a wrapper {Room: <room>, RefreshesAt: DateTime, RefreshIntervalMinutes: Int32}; with no 'Room' key the parsed DTO has Room=null and MagicDoorManager gets no destination — Magic Door dead/blank. partySize query param is ignored (acceptable).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:154`

**Fix.** Wrap the response: Ok(new { Room = wireRoom, RefreshesAt = <next refresh ISO string>, RefreshIntervalMinutes = N }).

##### `GET rooms/requiring/` — UNKNOWN (degraded)

Route + roomserver twin exist and accept the lowercased mode segment. Server returns a bare array of room NAMES (strings). The client's element type could not be resolved from ISIL metadata (restricted-rooms list; likely room ids). If the client expects Int64 ids, string names would parse to defaults or fail silently — cannot confirm either way.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2351`

**Fix.** Resolve the element type from the client (trace the list's consumer in Matchmaking) before changing; if ids are expected, Select(r => r.Id).

##### `GET rooms/rro_ids` — MISSING (degraded)

Bare route returns [Int64] (correct shape) but no roomserver/rooms/rro_ids twin exists → the NLDBPDCNNCF call 404s under the roomserver/ prefix and RRO badging/gating falls back to nothing.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2511`

**Fix.** Add [HttpGet("roomserver/rooms/rro_ids")] to AgRoomIds.

##### `GET rooms/search` — MISSING (degraded)

Paged {Results,TotalResults} shape and query/skip/take params are correct, but no roomserver/rooms/search twin → 404 under the roomserver/ prefix (play-menu search separately uses rooms/search_rooms/{...} which IS served and test-covered, so impact limited to the NLDBPDCNNCF.MOBJJDBNBMF surface). Rows are slim ToWireRoom (cosmetic).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:267`

**Fix.** Add [HttpGet("roomserver/rooms/search")].

##### `PUT rooms/{0}/accessibility` — SHAPE_MISMATCH (degraded)

Routing/verbs/body OK (roomserver twins registered, [Consumes] form, reads 'accessibility'). Defect: ApplyAndReturn responds with RoomService.ToWireRoom (RoomsModerationController.cs:1210) — the slim room object with NO SubRooms/Roles/Tags/LoadScreens/PromoImages/DataBlob/MinLevel/MaxPlayers. The 2023 client re-parses the FULL FGCPNAACHIK details after every mutation and its dispose-walk NREs on the missing nested lists (same failure the repo already documented for clone at RoomsModerationController.cs:1117-1125), so the toggle persists but the client reports failure.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:485`

**Fix.** Change ApplyAndReturn to return RoomsController.BuildRoomServerDetails (load scenes+roles like BuildRoomServerDetailsWithRolesAsync) instead of ToWireRoom — this one fix repairs the response of every bare-path room mutation.

##### `PUT rooms/{0}/allow_new_users` — SHAPE_MISMATCH (degraded)

Twins + form key allowNewUsers OK; response is ToWireRoom (same ApplyAndReturn defect as accessibility).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:789`

**Fix.** Same ApplyAndReturn fix.

##### `POST rooms/{0}/bans/import` — SHAPE_MISMATCH (degraded)

No roomserver twin; handler binds [FromBody] ImportRoomBansRequest (expects a Bans list) while the client sends form sourceRoomId → 415, and even as JSON the contract differs (client names a source ROOM to copy bans from; handler expects an explicit ban list). Import-ban-list never works.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:1195`

**Fix.** Add roomserver twin; read form sourceRoomId, copy that room's RoomBans rows into the target (owner-gated on both rooms).

##### `DELETE rooms/{0}/bans/{1}` — MISSING (degraded)

Bare DELETE works (banMask form field is ignored, which is fine — client parses no response), but no roomserver twin → unban 404s under the 2023 prefix.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:1181`

**Fix.** Add [HttpDelete("roomserver/rooms/{roomId:long}/bans/{playerId:long}")].

##### `PUT rooms/{0}/comments` — STUB (degraded)

No roomserver twin (404 under prefix), and the bare handler BareAck is an acknowledged no-op — DisableRoomComments is never persisted and the wire always reports DisableRoomComments=false, so the toggle never sticks across loads. Client's form key 'disable' is never read.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:940`

**Fix.** Add a DisableRoomComments column on RoomEntity, read form 'disable', persist, surface in both detail builders; add roomserver twins; return full details.

##### `PUT rooms/{0}/creator` — SHAPE_MISMATCH (degraded)

Twins + form key accountId handled (ReadBareLongAsync includes 'accountId'); response is ToWireRoom (same ApplyAndReturn defect).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:771`

**Fix.** Same ApplyAndReturn fix.

##### `PUT + DELETE rooms/{0}/interactionby/me/cheer` — MISSING (degraded)

Bare PUT/POST/DELETE all exist and the client parses no body, but there are NO roomserver twins → cheer/uncheer 404 under the 2023 prefix and the button silently fails.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2761`

**Fix.** Add roomserver/ twins for PUT+DELETE (and favorite below).

##### `PUT + DELETE rooms/{0}/interactionby/me/favorite` — MISSING (degraded)

Same as cheer: bare verbs fine, no roomserver twins → 404 under prefix.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2780`

**Fix.** Add roomserver twins.

##### `PUT rooms/{0}/max_player_calculation_mode` — SHAPE_MISMATCH (degraded)

Twins + form key maxPlayerCalculationMode OK; response is ToWireRoom (ApplyAndReturn defect).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:817`

**Fix.** Same ApplyAndReturn fix.

##### `PUT rooms/{0}/min_level` — SHAPE_MISMATCH (degraded)

Twins + form key minLevel OK; response is ToWireRoom (ApplyAndReturn defect).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:803`

**Fix.** Same ApplyAndReturn fix.

##### `PUT + DELETE rooms/{0}/promo_external` — SHAPE_MISMATCH (degraded)

Twins exist for both verbs. PUT stores the form dict verbatim, so Type is persisted as the STRING "1" while the client's EPPPACFECMH reader expects Type as Int32 — the strict value reader will reject or zero it when details are re-read. DELETE reads keys id/url/value but the client sends type+reference → always 400 missing_promo_external, so promo links can't be removed. Response is ToWireRoom.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:885`

**Fix.** PUT: build {Type=(int), Reference=(string)} explicitly from form. DELETE: match on form type+reference. Return full details.

##### `PUT + DELETE rooms/{0}/promo_images` — SHAPE_MISMATCH (degraded)

Twins for POST/PUT/DELETE all present, form key imageName read on both verbs — request side fully OK. Only defect: ToWireRoom response (ApplyAndReturn).

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:851`

**Fix.** Same ApplyAndReturn fix.

##### `GET rooms/{0}/roles` — MISSING (degraded)

Handler + wire shape are correct ([{AccountId,Role,InvitedRole,LastChangedByAccountId}], BuildRoomAccountRoleWire :4003-4014) but there is no roomserver twin → the permissions screen's list fetch 404s under the 2023 prefix.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3695`

**Fix.** Add [HttpGet("roomserver/rooms/{roomId:long}/roles")] (and twins for the two routes below).

##### `GET + PUT rooms/{0}/roles/{1}` — MISSING (degraded)

Handlers are contract-correct: GET returns a single role wire object; PUT reads form 'role' (ReadClientRoomRoleAsync :3944-3973) and returns full BuildRoomServerDetailsWithRolesAsync. But neither has a roomserver twin → 404 under the 2023 prefix.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3818 (GET), 3835 (PUT)`

**Fix.** Add roomserver twins for GET and PUT/POST.

##### `PUT rooms/{0}/roles/{1}/invite` — MISSING (degraded)

Handler correct (form role, Accepted=false row, full-details response); no roomserver twin → 404 under prefix.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3879`

**Fix.** Add roomserver twin.

##### `POST rooms/{0}/subrooms/{1}/clone` — SHAPE_MISMATCH (degraded)

Twins + empty-body POST OK; clone logic sound; but returns SceneWire instead of the full FGCPNAACHIK room details → client reports failure after a successful duplicate.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3483`

**Fix.** Return BuildRoomServerDetailsWithRolesAsync(roomId).

##### `POST rooms/{0}/subrooms/{1}/move` — SHAPE_MISMATCH (degraded)

Twins exist but the semantics are wrong: the client sends form 'newRoomId' (Nullable<Int64>) to move a subroom to ANOTHER ROOM; the handler binds [FromBody] SubRoomMoveRequest{NewIndex} and reorders scenes within the same room. Form body also 415s. Response SceneWire.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3543`

**Fix.** Read form newRoomId; when present re-parent the RoomSceneEntity to the target room (owner-gated on both, next OrderIndex there); return full details.

##### `PUT rooms/{0}/subrooms/{1}/name` — SHAPE_MISMATCH (degraded)

roomserver twins registered. Two residual defects: the action mixes [FromForm] name params with a [FromBody] RenameBody — the [FromBody] binder puts the form PUT at risk of the repo-documented 415; and the response is a minimal {Result,RoomId,SubRoomId,Name,...} object, not the full FGCPNAACHIK details the client re-parses.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2656`

**Fix.** Drop the [FromBody] parameter; return BuildRoomServerDetailsWithRolesAsync.

##### `PUT rooms/{0}/subrooms/{1}/permissions` — STUB (degraded)

Twins exist but the handler accepts SubRoomModifyRequest (Name/MaxPlayers/IsSandbox/CanMatchmakeInto) — the client actually sends a serialized LIST of BNLPBMJJOMM permission entries {Role,Type,Permission,Override,Value}. No permission matrix is ever stored (there is no storage for it), and the client body would 415/no-op. Response SceneWire. The role-permission matrix silently never saves.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3391`

**Fix.** Parse the request as a list of {Role,Type,Permission,Override,Value}; persist per-subroom permission rows (new table); echo full room details. Scope explicitly if deferred — currently it is a silent stub.

##### `POST rooms/{0}/subrooms/{1}/publish_save` — SHAPE_MISMATCH (degraded)

Twins + form key subRoomDataSaveId are handled (ReadPublishSubRoomSaveRequestAsync :3440-3478); restore logic sets the scene blob. Only defect: SceneWire response instead of full details → restore shows failure in the client after succeeding.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:3404`

**Fix.** Return BuildRoomServerDetailsWithRolesAsync.

##### `PUT rooms/{0}/voice_chat_encryption` — STUB (degraded)

Same as comments: no roomserver twin, no-op BareAck handler, EncryptVoiceChat never persisted and hardcoded false on the wire (RoomService.cs:1123, RoomsController.cs:2026); client form key encryptVoiceChat never read.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsModerationController.cs:942`

**Fix.** Persist an EncryptVoiceChat column; read form encryptVoiceChat; add twins; full-details response.

### Room currencies, consumables and keys

`room-economy`

All 25 real client routes were checked against DorkNet. RoomConsumables routes exist but three of them (consume, purchase/tokens, purchase/currency) only map POST while the client PUTs (405), and every consumable response embeds a nested PriceAndCurrency object where the client's response formatter (FCIBLPCOODP) reads FLAT Price/PurchaseCurrencyId/ModifiedAt — so all shop prices deserialize as 0/token. RoomCurrencies is the worst area: every response DTO uses server-invented key names (RoomCurrencyId/DailyLimit/UpdatedAt/PurchaseOfferId/Amount/PlayerId) that share almost no keys with the client formatters (CurrencyId/CurrencyType/Limit/ModifiedAt/CurrencyPurchaseOfferId/CurrencyAmount/Order/AccountId), awardCurrency/bulk cannot parse the client's JSON-array body at all, getPurchaseOffersBatch ignores the client's "ids" param and returns an ungrouped flat list, and v2/purchase returns a shape with zero keys in common with EGHOOCGCKAD. RoomKeys: all four edit routes (PUT updateAll/updateName/updateDescription/updatePrice) are missing (server only has POST v1/update), owns/bulk never reads the JSON array body and answers with "Owns" instead of "DoesPlayerOwnRoomKey", owns/purchased/mine have caller-vs-target semantic mismatches, and ToWire drops PurchaseCurrencyId/CreatedAt/ImageName. Only 5 routes are fully OK: consumable delete, consumable isOwned, roomkeys delete, roomcurrencies deletePurchaseOffer, and (verb/shape-wise) nothing else without caveats.

**Client-side notes.** VERB ENUM (ground truth): BNDIAONDFFF ctor (C:\tmp\recnet-runtime-decomp\BNDIAONDFFF.cs:194) takes BestHTTP.HTTPMethods (C:\tmp\recnet-runtime-decomp\BestHTTP\HTTPMethods.cs): 0=Get, 1=Head, 2=Post, 3=Put, 4=Delete. KEY CASING: every generated formatter carries three casing variants per key (PascalCase/camelCase/all-lowercase), so reads are casing-tolerant across those three exact forms, but the key NAME must match — unknown keys are skipped and missing value-type fields silently default. SERVER GAPS FOUND (DorkNet source cross-check): (1) RoomKeys update: client PUTs api/roomkeys/v1/updateAll|updateName|updateDescription|updatePrice; server only maps POST api/roomkeys/v1/update (DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:96) → all key edits 404. (2) owns/bulk: client POSTs a JSON array body [{RoomKeyId,AccountId}] which ReadRoomKeyIdsAsync (RoomKeysController.cs:215-232) never reads (query/form only), and the response key is "Owns" (RoomKeysController.cs:171) where the client reads "DoesPlayerOwnRoomKey" and also expects AccountId echoed. (3) RoomKeys ToWire (RoomKeysController.cs:180-190) omits PurchaseCurrencyId/CreatedAt/ImageName (defaults, non-fatal but lossy). (4) Consumables: client uses PUT for purchase/tokens, purchase/currency, and consume; server maps POST only (RoomConsumablesController.cs:230, :266, :307 — create/update at :139-140 already has PUT) → 405 on real client traffic. (5) Consumable RESPONSE desc must be FLAT (MOONCMIECPL formatter FCIBLPCOODP keys: Price, PurchaseCurrencyId, ModifiedAt); server ToDescWire (RoomConsumablesController.cs:407-414) sends nested PriceAndCurrency, so prices/currency deserialize as 0/null in catalog, /me, and edit responses. The nested PriceAndCurrency shape is correct ONLY for the create/update REQUEST DTO (JPFDLKMGKHF) and ExpectedPriceAndCurrency purchase bodies. docs/recroom-2023-room-consumables.md is wrong on both points (says POST, and attributes the nested desc to the response DTO) — update it. (6) RoomCurrencies responses are wrong nearly everywhere (RoomCurrenciesController.cs): currency ToWire (:376) sends RoomCurrencyId/Id/DailyLimit/UpdatedAt but client reads CurrencyId/Limit/ModifiedAt/CurrencyType (send CurrencyType=300 RoomCurrency); getBalance/getAllBalances (:82,:105) send PlayerId/RoomCurrencyId but client reads AccountId/CurrencyId/Balance/ModifiedAt; getBalance also ignores the client's "accountId" query param (server reads playerId); offer ToOfferWire (:391) sends PurchaseOfferId/Amount/CreatedAt but client reads CurrencyPurchaseOfferId/CurrencyId/CurrencyAmount/Order/ModifiedAt; getPurchaseOffersBatch takes param "ids" (room-currency Guids; server reads purchaseOfferIds/roomCurrencyIds → always empty filter) and must return List of {CurrencyId, PurchaseOffers:[...]} groups, not a flat offer list; awardCurrency/bulk request is a JSON ARRAY body [{TransactionId,RecipientId,Amount,CurrencyId}] which ReadFieldsAsync (:294-331) drops (object-root only) and the response must be [{AccountId,CurrencyId,Success,Error,Response:{AccountId,CurrencyId,Balance,AmountAwarded,AwardedAt}}]; roomCurrencies/v2/purchase response must be {CurrencyBalanceResponse:{AccountId,CurrencyId,Balance,ModifiedAt}, TokenBalanceResponse:{Balance,CurrencyType,Platform}} not the current {Success,Balance,...}. (7) Non-route literals seen alongside: "RoomCurrencyCreated"/"RoomCurrencyModified"/"RoomCurrencyDeleted" (MIHKAJFMIPE.txt:224-248) are in-client event/notification names, not HTTP routes. Storefront room-key purchasing (api/storefronts/v1/buyRoomKey GET!, api/storefronts/v1/PurchaseRoomKeyWithCurrency POST with RoomKeyId/RequestedPrice/RequestedPurchaseCurrencyId — DCFKEFHJAGC.txt:7085/:7424) belongs to the storefronts subsystem but completes the key-buy flow.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/roomCurrencies/v2/purchase` | params: "PurchaseOfferId" (Guid), "RequestedAmount" (Int64), "RequestedPrice" (Int64) | EGHOOCGCKAD: {"CurrencyBalanceResponse": {"AccountId": Int32, "CurrencyId": Guid, "Balance": Int64, "ModifiedAt": DateTime}, "TokenBalanceResponse": {"Balance": Int64, "CurrencyType": Int32 (EAFDEJBEFJB, RecCenterTokens= |
| PUT | `api/roomconsumables/v1/roomConsumable` | JSON body JPFDLKMGKHF: {"RoomConsumableId": Guid (absent/empty on create, set on update), "RoomId": Int64, "Name": String, "Description": String, "ImageName": String, "PriceAndCurr | GFKDAADENEE: {"Status": Int32 (enum DLDNGEDFMOJ, Success=0), "Consumable": MOONCMIECPL} where the RESPONSE desc is FLAT: {"RoomConsumableId": Guid, "RoomId": Int64, "Name": String, "Description": String, "ImageName": Str |
| GET | `api/roomconsumables/v1/roomConsumable/room/{roomId} (composed)` | none | JSON array of MOONCMIECPL (flat desc: RoomConsumableId, RoomId, Name, Description, ImageName, Price, PurchaseCurrencyId, ModifiedAt) |
| GET | `api/roomconsumables/v1/roomConsumable/room/{roomId}/me (composed)` | none | JSON array of HOMJKAOHGDG: [{"RoomConsumableId": Guid, "AccountId": Int32, "Count": Int32, "ConcurrencyCode": Guid, "ModifiedAt": DateTime, "Consumable": MOONCMIECPL (never null)}] |
| DELETE | `api/roomconsumables/v1/roomConsumable/{id} (composed from prefix)` | none (id in path) | bare Int32 status body (enum DLDNGEDFMOJ; deserialized as FGLDKEJLAKB<DLDNGEDFMOJ>) |
| PUT | `api/roomconsumables/v1/roomConsumable/{id}/consume (composed)` | JSON body CFOBCEPBBHA: {"CurrentConcurrencyCode": Guid?, "NewConcurrencyCode": Guid} — client GENERATES NewConcurrencyCode and adopts it locally; server must store it verbatim or n | BGHBAILNNJJ: {"Status": Int32 (DLDNGEDFMOJ), "InventoryItem": HOMJKAOHGDG (current row; returned on mismatch for resync)} |
| GET | `api/roomconsumables/v1/roomConsumable/{id}/isOwned (composed)` | none | bare JSON boolean |
| PUT | `api/roomconsumables/v1/roomconsumable/{0}/purchase/currency` | JSON body ILNJDMFNOCD: {"ConcurrencyCodes": {...}, "ExpectedPriceAndCurrency": {"Price": Int64, "CurrencyId": Guid}} | DEILBLCDNEA: {"OperationResult": Int32 (DLDNGEDFMOJ), "BalanceUpdateResult": Int32? (enum GACBLALELBP: 0 Success, 1 NotEnoughCredit), "CurrencyBalanceResponse": {"AccountId": Int32, "CurrencyId": Guid, "Balance": Int64,  |
| PUT | `api/roomconsumables/v1/roomconsumable/{0}/purchase/tokens` | JSON body ILNJDMFNOCD: {"ConcurrencyCodes": {"CurrentConcurrencyCode": Guid?, "NewConcurrencyCode": Guid}, "ExpectedPriceAndCurrency": {"Price": Int64, "CurrencyId": null}} | MJKFEHPIDME: {"OperationResult": Int32 (DLDNGEDFMOJ), "BalanceUpdateResult": Int32? (enum CABBDKFODEC: 0 OK, 2 NotEnoughCredit, 6 RequestedPriceDoesNotMatch...), "TokenBalanceResponse": {"Balance": Int64, "CurrencyType": |
| POST | `api/roomcurrencies/v1/awardCurrency/bulk` | JSON body = bare ARRAY of KOEIABFLEPM: [{"TransactionId": Guid, "RecipientId": Int32, "Amount": Int64, "CurrencyId": Guid}] — serialized then BNDIAONDFFF.FJLLPHFOOJJ(SetBody); NOT  | JSON array of KBPKAAIDCIJ: [{"AccountId": Int32, "CurrencyId": Guid, "Success": Boolean, "Error": String, "Response": {"AccountId": Int32, "CurrencyId": Guid, "Balance": Int64, "AmountAwarded": Int64, "AwardedAt": DateTi |
| POST | `api/roomcurrencies/v1/createCurrency` | form/query params via AFGEDDANEKP: "RoomId" (Int64), "Name" (String), "Description" (String), "Limit" (Int64), "ImageName" (String, optional) | single KDEOPJCNKMC object (same 9 keys as /currencies rows: CurrencyId, RoomId, Name, Description, CurrencyType, Limit, ImageName, CreatedAt, ModifiedAt) |
| POST | `api/roomcurrencies/v1/createPurchaseOffer` | params: "CurrencyId" (Guid), "Name" (String), "Amount" (Int64), "Price" (Int64, tokens), "Order" (Int32) | single CJPPFBEAAHD: {"CurrencyPurchaseOfferId": Guid, "CurrencyId": Guid, "Order": Int32, "Name": String, "CurrencyAmount": Int64, "Price": Int64, "ModifiedAt": DateTime} |
| GET | `api/roomcurrencies/v1/currencies` | query param "roomId" (Int64) — added via BNDIAONDFFF.AFGEDDANEKP | JSON array of currency DTO KDEOPJCNKMC: [{"CurrencyId": Guid, "RoomId": Int64? (nullable), "Name": String, "Description": String, "CurrencyType": Int32 enum EAFDEJBEFJB (RoomCurrency=300), "Limit": Int64, "ImageName": St |
| POST | `api/roomcurrencies/v1/deletePurchaseOffer` | param: "PurchaseOfferId" (Guid) | ignored by client (any 200 works) |
| GET | `api/roomcurrencies/v1/getAllBalances` | query param "roomId" (Int64) | JSON array of LGAMPMJNGFH: [{"AccountId": Int32, "CurrencyId": Guid, "Balance": Int64, "ModifiedAt": DateTime}] |
| GET | `api/roomcurrencies/v1/getBalance` | query params "currencyId" (Guid) and "accountId" (Int32) — note camelCase on the wire | single LGAMPMJNGFH: {"AccountId": Int32, "CurrencyId": Guid, "Balance": Int64, "ModifiedAt": DateTime} |
| GET | `api/roomcurrencies/v1/getPurchaseOffersBatch` | repeated/list query param "ids" = room-currency Guids (verb chosen dynamically by helper ALHIJCJOLCB.JIECAFGCODK(ids, 100) — GET for small batches; serve GET and POST) | JSON array GROUPED BY CURRENCY, List<EGHOFLLGKFA>: [{"CurrencyId": Guid, "PurchaseOffers": [CJPPFBEAAHD objects]}] — NOT a flat offer list |
| POST | `api/roomcurrencies/v1/updateCurrency` | params: "CurrencyId" (Guid) + optional "Name", "Description", "Limit" (Int64), "ImageName" (only sent when non-null) | single KDEOPJCNKMC object (see /currencies) |
| POST | `api/roomcurrencies/v1/updatePurchaseOffer` | params: "PurchaseOfferId" (Guid) + optional "Name", "Amount" (Int64), "Price" (Int64), "Order" (Int32) | single CJPPFBEAAHD (same keys as createPurchaseOffer) |
| POST | `api/roomkeys/v1/create` | params: "RoomId" (Int64), "Name", "Description", "Price" (Int32), "ImageName", "PurchaseCurrencyId" (Guid as string, "" when null) | ONLIDBLNMCC: {"Status": Int32 (enum GDIOEPIOPEE: 0 Success, 3 NameTooShort, 5 DuplicateName, 10/11 PriceTooLow/High, 12 PermissionDenied...), "RoomKey": {"RoomKeyId": Int64, "ReplicationId": Guid, "RoomId": Int64, "Name" |
| DELETE | `api/roomkeys/v1/delete/{roomKeyId} (composed)` | none (id in path) | bare Int32 status body (enum GDIOEPIOPEE) |
| GET | `api/roomkeys/v1/mine` | none | JSON array of JBOBOABIEBN room-key DTOs |
| GET | `api/roomkeys/v1/owns` | query params "playerId" (Int32) and "roomKeyId" (Int64) | bare JSON boolean |
| POST | `api/roomkeys/v1/owns/bulk` | JSON body = bare ARRAY of DIJEKBHKJDD: [{"RoomKeyId": Int64, "AccountId": Int32}] (serialized then SetBody FJLLPHFOOJJ — NOT query/form) | JSON array of NJBNPAOKNEE: [{"RoomKeyId": Int64, "AccountId": Int32, "DoesPlayerOwnRoomKey": Boolean}] — all three are non-nullable value types in the DTO; echo request pairs back |
| GET | `api/roomkeys/v1/purchased/{roomKeyId} (composed)` | none | bare JSON boolean |
| GET | `api/roomkeys/v1/room?roomId={id} (composed via NHNGFFFCBPD)` | query roomId (baked into route string) | JSON array of JBOBOABIEBN room-key DTOs (RoomKeyId/ReplicationId/RoomId/Name/Description/Price/PurchaseCurrencyId/CreatedAt/ImageName) |
| PUT | `api/roomkeys/v1/updateAll \| updateName \| updateDescription \| updatePrice (composed, 4 routes)` | PUT with params: "RoomKeyId" (Int64) always, plus per-action: updateAll={Name, Description, Price, PurchaseCurrencyId ("" if null), ImageName}; updateName={Name}; updateDescription | ONLIDBLNMCC {"Status", "RoomKey"} — same as create |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/roomconsumables` — n/a — prefix literal
- `api/roomkeys/` — n/a — prefix literal
- `api/roomkeys/v1/` — n/a — prefix; concatenated with an action string to form the real update routes (see updateAll entry)

#### Defects

##### `GET api/roomcurrencies/v1/currencies` — SHAPE_MISMATCH (breaks-gameplay)

Handler exists (GET, reads roomId query — both OK), but ToWire (RoomCurrenciesController.cs:376-389) emits RoomCurrencyId/Id/InternalId/CreatorPlayerId/DailyLimit/UpdatedAt. Client formatter PEOAOMGOMGC reads CurrencyId, RoomId, Name, Description, CurrencyType, Limit, ImageName, CreatedAt, ModifiedAt (verified PEOAOMGOMGC.txt:627-798). Missing CurrencyId means every currency deserializes with Guid.Empty as its id, so all downstream balance/offer lookups key on Guid.Empty; CurrencyType defaults to 0 instead of 300 (RoomCurrency), Limit and ModifiedAt default.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:18`

**Fix.** Rewrite ToWire to { CurrencyId = currency.PublicId, RoomId (nullable long), Name, Description, CurrencyType = 300, Limit = currency.DailyLimit (long), ImageName, CreatedAt, ModifiedAt = currency.UpdatedAt }.

##### `POST api/roomcurrencies/v1/createCurrency` — SHAPE_MISMATCH (breaks-gameplay)

Route+verb OK, RoomId/Name/Description/ImageName read OK (case-insensitive dict), but the client sends "Limit" (Int64) and the server only reads dailyLimit/DailyLimit (line 48) so the limit is silently dropped to 0. Response uses the broken ToWire — the creator's client never learns the new currency's CurrencyId (Guid.Empty).

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:30`

**Fix.** Add "Limit" (and "limit") to the ReadInt/ReadLong alias list at line 48 (store as long), and fix ToWire as above.

##### `POST api/roomcurrencies/v1/awardCurrency/bulk` — SHAPE_MISMATCH (breaks-gameplay)

Client serializes a bare JSON ARRAY body [{TransactionId, RecipientId, Amount, CurrencyId}] and PUTs it via SetBody (verified: MIHKAJFMIPE.txt route at instr 037, verb 2 at 039, serialize+FJLLPHFOOJJ at 055-061; request formatter GFKKOADFJKC keys TransactionId/RecipientId/Amount/CurrencyId). Server ReadFieldsAsync (lines 294-331) only enumerates a JSON OBJECT root — an array root yields zero fields — so FindCurrencyAsync returns null and every award 404s. Even if parsed, the handler reads playerIds/amount (single amount for all) and responds [{PlayerId, RoomCurrencyId, Balance}] where the client formatter JCPFCOEEELK reads AccountId/CurrencyId/Success/Error/Response and nested GAGIJHJGFJD reads AccountId/CurrencyId/Balance/AmountAwarded/AwardedAt (verified). CircuitsV2 award chips are fully dead.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:113`

**Fix.** In AwardCurrencyBulk, parse the raw body as a JSON array of {TransactionId:Guid, RecipientId:int, Amount:long, CurrencyId:Guid}; per element resolve the currency by PublicId, apply AddBalanceAsync per recipient, and return [{AccountId, CurrencyId, Success:bool, Error:string, Response:{AccountId, CurrencyId, Balance, AmountAwarded, AwardedAt}}].

##### `GET api/roomcurrencies/v1/getBalance` — SHAPE_MISMATCH (breaks-gameplay)

Route+verb OK and "currencyId" is accepted by FindCurrencyAsync. Two defects: (1) client sends query "accountId" (verified MIHKAJFMIPE.txt instrs 051/063 this session — can target OTHER players) but server reads only playerId/PlayerId (line 80) and falls back to the caller, so circuits querying another player's balance silently get the caller's. (2) Response {PlayerId, RoomCurrencyId, Balance} vs client formatter DPNLANKOHON reading AccountId/CurrencyId/Balance/ModifiedAt (verified DPNLANKOHON.txt:331-414) — AccountId=0 and CurrencyId=Guid.Empty so the client cannot attribute the balance it receives.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:74`

**Fix.** Read "accountId" (add to the ReadLong aliases at line 80) and respond { AccountId, CurrencyId = currency.PublicId, Balance, ModifiedAt }.

##### `GET api/roomcurrencies/v1/getAllBalances` — SHAPE_MISMATCH (breaks-gameplay)

Route/verb/roomId OK. Response rows {PlayerId, RoomCurrencyId, Balance} — client reads AccountId/CurrencyId/Balance/ModifiedAt (DPNLANKOHON). CurrencyId deserializes as Guid.Empty on every row so the currency HUD cannot map any balance to a currency on room entry.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:90`

**Fix.** Emit { AccountId = (int)playerId, CurrencyId = x.c.PublicId, x.b.Balance, ModifiedAt = x.b.UpdatedAt } per row.

##### `POST api/roomcurrencies/v1/createPurchaseOffer` — SHAPE_MISMATCH (breaks-gameplay)

Route/verb OK; CurrencyId/Name/Amount/Price are read. "Order" (Int32) is not read or persisted (entity has no Order). Response ToOfferWire (lines 391-404) emits PurchaseOfferId/RoomCurrencyPackageId/Id/InternalId/RoomCurrencyId/Amount/CurrencyType/CreatedAt/UpdatedAt — client formatter AALDCAANEMM reads CurrencyPurchaseOfferId, CurrencyId, Order, Name, CurrencyAmount, Price, ModifiedAt (verified AALDCAANEMM.txt:499-622). Only Name and Price land; the offer id and amount come back as Guid.Empty/0, so the creator UI shows a broken pack.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:137`

**Fix.** Add an Order int column to RoomCurrencyPurchaseOfferEntity (read "Order"/"order"), and change ToOfferWire to { CurrencyPurchaseOfferId = offer.PublicId, CurrencyId = currency.PublicId, Order, Name, CurrencyAmount = offer.Amount (long), Price (long), ModifiedAt = offer.UpdatedAt }.

##### `GET (POST fallback for big batches) api/roomcurrencies/v1/getPurchaseOffersBatch` — SHAPE_MISMATCH (breaks-gameplay)

GET+POST both mapped (good — client picks verb dynamically via ALHIJCJOLCB.JIECAFGCODK). Two defects: (1) client sends the list param "ids" containing room-currency Guids (verified MIHKAJFMIPE_NestedType_OBLJADOFIED.txt instr 066 route / 076 "ids", this session); server reads purchaseOfferIds/roomCurrencyIds/offerIds/currencyIds (lines 202-203) so both filters stay empty and it dumps up to 200 arbitrary offers. (2) Response must be GROUPED: List<{CurrencyId, PurchaseOffers:[offer]}> per formatter LKBGCOELLIN (verified: only keys CurrencyId/PurchaseOffers), but the server returns a flat ToOfferWire list — the client deserializes rows with CurrencyId=Guid.Empty and PurchaseOffers=null, so the This Room pack list is empty/garbage.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:196`

**Fix.** Add "ids"/"Ids" to the currency-id alias list, group results by currency PublicId, and return rows.GroupBy(...).Select(g => new { CurrencyId = g.Key, PurchaseOffers = g.Select(fixed ToOfferWire) }).

##### `POST api/roomCurrencies/v2/purchase` — SHAPE_MISMATCH (breaks-gameplay)

POST mapped, PurchaseOfferId lookup works. RequestedAmount/RequestedPrice are ignored (no price-agreement validation — degraded but not fatal). Fatal part: response is {Success, Balance, CurrencyType, RoomCurrencyId, RoomCurrencyBalance} while the client formatter KNGMIABFJHK reads exactly CurrencyBalanceResponse + TokenBalanceResponse (verified), with nested DPNLANKOHON {AccountId,CurrencyId,Balance,ModifiedAt} and PECGEJAAMHB {Balance,CurrencyType,Platform} (verified). Zero keys match, so after buying a pack the client sees null balances — the token counter and currency balance never update and the buy UI errors.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:216`

**Fix.** Validate RequestedPrice==offer.Price (and RequestedAmount==offer.Amount), then return { CurrencyBalanceResponse = { AccountId=(int)pid, CurrencyId=currency.PublicId, Balance=customBalance, ModifiedAt=UtcNow }, TokenBalanceResponse = { Balance=newBalance, CurrencyType=2, Platform=0 } }. The insufficient-funds path must also emit this shape (with the unchanged balances) — the current {Success=false,...} anonymous object is equally unreadable.

##### `GET api/roomconsumables/v1/roomConsumable/room/{roomId}` — SHAPE_MISMATCH (breaks-gameplay)

Route/verb OK, real data. But every row goes through nested ToDescWire while the client reads flat Price/PurchaseCurrencyId/ModifiedAt (FCIBLPCOODP.txt:670-710, verified). Every item in every room shop deserializes with Price=0 and PurchaseCurrencyId=null, i.e. the whole catalog appears token-priced at 0 — the room economy is effectively free and currency-priced items take the wrong purchase path.

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:80`

**Fix.** Same ToDescWire flattening as above (single shared fix).

##### `PUT api/roomconsumables/v1/roomConsumable/{id}/consume` — VERB_MISMATCH (breaks-gameplay)

Client PUTs (verified this session: LPNHMEFDAAG.txt:1449-1460, route format "{0}/v1/roomConsumable/{1}/consume" + verb literal 3 at instr 065). Server maps [HttpPost] only → ASP.NET returns 405 Method Not Allowed and no consumable can ever be consumed. Handler logic and the {Status, InventoryItem} response otherwise match LPFAFIGAIOE (concurrency-code store-verbatim behavior is already correct), apart from the nested Consumable desc issue.

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:307`

**Fix.** Add [HttpPut("api/roomconsumables/v1/roomConsumable/{publicId:guid}/consume")] alongside the existing HttpPost.

##### `PUT api/roomconsumables/v1/roomconsumable/{0}/purchase/tokens` — VERB_MISMATCH (breaks-gameplay)

Client PUTs (verified this session: DCFKEFHJAGC.txt instrs 097-108 — route literal + verb 3). Server maps [HttpPost] only → 405; no token-priced consumable can be bought. The response builder TokenResponse (lines 382-392) otherwise matches HJOKFJKKEJM/PECGEJAAMHB exactly (verified key sets this session).

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:230`

**Fix.** Add [HttpPut] for the same route.

##### `PUT api/roomconsumables/v1/roomconsumable/{0}/purchase/currency` — VERB_MISMATCH (breaks-gameplay)

Client PUTs (verified this session: DCFKEFHJAGC.txt instrs 112-123 — route literal + verb 3). Server maps [HttpPost] only → 405; currency-priced consumables unbuyable. CurrencyResponse shape (lines 394-405) matches the claimed IDBFKCFEAHM/DPNLANKOHON shape. Secondary nit: mismatch paths return BadRequest("price_mismatch") instead of an OperationResult body — the client's strict reader gets a 400 with a non-DTO body; prefer returning the DTO with a failure OperationResult.

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:266`

**Fix.** Add [HttpPut] for the same route; optionally convert the BadRequest branches to 200 + DTO.

##### `PUT api/roomkeys/v1/updateAll \| updateName \| updateDescription \| updatePrice` — MISSING (breaks-gameplay)

Client builds "api/roomkeys/v1/" + action with verb PUT and param "RoomKeyId" (verified this session: AHMBBJNANBP.txt instr 058 concat prefix, 067 verb 3, 079 "RoomKeyId"; action literals at :1345 updateAll, :1559 updateName, :1772 updateDescription, :2043 updatePrice). The server registers only POST api/roomkeys/v1/update — none of the four real routes exist, so every room-key edit 404s. Per-action fields: updateAll={Name,Description,Price,PurchaseCurrencyId,ImageName}, updateName={Name}, updateDescription={Description}, updatePrice={Price,PurchaseCurrencyId}. Response must be the {Status, RoomKey} envelope.

Handler: `none (nearest: POST api/roomkeys/v1/update, DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:96)`

**Fix.** Add [HttpPut] routes api/roomkeys/v1/updateAll, /updateName, /updateDescription, /updatePrice (can share the existing Update handler with a partial-update flag per action), reading RoomKeyId + the per-action fields from query/form, returning RoomKeyResponse.

##### `POST api/roomkeys/v1/owns/bulk` — SHAPE_MISMATCH (breaks-gameplay)

Verb OK (POST mapped; verified client POSTs with a serialized JSON ARRAY body via SetBody — AHMBBJNANBP_NestedType_JIPDBBEFAPN.txt instrs 044-065, read this session). Three defects: (1) request body [{RoomKeyId:Int64, AccountId:Int32}] is never read — ReadRoomKeyIdsAsync (lines 215-232) parses query and form only, so ids is always empty and the handler returns [] for every batch; (2) even with ids, the server checks only the CALLER's ownership while the request pairs name arbitrary AccountIds; (3) response key is "Owns" (line 171) and AccountId is absent, but the client formatter KHFKIJEGPDG reads RoomKeyId/AccountId/DoesPlayerOwnRoomKey, all non-nullable (verified KHFKIJEGPDG.txt:279-346) — "Owns" is skipped as unknown and DoesPlayerOwnRoomKey defaults to false. Batched key-door resolution is fully dead.

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:159`

**Fix.** Parse the JSON body as an array of {RoomKeyId:long, AccountId:int}, query RoomKeyPurchases for each (AccountId, RoomKeyId) pair, and echo [{RoomKeyId, AccountId, DoesPlayerOwnRoomKey}] per request element.

##### `POST api/roomcurrencies/v1/updateCurrency` — SHAPE_MISMATCH (degraded)

Route+verb OK; FindCurrencyAsync (line 245) accepts "CurrencyId" so lookup works. But optional "Limit" is ignored (server reads dailyLimit only, line 68) and the response is the broken ToWire, so the edit UI redisplays wrong data (Limit=0, CurrencyId=empty).

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:55`

**Fix.** Read "Limit" alias in UpdateCurrency; fix ToWire.

##### `POST api/roomcurrencies/v1/updatePurchaseOffer` — SHAPE_MISMATCH (degraded)

Route/verb OK; PurchaseOfferId lookup works via FindOfferAsync (line 256), Name/Amount/Price read. "Order" is silently dropped and the response is the same broken ToOfferWire (CurrencyPurchaseOfferId/CurrencyAmount/CurrencyId/Order/ModifiedAt all default), so the pack list re-renders wrong after every edit.

Handler: `DorkNet.Server/Controllers/API/RoomCurrencies/RoomCurrenciesController.cs:159`

**Fix.** Read/persist Order; fix ToOfferWire (same change as createPurchaseOffer).

##### `PUT api/roomconsumables/v1/roomConsumable (create/update)` — SHAPE_MISMATCH (degraded)

PUT and POST both mapped (verb OK); request parsing handles the nested PriceAndCurrency{Price,CurrencyId} body correctly (ReadPriceAndCurrency:476). Defect is the response: EditResponse embeds ToDescWire (lines 407-415) which nests PriceAndCurrency{Price,CurrencyId}, but the client's RESPONSE formatter FCIBLPCOODP reads FLAT keys Price, PurchaseCurrencyId, ModifiedAt (verified FCIBLPCOODP.txt:670-710 — no PriceAndCurrency key exists in that formatter). After saving, the editor shows Price=0, no currency, default ModifiedAt.

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:139`

**Fix.** Change ToDescWire to flat: { RoomConsumableId, RoomId, Name, Description, ImageName, Price, PurchaseCurrencyId = c.CurrencyId, ModifiedAt = c.UpdatedAt }. Keep the nested reader for REQUESTS only. Also update docs/recroom-2023-room-consumables.md, which wrongly documents the nested desc as the response shape.

##### `GET api/roomconsumables/v1/roomConsumable/room/{roomId}/me` — SHAPE_MISMATCH (degraded)

Route/verb OK; the inventory-row envelope (RoomConsumableId/AccountId/Count/ConcurrencyCode/ModifiedAt/Consumable, Consumable never null) matches CICEHLNDLDE exactly — no NRE risk. But the nested Consumable is the same nested-PriceAndCurrency desc, so owned items carry Price=0/PurchaseCurrencyId=null/ModifiedAt=default inside the inventory too.

Handler: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs:95`

**Fix.** Fixed automatically by the ToDescWire flattening.

##### `POST api/roomkeys/v1/create` — SHAPE_MISMATCH (degraded)

POST mapped, Status/RoomKey envelope and status enum match GDIOEPIOPEE. Three gaps: (1) request fields ImageName and PurchaseCurrencyId sent by the client are never read (ReadBodyAsync lines 234-286 only maps roomId/name/description/price), so keys lose their image and can never be room-currency-priced; (2) ToWire (lines 180-190) omits PurchaseCurrencyId, CreatedAt, ImageName which the client formatter ANHGLIFGACE reads (verified full key set this session) — they silently default; (3) ReadBodyAsync reads form or JSON body but never Request.Query — whether the 2023 client sends these as query string or form body is UNKNOWN (AFGEDDANEKP bodies are stripped in the decomp); the sibling RoomCurrenciesController reads both, RoomKeys should too for safety.

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:61`

**Fix.** Extend RoomKeyEntity + ReadBodyAsync with ImageName and PurchaseCurrencyId (Guid?, empty-string = null); add query-param fallback; extend ToWire with PurchaseCurrencyId, CreatedAt, ImageName.

##### `GET api/roomkeys/v1/purchased/{roomKeyId}` — SHAPE_MISMATCH (degraded)

Route/verb/bare-boolean OK, but the semantics are inverted: the client call-site is the creator asking "has ANYONE bought this key" before destructive edits, while the server answers "has the CALLER bought it" (p.PlayerId == pid, line 147). A creator never buys their own key, so this always returns false and the sold-stock warning never fires.

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:143`

**Fix.** Change the query to AnyAsync(p => p.RoomKeyId == roomKeyId) (optionally excluding the key's creator), no PlayerId filter.

##### `GET api/roomkeys/v1/room?roomId={id}` — SHAPE_MISMATCH (degraded)

Route/verb/array-shape OK; RoomKeyId/ReplicationId(Guid)/RoomId/Name/Description/Price present. ToWire omits PurchaseCurrencyId, CreatedAt, ImageName which the client formatter ANHGLIFGACE reads (verified) — key thumbnails blank, currency-priced keys indistinguishable from token-priced, CreatedAt defaults to DateTime.MinValue (non-nullable value type, silently defaulted, no crash).

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:22`

**Fix.** Extend ToWire (and the entity, per the create fix) with PurchaseCurrencyId (Guid?), CreatedAt, ImageName.

##### `GET api/roomkeys/v1/mine` — SHAPE_MISMATCH (degraded)

Route/verb/array OK, but semantics differ: the client call-site is the watch key ring — keys the caller OWNS/PURCHASED — while the server returns keys the caller CREATED (k.CreatorPlayerId == pid, line 39). Players see none of their bought keys (and creators see keys they don't hold). Also inherits the lossy ToWire.

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:34`

**Fix.** Join RoomKeyPurchases on PlayerId == caller and return the purchased keys' JBOBOABIEBN wires (decide whether creators' own keys should also appear; purchased set is what the UI labels 'mine').

##### `GET api/roomkeys/v1/owns` — SHAPE_MISMATCH (degraded)

Route/verb/bare-boolean OK, but the client sends BOTH "playerId" and "roomKeyId" query params and can ask about OTHER players (key-door gating); the server ignores playerId entirely and always answers for the caller (line 154-155). Key doors evaluating another player's ownership get the wrong answer whenever the target differs from the caller.

Handler: `DorkNet.Server/Controllers/API/RoomKeys/RoomKeysController.cs:151`

**Fix.** Accept [FromQuery] long? playerId and check ownership for playerId ?? caller.

### Avatar, wardrobe, consumables and custom items

`avatar-inventory`

Verified all 37 real client HTTP calls against the DorkNet server source (every handler file opened and read; the regex route index was cross-checked against actual attributes). 20 routes are fully OK. 9 endpoints are MISSING/verb-less: POST api/customAvatarItems/v1 (create), PUT+DELETE .../v1/{id}, GET+PUT+DELETE .../v1/design, POST .../v1/{id}/report, POST econ/customAvatarItems/v1/{id}/purchase (exists only under api/), GET api/consumables/v1/getTransferable/{id}, GET api/ugcPurchasables/v1/items/room/{roomId} — together these kill the entire custom-shirt designer/purchase pipeline and the consumable-transfer and in-room-store list flows. 8 shape/binding mismatches: bare-scalar violations on econ itemOwnershipLimit (object vs Int32, EconController.cs:101) and isCreationAllowedForAccount (object vs Boolean, CustomAvatarItemsController.cs:194); fromCreator returns a bare array where the client parses paged {Results,TotalResults} (CustomAvatarItemsController.cs:131); customAvatarItems/v1/bulk cannot receive the client's 'customAvatarItemIds' key by body ([FromBody] whitelist + 415-on-form, :60-71/:303-326) so remote players' shirts never resolve; consumables/v1/transfer binds [FromForm] but the client sends a JSON body → 415 (InventoryController.cs:212); saved-outfit DTO drops OutfitSelectionsV2 + CustomAvatarItems on both GET and set (AvatarSavedOutfitsController.cs:95-103); equipment/v1/update binds one object where ISIL shows the client posting a JSON array (PLAUSIBLE — server comments imply a single object bound live; needs a trace); gifts/generate v2+v3 keep [FromBody] and would 415 on the form-urlencoded body the same client helper provably produced for gifts/consume (PLAUSIBLE). Minor: v2/getUnlocked consumables hardcodes IsActive/IsTransferable=false; lockeditems ships a 5-key subset of the shared owned-item DTO (null TagList risk); minPriceForPublicItem key match is UNKNOWN (single-Int32 client DTO, key not in ISIL).

**Client-side notes.** HTTP layer: every request goes through request-builder BNDIAONDFFF; its ctor (native 0x1830036A0) takes the VERB ENUM in rdx and the route string in r9. Enum decoded from update/delete semantics across this subsystem: 0=GET, 2=POST, 3=PUT, 4=DELETE (e.g. customAvatarItems v1/{id} uses 3 for the update overload and 4 for delete; design save uses 3, design delete 4). Helpers: AFGEDDANEKP(key, boxedValue) adds a named body/query param (key literals visible in ISIL — the only place exact request keys can be read); FJLLPHFOOJJ sets a JSON body (JsonConvert via ALHIJCJOLCB or JsonUtility.ToJson); BPHHLAIILHP adds a multipart file part (always filename "file.bin"); AMDPEBKIHOH is the cached-GET variant; LBIIOPNIDAC.PBGNNIJLBDG is response-cache INVALIDATION — route strings passed to it (EAIGBBHMIKM.txt:2733, EAIGBBHMIKM_NestedType___c.txt:1335) are cache keys, not HTTP calls. Response DTO property getters are obfuscated auto-props with no key literals in ISIL (Newtonsoft reflection over name-preserved metadata), so exact response keys were grounded in the DorkNet server implementation that runs live against this exact client, which itself embeds per-key decompile citations (e.g. EKHJKDNHOPB.txt:471-718 for the avatar reader/writer). SERVER GAPS FOUND (client calls with no/incompatible server route): (1) GET api/consumables/v1/getTransferable/{id} — missing entirely; (2) POST api/customAvatarItems/v1 (create, multipart metadata+thumbnailImage) — no POST on collection; (3) PUT + DELETE api/customAvatarItems/v1/{id} — missing; (4) GET + PUT + DELETE api/customAvatarItems/v1/design — server only has POST; (5) POST api/customAvatarItems/v1/{id}/report — missing; (6) POST econ/customAvatarItems/v1/{id}/purchase/?requestedPrice= — purchase only exists under api/, not econ/; (7) GET api/ugcPurchasables/v1/items/room/{roomId} — server only accepts ?roomId= query form. SHAPE MISMATCHES (route exists but wire likely wrong): (a) econ/customAvatarItems/v1/itemOwnershipLimit — client parses BARE Int32, server sends {Limit,ItemOwnershipLimit} object; (b) api/customAvatarItems/v1/isCreationAllowedForAccount — client parses BARE Boolean, server sends an object (siblings isCreationEnabled/isRenderingEnabled correctly send bare booleans); (c) api/customAvatarItems/v2/fromCreator/{id} — client expects paged {Results,TotalResults} (CKFLFJCNEPH), server returns a bare array; (d) api/equipment/v1/update — client posts a JSON ARRAY (List<BCINJINBBHG>), server binds a single object; (e) api/consumables/v1/transfer — client sends a JsonUtility JSON body, server only reads form/query; (f) api/customAvatarItems/v1/bulk — client's param key is "customAvatarItemIds", which the server's form-key whitelist does not include (works only if the client emits it as a query param); (g) saved outfits (BEPMJICDNDD) carry a 7th string (likely OutfitSelectionsV2) + CustomAvatarItems list that the server's SavedOutfitDto silently drops. The bare literals "api/avatar/", "api/consumables/", "api/customAvatarItems", "econ/customAvatarItems", "api/ugcPurchasables" are String.Format prefixes, not standalone routes. No key-literal evidence exists for GGKCJMIFLDJ (catalog SKU), NOAAFNFJPFB, CKFLFJCNEPH, JEFHGGIDKEG, NCMMAGDIBND, IHLMDLKOLNB — their key sets above come from the live-verified server wire (dual-cased where uncertain); treat unlabeled names as PLAUSIBLE, not CONFIRMED.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| GET | `api/avatar/v1/defaultbaseavataritems` | none | JSON array of the same UnlockedAvatarItem shape as v4/items |
| GET | `api/avatar/v1/defaultunlocked` | none | Same array shape as v4/items (server aliases it to the same handler) |
| GET | `api/avatar/v1/lockeditems` | Query: repeated key "desc" carrying avatar-item desc strings (list added via helper 0x181F2E250 with key literal "desc") | JSON array; server ships subset {AvatarItemType, AvatarItemDesc, FriendlyName, Tooltip, Rarity} for each requested desc the caller does NOT own |
| GET | `api/avatar/v2` | none | Object with keys (exact literals in reader EKHJKDNHOPB, case-tolerant Pascal/camel/lower): OutfitSelections(string), OutfitSelectionsV2(string), FaceFeatures(string), SkinColor(string), HairColor(string), CustomAvatarIte |
| GET | `api/avatar/v2/gifts` | none | JSON array of gift objects. Server wire (verified live, DorkNet AvatarGiftsController.ToWire): Id(Int64), FromPlayerId(Int64?), ConsumableItemDesc(string), AvatarItemType(Int32), AvatarItemDesc(string, 4-comma-part 'guid |
| POST | `api/avatar/v2/gifts/consume/` | POST to the literal URL WITH trailing slash (no id in path). Params: "Id" (boxed Int64 gift id), "UnlockedLevel" (boxed Int32) — sent form-urlencoded (server had to accept form; [F | Gift object echo (server returns ToWire(gift)); client treats non-2xx as 'Gift is missing from the list.' error |
| POST | `api/avatar/v2/gifts/generate` | Body params (exact key literals from ISIL): "GiftContext" (boxed Int32 enum IFKEEPDDNBC), "IsGameGift" (boxed Boolean), "AlternateGiftContext" (boxed Nullable<Int32>), "Message" (s | Single gift object, same shape as api/avatar/v2/gifts element |
| POST | `api/avatar/v2/set` | JSON body written by EKHJKDNHOPB writer, PascalCase keys: OutfitSelections, OutfitSelectionsV2, FaceFeatures, SkinColor, HairColor, CustomAvatarItems:[{CustomAvatarItemId, BodyPart | Client fires-and-forgets (void); server echoes AvatarV2Dto |
| GET | `api/avatar/v2/{playerId}` | none (player id is bare path segment under v2) | Same HHDLNAPEMGP object as api/avatar/v2 |
| POST | `api/avatar/v3/gifts/generate` | Body params: "GiftContext" (boxed Int32), "Message" (string, optional) | Single gift object (same as v2) |
| GET | `api/avatar/v3/saved` | none | JSON array of saved outfits. Server DTO: Slot(Int32), PreviewImageName(string), OutfitSelections(string), FaceFeatures(string), SkinColor(string), HairColor(string). NOTE: client BEPMJICDNDD has Int32 + SIX strings + Lis |
| POST | `api/avatar/v3/saved/set` | JSON body = one BEPMJICDNDD serialised via JsonConvert (ALHIJCJOLCB) then BNDIAONDFFF.FJLLPHFOOJJ; same field set as the GET element | Client ignores body (fire-and-forget task with error toast); server echoes the DTO |
| GET | `api/avatar/v4/items` | none | JSON array. Server DTO (verified live against this client): AvatarItemType(Int32), AvatarItemDesc(string, MUST have >=4 comma-parts 'guid,,,'), AvatarItemId(Int32 — must be unique+non-zero or wardrobe collapses), Platfor |
| GET | `api/catalog/v1/all?onlyAvailableSkus=true` | query baked into the literal: onlyAvailableSkus=true | JSON array of SKU objects. Exact client key set not in ISIL (reflection DTO); server ships dual-cased dictionary verified live — required keys include SkuId(Int32 — string here throws FormatException), Slug/Sku, Name, De |
| POST | `api/consumables/v1/consume` | JSON body = JsonUtility.ToJson(ConsumableItemRequest); server binds {Id(Int64), DeltaCount(Int32)} (client passes Mathf.Abs of the count) | Server returns {Id, Count}; client parse target is a string callback (Action<String>) — tolerant |
| GET | `api/consumables/v1/getTransferable/{playerId}` | none (Int32 id is a path segment — likely the prospective recipient account id) | JSON array of the same bulk-consumable shape. SERVER GAP: no route registered for api/consumables/v1/getTransferable/* (server-routes.txt has only consume/getUnlocked/transfer/updateActive) — this call 404s |
| POST | `api/consumables/v1/transfer` | JSON body = JsonUtility.ToJson(TransferConsumableRequest) — field keys UNKNOWN from ISIL. LIKELY MISMATCH: server binds [FromForm] id/recipientPlayerId/quantity with a query fallba | Server returns {Success, SourceCount, RecipientCount}; client logs 'Failed to TransferConsumable' on error |
| POST | `api/consumables/v1/updateActive` | JSON body = JsonUtility.ToJson(ActivateConsumableItemRequest). Field keys not visible in ISIL; server binds {Id(Int64), IsActive(bool)} and works live | Client ignores; error toast 'Invalid consumable' on failure. Server returns {Id, IsActive} |
| GET | `api/consumables/v2/getUnlocked` | none | JSON array of BulkConsumable groups (PascalCase, verified live): Ids(Int64[]), CreatedAts(DateTime[]), ConsumableItemDesc(string), Count(Int32), InitialCount(Int32), IsActive(bool), ActiveDurationMinutes(Int32), IsTransf |
| POST | `api/customAvatarItems/v1` | multipart/form-data: named param "metadata" (JSON string) + file part "thumbnailImage" (filename "file.bin") added via BNDIAONDFFF.BPHHLAIILHP; two Byte[] args imply a second file  | Single NOAAFNFJPFB custom-item object; server ToWire shape (used by sibling endpoints): Id/CustomAvatarItemId(Guid), CreatorId/CreatorPlayerId(Int32), Name, Description, Price(Int32), ItemType(Int32), BaseAvatarItemId(In |
| POST | `api/customAvatarItems/v1/bulk` | Param key literal "customAvatarItemIds" (guid list added via helper 0x181F2DCD0). CAUTION: server ReadIdsAsync reads form keys ids/Ids/itemIds/ItemIds and ALL query pairs, but NOT  | JSON array of NOAAFNFJPFB custom-item objects |
| GET | `api/customAvatarItems/v1/design` | none | Single design-info object (4 props per ISIL getter set). SERVER GAP: only POST design exists; this GET 405s — client logs 'Unable to get custom avatar design info' |
| PUT | `api/customAvatarItems/v1/design` | multipart: named param "metadata" + file part "design" (filename "file.bin"). SERVER GAP: design route only accepts POST — the client's PUT 405s | status only (LDGADANDBIO) |
| DELETE | `api/customAvatarItems/v1/design` | none. SERVER GAP: no DELETE on design — client logs 'Unable to delete custom avatar item design' | status only |
| GET | `api/customAvatarItems/v1/featured` | none | JSON array of custom-item objects (server ToWire shape as listed for the create endpoint) |
| GET | `api/customAvatarItems/v1/hot` | none | JSON array of custom-item objects |
| GET | `api/customAvatarItems/v1/isCreationAllowedForAccount` | none | Client expects a BARE JSON boolean. LIKELY MISMATCH: server returns object {IsCreationAllowed, Allowed} — should be bare true/false like the sibling endpoints |
| GET | `api/customAvatarItems/v1/isCreationEnabled` | none | Bare JSON boolean; server correctly returns Content("true","application/json") |
| GET | `api/customAvatarItems/v1/isRenderingEnabled` | none | Bare JSON boolean; server returns bare true |
| GET | `api/customAvatarItems/v1/me` | Query params "skip" (boxed Int32), "take" (boxed Int32) | Paged object {Results:[NOAAFNFJPFB...], TotalResults:Int32} (server also emits extra Created/Owned arrays which the client ignores) |
| GET | `api/customAvatarItems/v1/minPriceForPublicItem` | none | Object with one int field; server hedges with {MinPrice:100, Price:100} — one of the two keys presumably matches the client's property name |
| PUT | `api/customAvatarItems/v1/{id}` | JSON body via FJLLPHFOOJJ carrying the changed fields (name/description/price/publish-state). SERVER GAP: only GET is registered on v1/{id:guid}; PUT 405s | Updated NOAAFNFJPFB object |
| DELETE | `api/customAvatarItems/v1/{id}` | none (guid in path). SERVER GAP: no DELETE registered — 405 | status only (client fire-and-forget) |
| POST | `api/customAvatarItems/v1/{id}/report` | JSON body via FJLLPHFOOJJ with report reason enum + free-text (exact keys UNKNOWN — body serialised from method args). SERVER GAP: no report route registered — 404 | status only |
| GET | `api/customAvatarItems/v2/fromCreator/{creatorId}` | none beyond path id | Client expects the PAGED container CKFLFJCNEPH {Results, TotalResults}. LIKELY MISMATCH: server FromCreator returns a bare JSON array (Ok(rows.Select(ToWire))) not the paged object |
| POST | `api/equipment/v1/update` | JSON body = serialised List<BCINJINBBHG> (new List built at ISIL 038, JsonConvert via ALHIJCJOLCB then FJLLPHFOOJJ) — a JSON ARRAY of equipment objects. POSSIBLE MISMATCH: server b | Client fire-and-forget; error toast 'Failed to upload equipment slot updates'. Server returns {Updated:true} |
| GET | `api/equipment/v2/getUnlocked` | none | JSON array (verified live): PrefabName(string), ModificationGuid(string), PlatformMask(Int32), IsPlatformLocked(bool), FriendlyName(string), Tooltip(string), Rarity(Int32), Favorited(bool), Equipped(bool), IsEquipped(boo |
| POST | `api/ugcPurchasables/v1/items/bulk` | JSON body {"RoomId":N, "Ids":[{"itemType":Int32, "itemId":"guid"}]} — Ids is an array of OBJECTS, not flat guid strings (server comment documents this shape from live traffic; body | JSON array with one entry PER requested id, in request order (server synthesizes placeholders for unknown ids — missing entries make the client re-query forever and break player spawn) |
| GET | `api/ugcPurchasables/v1/items/room/{roomId}` | none beyond path roomId. SERVER GAP: only GET api/ugcPurchasables?roomId=N is registered; the /v1/items/room/{roomId} path form 404s | JSON array of ugc-purchasable objects; server wire: UgcPurchasableId/PurchasableItemId/Id(Guid), InternalId(Int64), RoomId(Int64), CreatorPlayerId(Int32), Name, Description, ImageName, Price(Int32), CurrencyType(Int32),  |
| GET | `econ/customAvatarItems/v1/itemOwnershipLimit` | none | Client's declared response type is a BARE Int32 (method returns FGLDKEJLAKB<Int32>). LIKELY MISMATCH: server returns object {Limit:1000, ItemOwnershipLimit:1000} — deserialising an object into Int32 fails; should be a ba |
| GET | `econ/customAvatarItems/v1/owned` | Query "skip", "take" | Paged {Results:[custom item wire...], TotalResults:Int32}; server econ wire per item: CustomAvatarItemId/Id(Guid), CreatorPlayerId(Int32), Name, Description, Price, ItemType, ImageName, AssetName, Color |
| POST | `econ/customAvatarItems/v1/{id}/purchase/?requestedPrice={price}` | none beyond path guid + query requestedPrice(Int32). SERVER GAP: purchase exists only under api/customAvatarItems/.../purchase; there is NO econ/customAvatarItems/v1/{id}/purchase  | JEFHGGIDKEG object, exact keys UNKNOWN; the api-side server handler returns {Success, Balance, Item} / {Success, AlreadyOwned, Item} |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/avatar/` — String.Format prefix producing api/avatar/v2/{playerId}
- `api/consumables/` — String.Format prefix producing api/consumables/v1/getTransferable/{id}
- `api/customAvatarItems` — URL composition
- `api/ugcPurchasables` — URL composition
- `econ/customAvatarItems` — URL composition

#### Defects

##### `POST api/customAvatarItems/v1` — MISSING (breaks-gameplay)

The controller's bare collection route ([Route("api/customAvatarItems/v1")] class prefix) has only [HttpGet] List — there is no [HttpPost] on the collection, so the client's multipart create (named param 'metadata' JSON + file part 'thumbnailImage', filename file.bin, plus a second binary part for the item asset) gets 405 Method Not Allowed. Publishing a custom shirt from the in-game designer is impossible. (POST design at :221 is a different route and takes flat fields, not the multipart metadata+thumbnail contract.)

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:22`

**Fix.** Add [HttpPost] Create to CustomAvatarItemsController: read Request.Form, JSON-parse the 'metadata' form field into the item fields (name/description/price/itemType/baseAvatarItemId/color/isPublic — capture the real metadata key names via request tracing on first client hit), persist Request.Form.Files parts ('thumbnailImage' → ImageName via the image store, remaining file part → AssetName), create the CustomAvatarItemEntity + creator ownership, return ToWire(item).

##### `POST api/customAvatarItems/v1/bulk` — SHAPE_MISMATCH (breaks-gameplay)

GET+POST bulk are registered, but the client's guid-list param key is 'customAvatarItemIds' (ISIL literal) and no server read path honors it as a body key: (a) [FromBody] BulkRequest only binds Ids/ItemIds, so a JSON body {"customAvatarItemIds":[...]} binds nothing; (b) the form-key whitelist at :319 is ids/Ids/itemIds/ItemIds only — and worse, a form-urlencoded POST never reaches it because the [FromBody] parameter makes ASP.NET reject non-JSON bodies with 415. Only a query-string encoding would work (ReadIdsAsync scans all query pairs at :311-314). Result: resolving custom shirts worn by other players returns [] (or 415), so remote players' shirts never render — the exact failure mode the UGC bulk endpoint's comment describes for its own past bug.

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:60`

**Fix.** Mirror UgcPurchasablesController.Bulk: drop [FromBody], add [Consumes] for json+form, read the raw JSON body case-insensitively accepting customAvatarItemIds/ids/itemIds arrays, and add 'customAvatarItemIds'/'CustomAvatarItemIds' to the form-key whitelist.

##### `PUT api/customAvatarItems/v1/design` — MISSING (breaks-gameplay)

The client saves the in-progress shirt design as PUT multipart ('metadata' param + 'design' file part, filename file.bin); the server route only accepts POST → 405, so the designer can never save the texture. Together with the missing collection POST this makes the whole shirt-designer feature non-functional.

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:221`

**Fix.** Add [HttpPut("design")] handling multipart: JSON-parse the 'metadata' form field, store the 'design' file bytes (image store / blob table) on the per-player design row, return Ok() (client return type is status-only LDGADANDBIO).

##### `POST econ/customAvatarItems/v1/{id}/purchase/?requestedPrice={price}` — MISSING (breaks-gameplay)

A working purchase handler exists ONLY under the api/customAvatarItems/{v1,v2}/{id:guid}/purchase prefixes; EconController registers no purchase route, so the client's POST to econ/customAvatarItems/v1/{id}/purchase/?requestedPrice=N 404s — buying a custom shirt is impossible. Secondary risk once routed: the api-side handler returns {Success, Balance, Item} / {Success, AlreadyOwned, Item} while the client's result DTO JEFHGGIDKEG keys are UNKNOWN — verify the first live purchase response parse.

Handler: `none (api-side sibling: DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:265)`

**Fix.** Add [HttpPost("/econ/customAvatarItems/v1/{id:guid}/purchase")] (rooted route, trailing-slash-tolerant) on CustomAvatarItemsController delegating to the existing Purchase(Guid) action; also read the requestedPrice query param and reject/adjust when it differs from item.Price to keep price-display honesty.

##### `POST api/avatar/v2/gifts/generate` — SHAPE_MISMATCH (degraded)

Route and verb exist (v2+v3), response ToWire is correct, and GenerateGiftRequest properties match the client's exact body keys (GiftContext/IsGameGift/AlternateGiftContext/Message). BUT the handler binds [FromBody] GenerateGiftRequest? (line 68), which only the JSON input formatter can read. The client builds this request with the same BNDIAONDFFF.AFGEDDANEKP named-param helper as gifts/consume, and the server's own comment on the consume handler (lines 170-184) documents that this helper produced a form-urlencoded body live and that [FromBody] returned HTTP 415 there. The [Consumes(...form-urlencoded...)] attribute on line 67 does NOT add a form formatter — a form-encoded generate body still 415s and the reward chest never spawns a gift; a JSON body works. PLAUSIBLE (not live-confirmed which encoding generate uses; consume is proven form-encoded, so generate likely is too).

Handler: `DorkNet.Server/Controllers/API/Avatar/V2/AvatarGiftsController.cs:65`

**Fix.** In AvatarGiftsController.Generate, drop [FromBody] and read the body the way ConsumeViaBody does: if Request.HasFormContentType read GiftContext/IsGameGift/AlternateGiftContext/Message from the form, else JSON-parse the raw body (case-insensitive), else fall back to query. Keep the response unchanged.

##### `POST api/avatar/v3/gifts/generate` — SHAPE_MISMATCH (degraded)

Same handler as v2 generate ([HttpPost("api/avatar/v3/gifts/generate")] on line 66), so the route/verb the 2023 client uses for reward chests exists — but it shares the exact [FromBody]-vs-form-body risk described for v2 generate (415 on a form-urlencoded body despite the Consumes attribute).

Handler: `DorkNet.Server/Controllers/API/Avatar/V2/AvatarGiftsController.cs:66`

**Fix.** Same fix as v2 generate (manual form/JSON/query body reading); one change covers both routes.

##### `GET api/avatar/v3/saved` — SHAPE_MISMATCH (degraded)

Route/verb OK (v2/v3/v4) and the array element carries Slot/PreviewImageName/OutfitSelections/FaceFeatures/SkinColor/HairColor with exact PascalCase. But SavedOutfitDto (lines 95-103) has NO OutfitSelectionsV2 and NO CustomAvatarItems, while the client outfit DTO BEPMJICDNDD holds Int32 + SIX strings + List<BGNNOMBFMLH>. The 2023 avatar model round-trips OutfitSelectionsV2 + CustomAvatarItems everywhere else (AvatarV2Dto has both), so a saved outfit restores without its V2 selections and without custom shirts — silent data loss on every save slot, no crash (Newtonsoft defaults the missing fields).

Handler: `DorkNet.Server/Controllers/API/Avatar/V3/AvatarSavedOutfitsController.cs:29`

**Fix.** Add [JsonPropertyName("OutfitSelectionsV2")] string and [JsonPropertyName("CustomAvatarItems")] List<CustomAvatarItemRefDto> to SavedOutfitDto (reuse CustomAvatarItemRefDto from AvatarV2Controller). Existing persisted JSON deserializes fine (missing props default).

##### `POST api/avatar/v3/saved/set` — SHAPE_MISMATCH (degraded)

Route/verb OK, [FromBody] binds the client's JSON body, upsert-by-Slot works and echoes the DTO. Same defect as the GET: [FromBody] SavedOutfitDto silently DROPS the client's OutfitSelectionsV2 string and CustomAvatarItems list at bind time (System.Text.Json ignores unknown members), so those parts of the outfit are never persisted. Also note SavedOutfitDto's non-nullable value binding is fine here (all strings + int).

Handler: `DorkNet.Server/Controllers/API/Avatar/V3/AvatarSavedOutfitsController.cs:42`

**Fix.** Same DTO extension as the GET; no handler change needed.

##### `GET api/consumables/v1/getTransferable/{playerId}` — MISSING (degraded)

No handler anywhere in the server registers getTransferable (grep over all Controllers confirms; InventoryController has only getUnlocked/consume/transfer/updateActive). The client's DNNDEMBIDKJ.PJJFBMAICLN GET String.Format("{0}v1/getTransferable/{1}") therefore 404s, and the gifting/transfer UI cannot list which consumables can be sent to the target player.

**Fix.** Add [HttpGet("api/consumables/v1/getTransferable/{playerId:long}")] to InventoryController returning the caller's transferable consumables in the SAME BulkConsumable group shape as GetUnlockedConsumablesV2 (List<CGCALDBHLGD> client-side), with IsTransferable=true; the path id is the prospective recipient (use it for any recipient-side filtering or ignore).

##### `POST api/consumables/v1/transfer` — SHAPE_MISMATCH (degraded)

Route/verb exist but the handler binds [FromForm] id/recipientPlayerId/quantity (lines 214-216) with only a query-string fallback and never reads a JSON body. The client serializes TransferConsumableRequest via JsonUtility.ToJson and sends it as the request body — with [ApiController]+[FromForm], an application/json request is rejected 415 before the action runs (and even if it ran, the JSON values would never populate the form params → 400 invalid_transfer). Sending a consumable to another player always fails ('Failed to TransferConsumable').

Handler: `DorkNet.Server/Controllers/API/Inventory/InventoryController.cs:212`

**Fix.** Rework TransferConsumable to read the body manually (same pattern as AvatarGiftsController.ConsumeViaBody): if form content read form keys, else JSON-parse the raw body case-insensitively accepting the plausible JsonUtility field names (id/Id, recipientPlayerId/RecipientPlayerId/destinationPlayerId, quantity/Quantity/count/Count — exact client field names are UNKNOWN from ISIL, so log unmatched bodies via request tracing to capture the real keys on first live use), keep the query fallback.

##### `POST api/equipment/v1/update` — SHAPE_MISMATCH (degraded)

Route/verb exist, but the handler binds [FromBody] EquipmentUpdateRequest — a SINGLE object — while the client ISIL shows it serializing a List<BCINJINBBHG> via JsonConvert, i.e. a JSON ARRAY body. System.Text.Json cannot bind an array into an object → model binding fails → automatic 400 → the favorite/equip change never persists (client shows 'Failed to upload equipment slot updates'). PLAUSIBLE rather than CONFIRMED: the server code comment (lines 69-73) describes live-observed behavior ('only Favorited was read... the skin never stuck') that implies some client body DID bind as a single object, which contradicts the array reading — one of the two observations is wrong, so verify with request tracing.

Handler: `DorkNet.Server/Controllers/API/Inventory/InventoryController.cs:81`

**Fix.** Bind the body as JsonElement (or read raw), accept BOTH a single object and an array of {PrefabName, ModificationGuid, Favorited, Equipped?, IsEquipped?}, and apply the existing per-item update loop to each element.

##### `PUT api/customAvatarItems/v1/{id}` — MISSING (degraded)

Only [HttpGet("{id:guid}")] exists on the id route — the client's PUT (JSON body with changed name/description/price/publish-state) 405s, so editing an existing custom shirt's metadata/price is impossible.

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:50`

**Fix.** Add [HttpPut("{id:guid}")] to CustomAvatarItemsController: load by PublicId, enforce CreatorPlayerId == caller, apply the DesignRequest-style optional fields from a leniently-parsed JSON body, return ToWire(item).

##### `DELETE api/customAvatarItems/v1/{id}` — MISSING (degraded)

No [HttpDelete] on the id route — deleting one's own custom item 405s (client is fire-and-forget, so the item just silently persists).

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:50`

**Fix.** Add [HttpDelete("{id:guid}")]: creator-only, soft-delete (add an IsDeleted flag or set IsPublic=false and remove ownership rows), return Ok().

##### `GET econ/customAvatarItems/v1/itemOwnershipLimit` — SHAPE_MISMATCH (degraded)

Handler returns the JSON OBJECT {"Limit":1000,"ItemOwnershipLimit":1000}, but the client method signature is FGLDKEJLAKB<System.Int32> — it deserializes the response body as a BARE integer. Deserializing an object into Int32 throws, so the ownership-cap check preceding a custom-shirt purchase errors out. CONFIRMED from both sides (server code + client method signature).

Handler: `DorkNet.Server/Controllers/Econ/EconController.cs:101`

**Fix.** Change CustomAvatarItemOwnershipLimit to return Content("1000", "application/json") — the same bare-scalar pattern the server already uses for isCreationEnabled.

##### `GET api/customAvatarItems/v2/fromCreator/{creatorId}` — SHAPE_MISMATCH (degraded)

Route+verb exist (fromCreator/{creatorId:long} under the v2 class prefix), but the handler returns a BARE JSON array (Ok(rows.Select(ToWire)) at :131) while the client deserializes the paged container CKFLFJCNEPH {Results, TotalResults}. Parsing an array into that object throws / yields empty — viewing another player's creations shows nothing.

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:114`

**Fix.** Wrap in the paged object like Owned(): return Ok(new { Results = rows.Select(ToWire), TotalResults = total }) with a CountAsync before Skip/Take.

##### `POST api/customAvatarItems/v1/{id}/report` — MISSING (degraded)

No report route exists on CustomAvatarItemsController (grep for '/report' finds only clubreporting/bugreporting/inventions/playerevents/PlayerReporting) — reporting a custom shirt 404s.

**Fix.** Add [HttpPost("{id:guid}/report")] that leniently parses the JSON body (reason enum + free text; exact client keys UNKNOWN — log the first live body), stores a moderation row (reuse the PlayerReporting entity or a new CustomAvatarItemReport table), returns Ok().

##### `GET api/customAvatarItems/v1/design` — MISSING (degraded)

Only [HttpPost("design")] exists; the client's GET (load saved in-progress design, response NCMMAGDIBND {Int32, Int32?, String, String}) 405s → 'Unable to get custom avatar design info' every time the designer opens. Note the existing POST design action is also semantically different from the client's contract (it creates/updates a published item from flat fields; the client never POSTs design).

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:221`

**Fix.** Add [HttpGet("design")] returning the caller's saved design record. Response key names for NCMMAGDIBND are UNKNOWN from ISIL — ship a dual-cased dictionary hedging the 4 props (e.g. BaseAvatarItemId/ItemType ints + Color/ImageName strings) and capture the real names from client behavior; requires a per-player design storage row (new entity).

##### `DELETE api/customAvatarItems/v1/design` — MISSING (degraded)

No [HttpDelete("design")] — discarding the saved shirt design 405s ('Unable to delete custom avatar item design').

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:221`

**Fix.** Add [HttpDelete("design")] that removes the caller's design row and returns Ok().

##### `GET api/customAvatarItems/v1/isCreationAllowedForAccount` — SHAPE_MISMATCH (degraded)

Handler returns the OBJECT {IsCreationAllowed, Allowed}, but the client method returns FGLDKEJLAKB<System.Boolean> — it parses the body as a BARE JSON boolean. Object-into-Boolean deserialization fails, so the can-create gate errors (likely read as not-allowed) and shirt creation is blocked at the UI gate. CONFIRMED from both sides. The two sibling flags on lines 209-215 already do it correctly with Content("true","application/json").

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:194`

**Fix.** Return Content(allowed ? "true" : "false", "application/json") from IsCreationAllowedForAccount.

##### `GET api/ugcPurchasables/v1/items/room/{roomId}` — MISSING (degraded)

The server only registers GET api/ugcPurchasables with a ?roomId= query (plus /update, /delete, /v1/items/bulk). The client's path form api/ugcPurchasables/v1/items/room/{roomId} matches nothing → 404 → a room's in-room UGC store list never loads on room join. The response shape itself (ToWire at :323-341) already matches the documented wire, so only the route is missing.

Handler: `DorkNet.Server/Controllers/API/UgcPurchasables/UgcPurchasablesController.cs:14`

**Fix.** Add [HttpGet("api/ugcPurchasables/v1/items/room/{roomId:long}")] mapped to the existing List logic (roomId from route instead of query).

##### `GET api/customAvatarItems/v1/minPriceForPublicItem` — UNKNOWN (none)

Route/verb OK; server hedges with {MinPrice:100, Price:100}. Client DTO IHLMDLKOLNB is an object with a single Int32 property whose JSON key is not recoverable from ISIL (reflection DTO) — whether either hedge key matches is UNKNOWN. If neither matches, the client silently falls back to the DTO's static default rather than crashing (int defaults to 0 under Newtonsoft missing-key semantics), so worst case is a wrong minimum-price validation, not a crash.

Handler: `DorkNet.Server/Controllers/API/CustomAvatarItems/CustomAvatarItemsController.cs:217`

**Fix.** None provable. If live testing shows the min-price gate misbehaving, dump the real key from a live Newtonsoft trace or the metadata name in the decompile and align.

### Clubs, threads, chat and comments

`social-clubs-chat`

Of 65 real client routes: ~17 fully work. 20 routes are entirely MISSING (all club-chat, room-comments CRUD, member/banned/requests lists+search, hasDisabledClubChat, clubChatEnabled, minlevel, permissions PUT, club DELETE, home-clubhouse PUT/DELETE, image DELETEs, thread favorite/moderate). 13 club write endpoints exist but bind [FromBody] JSON while the 2023 client always sends application/x-www-form-urlencoded (BNDIAONDFFF transport), so every club member-management action 415s. One systemic response-shape defect: the club details envelope (returned by details/create/clubhouse/modify/mainimage/additionalimage) sends int bitmasks for CoownerPermissions/ModeratorPermissions/MemberPermissions where the 2023 MMOCDPPONNG DTO is an object — Json.NET throws, so the club page and every club-settings save fails to parse even when the mutation succeeds. Two logic bugs: clubhouse PUT reads roomId from query so the form-posted roomId silently CLEARS the clubhouse, and requesttojoin inverts the Joinability enum (AskToJoin=2 is Forbidden, InviteOnly=1 is accepted as pending). Verified against client ISIL: comments/unreadcounts wire shape is a JSON ARRAY of UnreadRoomComments (OPELMBNJHNO.txt:856, Func<List<UnreadRoomComments>,Dictionary<Int64,UInt32>> is a client-side projection) — the server's array response is correct and the task inventory's 'JSON object map' claim is wrong; it is however a zero-count stub. members/bulk guaranteed-400s because the client only sends repeated 'id' params and never clubId.

**Client-side notes.** TRANSPORT (applies to every endpoint here): the shared request builder BNDIAONDFFF (C:\tmp\recroom-2023-03-21-isil\IsilDump\RecNet.Runtime\BNDIAONDFFF.txt; decompile C:\tmp\recnet-runtime-decomp\BNDIAONDFFF.cs) is constructed as ctor(BestHTTP.HTTPMethods verb, GJDLNNLKDIJ serviceHost, string route). Verb ints observed: 0=GET, 2=POST, 3=PUT, 4=DELETE (BestHTTP enum). Hosts: 13=Clubs (all club/* + members/bulk), 8=Chat (thread/*), 12=RoomComments (comments/*), 3=Matchmaking (clubhousesearch) — enum in recnet-runtime-decomp/GJDLNNLKDIJ.cs. CRITICAL: parameters added via AFGEDDANEKP become URL query string ONLY for GET; for POST/PUT/DELETE they are sent as an application/x-www-form-urlencoded body (HTTPUrlEncodedForm/HTTPFormBase.AddField, or multipart when files present) — see FGHNOKLDOKO branch at BNDIAONDFFF.txt:2900-3260. The 2023 client NEVER sends JSON bodies on this path; every DorkNet handler that binds [FromBody] JSON for these routes (club member ops ClubsController.cs:503-677, snooze/rename in ChatController) will 415/400 for this client. ChatController already has form-reading twins for thread/withmembers, thread, thread/{id} (ChatController.cs:438-528) — the same treatment is needed for the club write endpoints. Clubs write helpers: IKMMOCKDKAF.IBAKMFKEEDJ(desc, verb, route, Action<BNDIAONDFFF>) parses an LCLFBBPEMIH details envelope; JNFEEDLCGHH is the status-only variant (no DTO parse). SERVER GAPS (2023 client will 404/405/415): missing routes — club/{id} DELETE, club/home/me PUT+DELETE, club/{id}/mainimage DELETE, club/{id}/additionalimage/{slot} DELETE, club/{id}/clubChatEnabled PUT, club/{id}/hasDisabledClubChat GET, club/{id}/minlevel PUT, club/{id}/members GET, club/{id}/members/banned GET, club/{id}/members/requests GET, club/{id}/members/requests/search GET, club/{id}/members/search GET, club/{id}/permissions/{role} PUT, thread/club/{clubId} GET, thread/message/{id}/moderate PUT, thread/{id}/favorite PUT, comments/get/{roomId} GET, comments/create/{roomId} POST, comments/delete/{id} DELETE, comments/read/{roomId}/{id} PUT. Verb mismatches — thread/{id}/leave (client POST, server DELETE only), thread/{id}/rename (client POST form, server PUT JSON). Param mismatch — members/bulk: client sends only repeated 'id' query params (clubId never transmitted; it's only the client cache key), server demands clubId+playerIds → always 400. Shape risk — the three *Permissions keys in the club details envelope: server sends int bitmasks (ClubsController.cs:806-809) but the 2023 MMOCDPPONNG DTO is an object {ClubId:long, MembershipType:int, approveMember/banUnban/createEvent/editDetails/editPermissionSettings/postAnnouncement:bool} (keys per the PUT permissions closure; response casing unverified). RESPONSE KEY CASING caveat: 2023 DTO JSON names live in DataMember attribute blobs in global-metadata.dat and are NOT visible in the ISIL or decompile dumps; where I state keys they come from DorkNet's live-validated projections (ClubsController/ChatController) whose casing the 2023 client demonstrably accepts for the already-working endpoints — chat is camelCase (chatThreadId, chatMessageId, senderPlayerId, timeSent, contents, chatResult), clubs are PascalCase (ClubId, MemberCount, MembershipType, Clubs/TotalClubs/ContinuationToken), consistent with the per-DTO-casing memory note. MessageJson field names (Type/Version/Data) are UNOBFUSCATED in the binary and are the exact contents-string schema. Enum wire values worth persisting: MJOOBDNCHBO membership (Banned=-1, None=0, Pending_Requested=1, Pending_Invited=2, Pending_Denied=3, Member=10, Moderator=20, CoOwner=30, Creator=100 — matches ClubService constants memory), club State IACDINKNHKB (Active=0, PendingJunior=11, Moderation_* 100/101, MarkedForDelete=1000), message moderation MOFGOGFEHNN (0/11/100/101/102). Non-route literal: club_events/{0} is a player-events cache key (EMMAMFINMMJ/ENKNLDABNBB string builders), not HTTP; club/account/{0}/created, club/mine/created, club/mine/member, club/home/me double as client cache keys in IKMMOCKDKAF (GMCDPOAJOID:10948, AJDNEGBEKNI:30123-30328) but are also real GET routes.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| GET | `club/account/{0}/created` | none (accountId in path) | JSON array of Club objects. Club CLR layout (recnet-runtime-decomp/FOIJDINBPFG.cs): ObscuredLong ClubId, string Name, string Description, string MainImageName, IACDINKNHKB State (int: Active=0,PendingJunior=11,Moderation |
| GET | `club/categoryTags` | none | JSON array of strings (category tag names) |
| POST | `club/create` | application/x-www-form-urlencoded: name (string), description (string), category (string). NOT JSON — BNDIAONDFFF.FGHNOKLDOKO puts AFGEDDANEKP params into HTTPUrlEncodedForm for no | LCLFBBPEMIH envelope (recnet-runtime-decomp/LCLFBBPEMIH.cs): Club (FOIJDINBPFG), CustomTags (List<string>), AdditionalImages (List<HIKCHBLAMLP>{string imageName,int slot}), 3x role-permission objects (MMOCDPPONNG), MyMem |
| GET | `club/home/me` | none | single Club object (see club/account entry) |
| PUT | `club/home/me` | form-urlencoded: clubId (Int64) | status-only (JNFEEDLCGHH parses no DTO — IKMMOCKDKAF.txt:26503-26612) |
| DELETE | `club/home/me` | none | status-only |
| GET | `club/mine/created` | none | JSON array of Club |
| GET | `club/mine/member` | none | JSON array of Club |
| GET | `club/mostactivetoday` | none | JSON array of Club (bare list, not paged envelope) |
| GET | `club/search` | query: sort (int; MJLECOMCJCN: MemberCount_Desc=0,NewestFirst=1,OldestFirst=2,MostRecentlyUpdatedRoom=3,MostRecentlyUpdatedInvention=4), query (string), category (string), count (i | paged envelope EEMELNLCJJF: List<FOIJDINBPFG> + Int32 total + String continuationToken. Server's validated keys: Clubs, TotalClubs, ContinuationToken (ClubsController.cs:100-106) |
| GET | `club/{0}` | none | single Club object |
| DELETE | `club/{0}` | none (name arg is client-side only) | status-only |
| PUT | `club/{0}/additionalimage/{1}` | form-urlencoded: imageName (string) | LCLFBBPEMIH details envelope |
| DELETE | `club/{0}/additionalimage/{1}` | none | LCLFBBPEMIH details envelope |
| PUT | `club/{0}/clubChatEnabled` | form-urlencoded: clubChatEnabled (bool) | LCLFBBPEMIH details envelope |
| PUT | `club/{0}/clubhouse` | form-urlencoded: roomId (Int64). Server binds [FromQuery(Name="roomId")] (ClubsController.cs:271) — will miss a form-encoded roomId on PUT | LCLFBBPEMIH details envelope |
| DELETE | `club/{0}/clubhouse` | none | LCLFBBPEMIH details envelope |
| GET | `club/{0}/details` | none | LCLFBBPEMIH envelope (see club/create). MMOCDPPONNG permission object CLR layout: long ClubId, MJOOBDNCHBO MembershipType, 6 bools whose request-side key names are approveMember/banUnban/createEvent/editDetails/editPermi |
| GET | `club/{0}/hasDisabledClubChat` | none | bare JSON boolean (FGLDKEJLAKB<Boolean>) |
| PUT | `club/{0}/mainimage` | form-urlencoded: imageName (string) | LCLFBBPEMIH details envelope |
| DELETE | `club/{0}/mainimage` | none | LCLFBBPEMIH details envelope |
| GET | `club/{0}/members` | query: membershipType (int; Banned=-1,None=0,Pending_Requested=1,Pending_Invited=2,Pending_Denied=3,Member=10,Moderator=20,CoOwner=30,Creator=100), sortBy (int; Default=0,JoinDate_ | JSON array of member rows. CADEIMCFIIG CLR: int AccountId, long ClubId, MJOOBDNCHBO MembershipType, DateTime CreatedAt (keys per server's validated ToWireMembership, ClubsController.cs:836-842). MISSING on server (only / |
| POST | `club/{0}/members/acceptinvite` | none | status-only |
| POST | `club/{0}/members/acceptrequest` | form-urlencoded: accountId (Int32). Server binds [FromBody] JSON (ClubsController.cs:571) — content-type mismatch for this client | status-only |
| POST | `club/{0}/members/acceptrequests` | form-urlencoded: accountIds (repeated Int32 list) | status-only |
| POST | `club/{0}/members/ban` | form-urlencoded: accountId (Int32) | status-only |
| GET | `club/{0}/members/banned` | query: sortBy, skip, take | JSON array; ICPNBOOIDLI CLR: int AccountId, long ClubId, DateTime CreatedAt (banned-at). MISSING on server |
| PUT | `club/{0}/members/changetype` | form-urlencoded: accountId (Int32), membershipType (int enum) | status-only |
| POST | `club/{0}/members/declineinvite` | none | status-only |
| POST | `club/{0}/members/denyrequest` | form-urlencoded: accountId (Int32) | status-only |
| POST | `club/{0}/members/denyrequests` | form-urlencoded: accountIds (repeated list) | status-only |
| POST | `club/{0}/members/directJoin` | form-urlencoded: inviterAccountId (Int32), joinability (int; Open=0,InviteOnly=1,AskToJoin=2). Client short-circuits with toast "You're already a member of this club!" (IKMMOCKDKAF | status-only |
| PUT | `club/{0}/members/invite` | form-urlencoded: accountId (Int32), membershipType (int enum) | status-only |
| POST | `club/{0}/members/invitemembers` | form-urlencoded: accountIds (repeated list), bulkInviteType (int; Nearby=0,Friends=1) | status-only |
| POST | `club/{0}/members/leave` | none | status-only |
| POST | `club/{0}/members/remove` | form-urlencoded: accountId (Int32) | status-only |
| GET | `club/{0}/members/requests` | query: skip, take | JSON array; AADIHDCMEDB CLR: long (request id), int AccountId, int? (inviter account id), long ClubId, MJOOBDNCHBO MembershipType, MDFFODMAIGJ status (Invited=0,Requested=1,Denied=2), DateTime CreatedAt. Response key cas |
| GET | `club/{0}/members/requests/search` | query: parameters.name, parameters.sortBy (Default=0,RequestDate_Asc=1,RequestDate_Desc=2,Username_Asc=3,Username_Desc=4), parameters.maxCount, parameters.status (Invited=0,Request | paged envelope EDFOCLNECPM: List<AADIHDCMEDB> + Int32 total + String continuationToken (key casing UNKNOWN). MISSING on server |
| PUT | `club/{0}/members/requesttojoin` | none | status-only |
| GET | `club/{0}/members/search` | query: parameters.name, parameters.type (membership enum int), parameters.sortBy, parameters.maxCount, continuationToken (unprefixed) | paged envelope MBMNHFAPFCJ: List<CADEIMCFIIG> + Int32 total + String continuationToken (key casing UNKNOWN). MISSING on server |
| POST | `club/{0}/members/unban` | form-urlencoded: accountId (Int32) | status-only |
| GET | `club/{0}/members/{1}` | none | single membership row: AccountId (int), ClubId (long), MembershipType (int), CreatedAt (DateTime) — server-validated keys ClubsController.cs:836-842 |
| PUT | `club/{0}/minlevel` | form-urlencoded: minLevel (Int32) | LCLFBBPEMIH details envelope |
| PUT | `club/{0}/modify` | form-urlencoded: name, description, category (strings) | LCLFBBPEMIH details envelope |
| PUT | `club/{0}/modifydetails` | form-urlencoded: allowJuniors (bool), customTags (list), joinability (int), visibility (int; Private=0,Public=1) | LCLFBBPEMIH details envelope |
| PUT | `club/{0}/permissions/{1}` | form-urlencoded: approveMember, banUnban, createEvent, editDetails, editPermissionSettings, postAnnouncement (6 bools) | LCLFBBPEMIH details envelope |
| GET | `clubhousesearch/mostactivenow` | none | JSON array; OEJFMLMJINJ CLR: long (RoomId — same field name EEFNKAFLPLG as RoomComment.RoomId), long ClubId (field AAGOEEGKGJL, the ClubId name across club DTOs), int (player/active count, key UNKNOWN). Sent to Matchmaki |
| POST | `comments/create/{0}` | form-urlencoded: message (string), subRoomId (Int64), style (int; Feedback=0,Idea=1), positionX, positionY, positionZ (floats). RoomComments host (r8=12) | single RoomComment. CLR (recnet-runtime-decomp/RecNet/RoomComment.cs): long CommentId, long RoomId, long? SubRoomId, int? AccountId, DateTime CreatedAt, Vector3? position, string message, EKHGDABMJOJ style, 2 bools. JSON |
| DELETE | `comments/delete/{0}` | none | status-only ('Failed to delete comment'). MISSING on server |
| GET | `comments/get/{0}` | query: count (Int32), minId (Int64) | JSON array of RoomComment. MISSING on server |
| PUT | `comments/read/{0}/{1}` | none | status-only ('Failed to update latest read comment'). MISSING on server |
| GET | `comments/unreadcounts` | roomIds (repeated Int64 list). Verb is dynamic: GET when <100 room ids (query), POST with form body when >=100 (verb = count<100 ? 0 : 2 — cmovl at instr 059-061). Server registers | JSON object mapping roomId (stringified Int64 key) -> unread count (UInt32) |
| GET | `members/bulk` | query: id (repeated, one per account). clubId is NOT transmitted — it is only the client-side cache key (response handler NJNJEOFNJIG(clubId, List<FCKGOFHNDNJ>) at IKMMOCKDKAF.txt: | JSON array; FCKGOFHNDNJ CLR: int AccountId, MJOOBDNCHBO MembershipType |
| GET | `thread/club/{0}` | query: maxCount (int, default 10), mode (int, semantics UNKNOWN — client always sends 0). Chat host (GJDLNNLKDIJ.Chat=8) | JSON array of chat threads. The thread reader (EKEFFNBIOHJ.txt instrs 082/109/133/149/173/197/221/245/269) takes exactly nine keys in three casings: ChatThreadId, LastReadMessageId, Messages, LatestMessage, PlayerIds, ChatThreadName, SnoozedUntil, IsFavorited, ClubId. IMPLEMENTED: `ChatController.GetClubThreads` — one `club:{clubId}` thread per club, roster-gated, returned as a one-element array |
| PUT | `thread/message/{0}/moderate` | form-urlencoded: moderationState (byte enum: Active=0,Junior_Pending=11,Moderation_Pending=100,Moderation_Closed=101,Moderation_Banned=102) | chat-result enum MGGNHLHPHOF (Success=0,InvalidArguments,ThreadNotFound,MembershipNotFound,PlayerAlreadyOnThread,CannotMessagePlayer,InvalidCharacters,RecentlyLeftThread,ThreadTooLarge) — bare value (`Action<MGGNHLHPHOF>` at DLDKCILCKNA.txt instr 101). STILL MISSING on server — blocked on a `ChatMessageEntity.ModerationState` column |
| POST | `thread/withmembers` | form-urlencoded: ids (repeated Int32 list), messageCount (int). Server has a form-reading variant (ChatController.cs:438-446) — matches | single thread object (get-or-create DM/group with these members) |
| GET | `thread/{0}` | query: messageCount (int) | single thread object with messages |
| POST | `thread/{0}` | form-urlencoded: messageContents = JSON string of DLDKCILCKNA.MessageJson with PRESERVED field names {Type (int: Text=0,PartyInvite=1), Version (int), Data (string)} (recnet-runtim | ANDECIOOJNB: {ChatMessage, chat-result enum}. Server's validated keys: chatMessage, chatResult; message keys: chatMessageId, chatThreadId, senderPlayerId, timeSent, contents (ChatController.cs:792-810) |
| PUT | `thread/{0}/favorite` | form-urlencoded: favorite (bool) | NOT a bare enum — the continuation is `Func<GLIKNMPPEHL, MGGNHLHPHOF>` (DLDKCILCKNA.txt instr 142), so the body is the wrapper object GLIKNMPPEHL, read by OGCMIDEFOEE.txt (instrs 042/069) with exactly two keys in three casings: ChatResult, IsFavorited. IMPLEMENTED: `ChatController.Favorite` — flag persisted in PlayerSettings under `chat.favorite:<threadKey>` and echoed back in the thread DTO's IsFavorited |
| POST | `thread/{0}/leave` | none | chat-result enum. VERB MISMATCH: server registers HttpDelete only (ChatController.cs:337) → 405 for this client's POST |
| POST | `thread/{0}/member/{1}` | none (both ids in path) | chat-result enum (PlayerAlreadyOnThread etc.). Server POST exists (ChatController.cs:673) |
| GET | `thread/{0}/message` | query: messageCount (int), mode (int, semantics UNKNOWN), referenceMessageId (Int64) | JSON array of ChatMessage. CLR (recnet-runtime-decomp/RecNet/ChatMessage.cs): long MessageId, long ThreadId, int SenderAccountId, DateTime SentAt, string contents, MOFGOGFEHNN moderation state, MessageJson parsed content |
| POST | `thread/{0}/message/{1}/read` | none | chat-result enum. Server accepts POST+PUT (ChatController.cs:268-269) — OK |
| POST | `thread/{0}/rename` | form-urlencoded: name (string) | chat-result enum, bare value (`Action<MGGNHLHPHOF>` at DLDKCILCKNA.txt instr 162). FIXED: `ChatController.Rename` now registers PUT+POST and binds via FormOrJsonModelBinder |
| POST | `thread/{0}/snooze` | form-urlencoded: snooze (bool) | chat-result enum. ENCODING MISMATCH: server POST exists but binds [FromBody] SnoozeRequest JSON (ChatController.cs:307-308) → 415 on form content-type |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `club_events/{0}` — Player-events cache layer, keyed per club

#### Defects

##### `POST club/create` — SHAPE_MISMATCH (breaks-gameplay)

Request side is fine (ReadCreateClubRequestAsync at :888-912 reads form keys name/description/category). But the response is BuildDetailsResponseAsync (:794-808) whose CoownerPermissions/ModeratorPermissions/MemberPermissions are int bitmasks (PermissionsForRole, :819-826) while the 2023 LCLFBBPEMIH DTO declares three MMOCDPPONNG OBJECT fields (long ClubId, MJOOBDNCHBO MembershipType, 6 bools — verified C:/tmp/recnet-runtime-decomp/LCLFBBPEMIH.cs:64-115, MMOCDPPONNG.cs:16-157). Json.NET deserializing an integer into a complex object throws, so club creation appears to fail client-side even though the club row is created.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:338`

**Fix.** In BuildDetailsResponseAsync emit each *Permissions key as an object {ClubId, MembershipType, approveMember, banUnban, createEvent, editDetails, editPermissionSettings, postAnnouncement} (key casing of the 6 bools per the PUT permissions request closure; response casing UNKNOWN — mirror request casing and validate live). Applies to all 8 envelope-returning handlers at once.

##### `PUT club/home/me` — FIXED

`ClubsController.HomeMeSet` reads the form-urlencoded `clubId` (query fallback) and stores it through `ClubService.SetHomeClubAsync`, which persists into the general-purpose `PlayerSettings` key/value bag under key `club.home` — no new column needed. Returns an empty 200; the client's `LDGADANDBIO JOPMBFIFFBB(Nullable<Int64>)` parses no DTO. `HomeClubAsync` now prefers the stored pick and only falls back to most-recently-joined when nothing is stored or the stored club was disbanded.

##### `DELETE club/home/me` — FIXED

`ClubsController.HomeMeClear` removes the `club.home` `PlayerSettings` row (idempotent) and returns an empty 200.

##### `DELETE club/{0}` — FIXED

`ClubsController.ClubDelete` → `ClubService.DisbandAsync`: owner-only (co-owners/moderators are rejected, unlike `ModifyAsync`), soft delete stamping `State = 1000` (`IACDINKNHKB.MarkedForDelete`). Every read path here filters `State == 0`, so the club disappears from browse/mine/details while its announcements and membership rows survive for moderation. Returns an empty 200.

##### `PUT club/{0}/clubChatEnabled` — MISSING (breaks-gameplay) — BLOCKED ON SCHEMA

No route → 404. Club-chat toggle in settings fails. Verified from the binary: `MEPEAAJOGLP(Int64, Boolean)` at IKMMOCKDKAF.txt:25601, route literal :25779, verb ordinal 3 (PUT) at :25802, form key `clubChatEnabled` (IKMMOCKDKAF_NestedType_BLCIHIILJNE.txt:67), response is the `LCLFBBPEMIH` details envelope. Deliberately NOT stubbed: a handler that accepted the toggle and dropped it would make the settings screen lie, and `GET club/{0}/hasDisabledClubChat` (now served) would keep answering `false`.

**Fix.** Needs `ClubEntity.ClubChatEnabled` (bool, default true). Once the column exists: `[HttpPut("/club/{clubId:long}/clubChatEnabled")]` reading the form `clubChatEnabled` through `FormOrJsonModelBinder`, write via `ModifyAsync`, return `BuildDetailsResponseAsync`, and update BOTH `ToWireClub`'s `ClubChatEnabled = true` literal and `ClubsController.ClubHasDisabledChat`'s `false`.

##### `PUT club/{0}/clubhouse` — SHAPE_MISMATCH (breaks-gameplay)

Route+verb exist, but the handler binds [FromQuery(Name="roomId")] (:271) while the 2023 client sends roomId in the form-urlencoded BODY. roomId binds null, and the PUT branch then executes c.ClubhouseRoomId = null (:277-278) — setting a clubhouse silently CLEARS it instead. Response envelope also hits the *Permissions int defect.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:269`

**Fix.** In ClubClubhouse, when Request.HasFormContentType also parse Request.Form["roomId"] and prefer it over the query value before mutating.

##### `GET club/{0}/details` — SHAPE_MISMATCH (breaks-gameplay)

All 7 keys present (Club, CustomTags, AdditionalImages, CoownerPermissions, ModeratorPermissions, MemberPermissions, MyMembershipType) but the three *Permissions values are ints (0x7FFE/0x00FF/0x0007 from PermissionsForRole :819-826) whereas 2023's LCLFBBPEMIH fields JNOKOILEFHD/OMMHPBFEHBH/IJKKPOBDJGG are MMOCDPPONNG OBJECTS (LCLFBBPEMIH.cs:64-115). Strict Json.NET read of int-into-object throws → the whole club page load fails. CustomTags/AdditionalImages are always empty (not persisted) — cosmetic on top.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:258`

**Fix.** Emit MMOCDPPONNG-shaped objects for the three permission keys (see club/create finding); persist CustomTags/AdditionalImages later.

##### `GET club/{0}/hasDisabledClubChat` — FIXED (pending schema)

`ClubsController.ClubHasDisabledChat` 404s an unknown/disbanded club and otherwise answers a bare JSON `false` (the issuing method is `FGLDKEJLAKB<System.Boolean> OFOJAHBKMOJ(Int64)` — a bare boolean body, not an object). `false` is the *true* answer today: the value is the negation of the Club wire field `ClubChatEnabled`, which has no `ClubEntity` column and whose setter (`PUT club/{0}/clubChatEnabled`) is therefore still unimplemented, so no club can be in the disabled state. When the column lands, this handler and `ToWireClub`'s `ClubChatEnabled` literal must change together.

##### `PUT club/{0}/mainimage` — SHAPE_MISMATCH (breaks-gameplay)

Handler binds [FromBody] MainImageRequest (JSON) at :399; the 2023 client posts form-urlencoded imageName → 415 Unsupported Media Type before the action runs. 'Modify Club Image' always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:396`

**Fix.** Read Request.Form["imageName"] when HasFormContentType (mirror ReadCreateClubRequestAsync pattern), keep JSON path for other callers.

##### `GET club/{0}/members` — FIXED

`ClubsController.ClubMembers` binds `membershipType`/`sortBy`/`skip`/`take` and returns the bare `List<CADEIMCFIIG>` array via the existing `ToWireMembership` projection. `membershipType` is compared as the MJOOBDNCHBO wire value (after `MembershipTypeFromPerms`), not the stored perms int; with no filter the roster is real members only, so ban markers (256) and pending rows (128) stay in their own endpoints. `sortBy` implements GNLOJEONFIG (Default=privileged-first, JoinDate_Asc/Desc, Username_Asc/Desc) — usernames are joined in from `Players` once per request.

##### `POST club/{0}/members/acceptrequest` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] MemberTargetRequest JSON binding (:571) vs client's form-urlencoded accountId → 415. Approving a join request always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:569`

**Fix.** Add form support: when Request.HasFormContentType read accountId from Request.Form (a shared helper for all member ops); property name AccountId already aligns.

##### `POST club/{0}/members/acceptrequests` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] BulkTargetRequest JSON vs client's repeated form accountIds → 415. 'Approve all' fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:589`

**Fix.** Read repeated Request.Form["accountIds"] values when form content-type.

##### `POST club/{0}/members/ban` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] JSON vs form accountId → 415. Ban always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:644`

**Fix.** Same form-reading helper.

##### `PUT club/{0}/members/changetype` — SHAPE_MISMATCH (breaks-gameplay)

PUT is registered (:675) but [FromBody] JSON binding vs client's form accountId+membershipType → 415. Promote/demote always fails. (Wire enum handling via PermsFromMembershipType is otherwise correct: Member=10/Moderator=20/CoOwner=30 map to 0/24/124, ClubService.cs:366-372.)

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:674`

**Fix.** Form-reading helper for accountId + membershipType.

##### `POST club/{0}/members/denyrequest` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] JSON vs form accountId → 415.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:604`

**Fix.** Form-reading helper.

##### `POST club/{0}/members/denyrequests` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] JSON vs repeated form accountIds → 415.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:615`

**Fix.** Form-reading helper.

##### `PUT club/{0}/members/invite` — SHAPE_MISMATCH (breaks-gameplay)

PUT registered (:504) but [FromBody] MemberInviteRequest JSON vs client's form accountId+membershipType → 415. Inviting a player always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:503`

**Fix.** Form-reading helper.

##### `POST club/{0}/members/invitemembers` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] InviteMembersRequest JSON vs client's repeated form accountIds + bulkInviteType → 415. Bulk invite fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:525`

**Fix.** Form-reading helper (accountIds repeated; bulkInviteType may be ignored).

##### `POST club/{0}/members/remove` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] JSON vs form accountId → 415. Kick always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:626`

**Fix.** Form-reading helper.

##### `PUT club/{0}/members/requesttojoin` — SHAPE_MISMATCH (breaks-gameplay)

Verb OK (POST+PUT registered, no body needed) but the Joinability switch is inverted vs the client enum LNJLEKOPAHB (Open=0, InviteOnly=1, AskToJoin=2): server maps 1→pending and everything else→Forbid (:453-459). So the 'Ask to join' button on AskToJoin(2) clubs always gets 403, while InviteOnly(1) clubs wrongly accept join requests as pending.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:446`

**Fix.** Change the switch: 0→instant member, 2→pending(128), 1(InviteOnly)→Forbid. Audit other Joinability comparisons in ClubService for the same inversion.

##### `POST club/{0}/members/unban` — SHAPE_MISMATCH (breaks-gameplay)

[FromBody] JSON vs form accountId → 415. Unban always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:659`

**Fix.** Form-reading helper.

##### `PUT club/{0}/modify` — SHAPE_MISMATCH (breaks-gameplay)

PUT registered but [FromBody] ModifyClubRequest JSON (:371) vs client's form name/description/category → 415. Editing club name/description/category always fails.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:366`

**Fix.** Read form fields when HasFormContentType (extend ReadCreateClubRequestAsync pattern to modify).

##### `PUT club/{0}/modifydetails` — SHAPE_MISMATCH (breaks-gameplay)

Same handler as /modify: [FromBody] JSON → 415 on the client's form allowJuniors/customTags/joinability/visibility. Additionally ModifyClubRequest has no CustomTags member and tags are never persisted.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:368`

**Fix.** Form-read allowJuniors (bool), joinability (int), visibility (int), repeated customTags; persist CustomTags so the details envelope can echo them.

##### `GET members/bulk` — SHAPE_MISMATCH (breaks-gameplay)

Client sends ONLY repeated 'id' query params (clubId is a client-side cache key, never transmitted). Server hard-requires a clubId param (:702-704 → 400) and reads ids from 'playerIds' (:706-708), so even with clubId it would collect zero ids. Every call is a guaranteed 400 → membership badges on nameplates never resolve.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:697`

**Fix.** Accept repeated 'id' query params; when clubId is absent, return each account's membership without club scoping (e.g. highest/most-relevant membership per account) or infer from context — response stays [{AccountId, MembershipType}].

##### `GET thread/club/{0}` — FIXED (was breaks-gameplay)

'thread/club/123' (3 segments) matched no template — thread/{chatThreadId} is 2 segments — → 404. The club chat tab could not open at all.

**Fixed.** `ChatController.GetClubThreads` — `[HttpGet("/thread/club/{clubId:long}")]`, get-or-creates the `club:{clubId}` thread (named after the club on first materialisation), honours `maxCount`, accepts and ignores `mode`, and returns a one-element JSON ARRAY of ToWireThread objects. Non-roster callers and unknown clubs get `[]` (the client deserialises the body before it looks at the status). The thread helpers now understand `club:` keys: `LoadParticipantsAsync` resolves participants from ClubMemberships (pending 128 / banned 256 excluded), `ThreadRecipientsAsync` fans new messages out over the roster, `IsThreadMemberAsync` gates rename/favorite on roster membership, `Leave` refuses club threads, and `GetThreads` lists club channels that have messages. ToWireThread now also emits the two keys the 2023 reader wants and the 2020 DTO lacked: `isFavorited` and `clubId`.

##### `POST thread/{0}/leave` — VERB_MISMATCH (breaks-gameplay)

Server registers HttpDelete only; the 2023 client sends POST → 405. Leaving a group chat is impossible.

Handler: `DorkNet.Server/Controllers/Chat/ChatController.cs:337`

**Fix.** Add [HttpPost("/thread/{chatThreadId}/leave")] on the same action.

##### `PUT club/{0}/additionalimage/{1}` — STUB (degraded)

Two defects: (1) handler binds [FromBody] MainImageRequest JSON but the 2023 client sends form-urlencoded imageName → ASP.NET [ApiController] returns 415 before the handler runs; (2) even reachable, the handler is an admitted stub — it never persists the slot (comment at :411-416, 'gallery renders blank') and just re-emits current details. Response envelope also carries the *Permissions int defect.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:417`

**Fix.** Persist additional image slots (new AdditionalImages storage keyed clubId+slot), read Request.Form["imageName"] when HasFormContentType, echo them in the envelope's AdditionalImages as [{imageName,slot}] per HIKCHBLAMLP.

##### `DELETE club/{0}/additionalimage/{1}` — MISSING (degraded) — BLOCKED ON SCHEMA

Only POST/PUT registered → DELETE 405. 'Remove Additional Club Image' fails. Verified from the binary: `AKJJLMLPBKN(Int64, Int32 slot, String imageName)` at IKMMOCKDKAF.txt:16054 branches on the captured imageName (:16219) — verb ordinal 4 with no body when null (description "Remove Additional Club Image", :16229-16234), verb 3 with the form field otherwise (:16256-16260) — and both branches parse the `LCLFBBPEMIH` details envelope.

Deliberately NOT registered: the PUT twin is already an admitted stub that persists nothing, so a DELETE would have nothing to clear. Registering it would only convert a visible 405 into a silent success while the gallery stays blank.

**Fix.** Needs the additional-image store the PUT twin also wants: a `ClubAdditionalImageEntity` keyed (ClubId, Slot) with an ImageName column. Then register `[HttpDelete("/club/{clubId:long}/additionalimage/{slot:int}")]`, delete the row, and have `BuildDetailsResponseAsync` project the rows into the envelope's `AdditionalImages` as `HIKCHBLAMLP` {imageName, slot} instead of the current `Array.Empty<object>()`.

##### `DELETE club/{0}/mainimage` — FIXED

`ClubsController.ClubMainImageDelete` is its own action (NOT a third verb binding on the POST/PUT setter — aliasing an edit verb onto a destructive handler is exactly what made `PUT /announcements/club/{c}/{a}` delete posts). Clears `ImageName` through `ModifyAsync` and returns the full `LCLFBBPEMIH` details envelope, which is what `IBAKMFKEEDJ` parses on both branches of `BLEDOCHHHJM` — the DELETE branch is *not* status-only.

##### `GET club/{0}/members/banned` — FIXED

`ClubsController.ClubMembersBanned` returns the bare `List<ICPNBOOIDLI>` array — three fields only ({AccountId:int, ClubId:long, CreatedAt}; ICPNBOOIDLI has no MembershipType) — over rows carrying the perms 256 ban marker, with `sortBy`/`skip`/`take`. Moderator-gated (`CanManageAsync`): the ban list is staff-only information.

##### `GET club/{0}/members/requests` — FIXED

`ClubsController.ClubMemberRequests` returns the bare `List<AADIHDCMEDB>` array over rows with the 128 pending marker (and without the 256 ban marker), `skip`/`take`, moderator-gated. Two fields are still un-fillable rather than invented: `InviterAccountId` is emitted as null (the DTO declares it nullable; invites and requests both collapse to the single 128 marker on the target's own row, so no inviter is recorded) and `Status` is always Requested for the same reason. Neither is read by the accept/deny flow, which keys off `accountId`. Persisting invited-vs-requested would let `Status` be filled properly.

##### `GET club/{0}/members/requests/search` — FIXED

`ClubsController.ClubMemberRequestsSearch` reads the dot-prefixed keys straight off `Request.Query` (`parameters.name`, `parameters.sortBy`, `parameters.maxCount`, `parameters.status`, plus the unprefixed `continuationToken`) and returns the `EDFOCLNECPM` paged envelope. `continuationToken` is an opaque row offset (String on the DTO); an empty string means no further pages, matching the `club/search` envelope. NAHBJCGNJKA's RequestDate_Asc/Desc index the same JoinedAt column GNLOJEONFIG's JoinDate_Asc/Desc do, so both enums share one comparator. Envelope key names remain unverified (DataMember blobs are not in the ISIL/decompile), so it emits the entity-named pair validated on the sibling `club/search` envelope — `Requests`/`TotalRequests` — alongside `Results`/`TotalResults`; Json.NET ignores whichever pair the DTO doesn't declare.

##### `GET club/{0}/members/search` — FIXED

`ClubsController.ClubMembersSearch`, same treatment: `parameters.name`/`parameters.type`/`parameters.sortBy`/`parameters.maxCount` + `continuationToken` off `Request.Query`, returning the `MBMNHFAPFCJ` paged envelope with `Members`/`TotalMembers` plus `Results`/`TotalResults` and `ContinuationToken`.

##### `PUT club/{0}/minlevel` — MISSING (degraded) — BLOCKED ON SCHEMA

No club minlevel route (only Rooms.MinLevel exists) → 404. 'Min Level to Join' setting fails. Verified from the binary: `AOAFDLMFCGB(Int64, Int32)` at IKMMOCKDKAF.txt:16448, route literal :16561, verb ordinal 3 (PUT) at :16584, form key `minLevel` (IKMMOCKDKAF_NestedType_AOOCOABJPDN.txt:67), response is the `LCLFBBPEMIH` details envelope via IBAKMFKEEDJ.

**Fix.** Needs `ClubEntity.MinLevel` (int, default 0). Once the column exists: `[HttpPut("/club/{clubId:long}/minlevel")]` reading the form `minLevel` through `FormOrJsonModelBinder`, write via `ModifyAsync`, return `BuildDetailsResponseAsync`, and swap `ToWireClub`'s `MinLevel = 0` literal for the column.

##### `PUT club/{0}/permissions/{1}` — MISSING (degraded)

Only HttpGet is registered → PUT 405. Per-role permission checklist saves fail. BLOCKED ON SCHEMA. Verified from the binary: `HDDNEPNKCIK(MMOCDPPONNG)` at IKMMOCKDKAF.txt:17537 formats the route from the DTO's own ClubId + MembershipType (:17671-17689), verb ordinal 3 (PUT) at :17709, description "Modify Club Permissions", response is the `LCLFBBPEMIH` details envelope. Form keys are the 6 bools `editDetails`/`approveMember`/`createEvent`/`postAnnouncement`/`editPermissionSettings`/`banUnban` (IKMMOCKDKAF_NestedType_BOIMHOCCOEI.txt:173-253), matching the response-side reader BPHCOIBNCDP.txt:638-758.

Deliberately NOT stubbed: `PermissionsForRole` derives the six bools from the role with a fixed default policy and there is nowhere to store an override, so a handler would accept the checklist and silently discard it — the screen would appear to save and revert on reload.

**Fix.** Needs a per-role permission table (ClubId, MembershipType, the 6 bools). Then register `[HttpPut("/club/{clubId:long}/permissions/{role:int}")]` binding those 6 form keys through `FormOrJsonModelBinder`, upsert the row owner-only, and have `PermissionsForRole` read the stored row (falling back to the current derived default when absent) so `BuildDetailsResponseAsync` reflects the save.

##### `POST comments/create/{0}` — MISSING (degraded)

No comments/create route (grep zero hits; only /comments/unreadcounts and an unrelated rooms/{id}/comments exist) → 404. Posting room feedback/idea comments fails.

**Fix.** Add RoomComments CRUD to CommentsController: [HttpPost("/comments/create/{roomId:long}")] reading form message/subRoomId/style/positionX/Y/Z, persist a RoomCommentEntity, return a single RoomComment object (CommentId, RoomId, SubRoomId?, AccountId?, CreatedAt, position?, message, style, 2 bools — key casing UNKNOWN, dual-case via Dictionary and validate live).

##### `DELETE comments/delete/{0}` — MISSING (degraded)

No route → 404. Deleting a comment fails.

**Fix.** Add [HttpDelete("/comments/delete/{commentId:long}")], status-only 200.

##### `GET comments/get/{0}` — MISSING (degraded)

No route → 404. Room-details comment list never loads ('Failed to get room comments').

**Fix.** Add [HttpGet("/comments/get/{roomId:long}")] with query count (int) + minId (long), returning JSON array of RoomComment.

##### `PUT thread/message/{0}/moderate` — MISSING (degraded) — BLOCKED ON SCHEMA

4-segment path matches no thread template → 404. Chat moderation (hide/flag) fails.

Verb + shape re-verified from the binary: DLDKCILCKNA.txt `PLBCHNGAEML(ChatMessage, MOFGOGFEHNN)`, instr 065 `Move rcx, "thread/message/{0}/moderate"`, verb `Move rdx, 3` at instr 075 (PUT), host `Move r8, 8` (Chat), single form field `moderationState` at instr 087, response consumed by `Action<MGGNHLHPHOF>` at instr 101 = bare int.

**Fix.** Add `ChatMessageEntity.ModerationState` (int, default 0 = Active) + migration, emit it as the `ModerationState` key in `ChatController.ToWireMessage` (the ChatMessage reader CBKDKACJDOF.txt reads ChatMessageId / ChatThreadId / SenderPlayerId / TimeSent / Contents / ModerationState), then add `[HttpPut("/thread/message/{messageId:long}/moderate")]` reading form `moderationState` and returning bare int 0. Deliberately NOT stubbed: without the column the handler could only discard the request, which hides the gap behind a 200.

##### `POST thread/{0}/rename` — FIXED (was verb_mismatch, degraded)

Server registered PUT only with [FromBody] RenameRequest JSON; the client sends POST with form 'name' → 405 (and would 415 even as PUT). Renaming a group chat failed.

Handler: `DorkNet.Server/Controllers/Chat/ChatController.cs` — `ChatController.Rename`

**Fixed.** Route now carries both `[HttpPut]` and `[HttpPost]`; `RenameRequest` became a plain class with a parameterless ctor bound by `[ModelBinder(typeof(FormOrJsonModelBinder))]`, so the client's form `name` and the admin SPA's JSON both bind. Still returns the bare int (`Action<MGGNHLHPHOF>` at DLDKCILCKNA.txt instr 162). Membership is now checked through `IsThreadMemberAsync`, which also understands club threads.

##### `POST thread/{0}/snooze` — SHAPE_MISMATCH (degraded)

Three defects for the 2023 client: (1) [FromBody] SnoozeRequest JSON → 415 on the form content-type; (2) the client's field is 'snooze' (bool), not an Until date — never bound; (3) the response is an object {chatResult, snoozedUntil} (:325-329) while the 2023 client deserializes a bare MGGNHLHPHOF enum — object-into-int throws even after the 415 is fixed.

Handler: `DorkNet.Server/Controllers/Chat/ChatController.cs:307`

**Fix.** Add a form-consuming variant: read Request.Form["snooze"] bool, set SnoozeUntil = snooze ? DateTime.MaxValue (or a policy window) : null, and return bare int 0; keep the JSON+object variant for the 2020 watch on the JSON content-type.

##### `PUT comments/read/{0}/{1}` — MISSING (cosmetic)

No route → 404. Unread-comment badge never clears.

**Fix.** Add [HttpPut("/comments/read/{roomId:long}/{commentId:long}")] persisting per-player last-read comment id (also lets comments/unreadcounts return real counts), status-only 200.

##### `GET comments/unreadcounts` — STUB (cosmetic)

Route exists for both GET and POST with form+JSON+query parsing of roomIds — matches the client's dynamic verb (GET <100 ids, POST >=100). Wire shape verified correct against the client binary: the response IS a JSON array — OPELMBNJHNO.txt:856 (instr 109) constructs Func<List<UnreadRoomComments>, Dictionary<Int64,UInt32>>, i.e. the dictionary in the method signature is a client-side projection; the task inventory's 'JSON object map' claim is wrong. Element key names are attribute-driven/UNKNOWN; server hedges with RoomId/UnreadCount/Count aliases (Json.NET matches case-insensitively). Defect: counts are hardcoded 0 (no read-state tracking) so the unread badge never appears.

Handler: `DorkNet.Server/Controllers/API/Comments/CommentsController.cs:40`

**Fix.** Once comments/read persists per-player read state, compute real counts (max comment id per room vs player's last-read).

##### `PUT thread/{0}/favorite` — FIXED (was cosmetic)

No route → 404. Starring a thread failed.

**Fixed.** `ChatController.Favorite` — `[HttpPut]`+`[HttpPost]` on `/thread/{chatThreadId}/favorite`, form field `favorite` bound with FormOrJsonModelBinder. The response is NOT a bare int as previously recorded: the client's continuation is `Func<GLIKNMPPEHL, MGGNHLHPHOF>` (DLDKCILCKNA.txt instr 142), i.e. a client-side projection off a wrapper object, so the handler returns `{chatResult, isFavorited}` (reader OGCMIDEFOEE.txt, both keys casing-tolerant). The flag is per-(player, thread) and there is no column for it, so it is persisted in the generic PlayerSettings table under `chat.favorite:<threadKey>` and read back into every thread DTO's `IsFavorited` via `LoadFavoritesAsync`. Unknown threads answer ThreadNotFound(2), non-members MembershipNotFound(3).

### Relationships, friends and messaging

`relationships-messaging`

Audited all 20 real HTTP routes of the relationships-messaging subsystem against DorkNet (RelationshipsController.cs, MessagesController.cs, ExternalFriendInviteController.cs; global JSON options at ServiceCollectionExtensions.cs:380-381 confirm PascalCase-verbatim serialization and NO AllowReadingFromString). 11 routes match the 2023-03-21 client exactly (v2/get, v2/addfriend, v2/removefriend, v2/sendfriendrequest, v2/acceptfriendrequest, v1/addfriendwithcode, v1 mute/unmute/ignore/unignore, messages v2/get, v2/send, v3/delete, favoriteFriendOnlineStatus). 9 defects: (worst) both externalfriendinvite referrer endpoints return object arrays where the client requires bare [Int32,...] — a hard Utf8Json fault at login bootstrap once a player has any stored invite; sendMultiple 400s whenever the client attaches a room because RoomId arrives as a JSON string and AllowReadingFromString is unset; sendfriendintroductions binds [FromForm] against a JSON body and always 400s; bulkignoreplatformusers reads key PlatformUserIds but the client sends PlatformIds (silent zero-row import); mutualfriends binds playerId instead of id AND returns account objects instead of Relationship objects (always-empty mutual-friends UI); createplatforminvite's response lacks Success/Error so the client always sees failure; sendtextmessageinvite drops friendCode/senderName; favorite/unfavorite return RecNetResult instead of a Relationship (non-fatal); offlineinvite/v1/send is fully mismatched but has zero call sites in this build. Cross-cutting constraint: ECGNEHMCGCN.PlayerID and KJECOLODAFA.FromPlayerId are ReadInt32 on this client — player ids must stay <= Int32.MaxValue.

**Client-side notes.** HOW I READ THE WIRE SHAPES. Response DTO JSON keys are NOT il2cpp string literals in the DTO types — this build serializes with a code-generated Utf8Json resolver, so each DTO has a sibling formatter type whose .ctor registers the key strings. Those ctors DO contain the literals (canonical PascalCase first, then the camelCase and all-lowercase automata variants). Formatter map used here: ECGNEHMCGCN -> RecNet.Runtime/FNIDAIDJBBO.txt; KJECOLODAFA -> KKOHONLGIHL.txt; PHMHCPEMABG -> GBPDOLJBABB.txt; DJMHAFPGLLN -> LMCCNLLHLCJ.txt; RecNet.DeleteMessagesRequestDTO -> AAKOLCABMCP.txt; PDNMHAFBHIB/KECFAGHHOKD -> JNCIDDKKHHN.txt. C:\tmp\recroom-2023-03-21-decompiled contains ONLY Assembly-CSharp, so RecNet.Runtime DTO layouts came from the Cpp2IL/Il2CppInterop dummy assemblies shipped in the dev package: "C:\tmp\recroom-2023-03-21-devdork-package\Z\Rec Room old versions\staging\7490748483298966814\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out\RecNet.Runtime.dll" (field/enum constants) and ...\MelonLoader\Il2CppAssemblies\Il2CppRecNet.Runtime.dll (property CLR types). Il2CppDumper 6.7.46 fails on this binary (metadata v27 / il2cpp v29.1 overflow), so dump.cs is not available for 2023-03-21.

REQUEST-ENCODING RULE (applies to every endpoint above). All requests are built by BNDIAONDFFF..ctor(HTTPMethods verb, GJDLNNLKDIJ host, String route) — fields [+16]=verb, [+20]=host, [+24]=route, [+32]=param list, [+40]=raw JSON body (RecNet.Runtime/BNDIAONDFFF.txt:74-124). Verb enum is BestHTTP.HTTPMethods: Get=0, Head=1, Post=2, Put=3, Delete=4, Patch=5, Merge=6, Options=7. Host enum GJDLNNLKDIJ: Auth=0, API=1, Commerce=2, ... — every route in this subsystem uses host 1 (api.<apex>). BNDIAONDFFF.FGHNOKLDOKO (BNDIAONDFFF.txt:2490, ISIL 072-073 vs 099-133) shows: if verb==Get the AFGEDDANEKP params become a '?'-joined QUERY STRING; otherwise they become a BestHTTP.Forms.HTTPUrlEncodedForm body (application/x-www-form-urlencoded). AFGEDDANEKP skips null values (ISIL 031) and expands IEnumerable values into repeated keys. FJLLPHFOOJJ sets a raw JSON string body instead.

STRICTNESS. Unlike the 2020 client (LitJson + Util.GetKey, which throws on a missing key), the 2023 Utf8Json object formatters tolerate missing and unknown keys — a missing field silently becomes default(T). The real crash risks are TYPE mismatches: an object where an Int32 is expected (getplatformreferrers), or a JSON string where a number is expected. Enum-valued fields are safe either way: the enum formatter (RecRoom.Utf8json.Runtime/FMIHCGAPIOJ.txt:2187, ISIL 034-040 compares the token against 6=String and 5=Number) accepts both numeric and name form.

INT32 vs INT64 TRAP. ECGNEHMCGCN.PlayerID and KJECOLODAFA.FromPlayerId are read with ReadInt32 (FNIDAIDJBBO.txt deserialize; KKOHONLGIHL.txt deserialize), while KJECOLODAFA.Id / RoomId / PlayerEventId are Int64. Serving a player id above Int32.MaxValue in those two fields will break the 2023 client even though the same server field is a long for the 2020 client.

SERVER-SIDE GAPS FOUND WHILE CROSS-CHECKING (DorkNet at C:\Users\Alexa\Documents\Recnet). Every route in this subsystem is registered, but several bind or emit the wrong shape for THIS client:
1. api/externalfriendinvite/v1/getplatformreferrers + gettextmessagereferrers — DorkNet.Server/Controllers/API/ExternalFriendInvite/ExternalFriendInviteController.cs:46-66 returns an array of OBJECTS {InviteCode,Kind,Value,CreatedAt}; the client wants [Int32,...]. This is a hard deserialize fault, not a silent default.
2. api/externalfriendinvite/v1/createplatforminvite — same file :16-30 returns {InviteCode,InviteUrl,Platform,PlatformId}; client wants {"Success":bool,"Error":string} and will read Success=false.
3. api/externalfriendinvite/v1/sendtextmessageinvite — same file :32-44 binds [FromForm] phoneNumber + message; the 2023 client sends phoneNumber + friendCode + senderName (no "message"). Response should be {"Success","Error"}; the current {Success,InviteCode,PhoneNumber} only accidentally satisfies Success.
4. api/relationships/sendfriendintroductions — RelationshipsController.cs:475-489 binds [FromForm] playerId + introducedPlayerId; the 2023 client posts a JSON body {"ToPlayerIds":[…],"AboutPlayerId":N}. Nothing binds. Response should be {"Success","Message"}.
5. api/relationships/v1/bulkignoreplatformusers — RelationshipsController.cs:427-451 expects JSON key PlatformUserIds; the client sends PlatformIds. Zero rows imported, silently.
6. api/relationships/mutualfriends — RelationshipsController.cs:454-473 binds [FromQuery] playerId, but the 2023 client sends id, and it returns account objects {AccountId,Username,DisplayName,ProfileImage} where the client expects Relationship objects. Both param name and element shape are wrong.
7. api/relationships/v1/favorite|unfavorite — RelationshipsController.cs:354-360 returns RecNetResult {Success,Error}; the 2023 client parses the response as a Relationship (ECGNEHMCGCN). Non-fatal (defaults) but the wire contract is a Relationship.
8. api/offlineinvite/v1/send — MessagesController.cs:509-531 binds RecipientId + Data and returns {success,messageId} (lowercase); the 2023 client sends only PlayerId and reads {"Message":string}. Fully broken for this client (though no 2023 call site exists, so it is currently unreachable).
9. api/messages/v1/sendMultiple — MessagesController.cs:455-492 binds `long? RoomId`, but the 2023 client sends RoomId as a JSON STRING ("123"). Server JSON options (DorkNet.Server/Startup/ServiceCollectionExtensions.cs:380-381) only null the naming policy; NumberHandling.AllowReadingFromString is NOT set, so a sendMultiple that carries a room will 400.
Matching correctly today: api/relationships/v2/get, v2/addfriend, v2/removefriend, v2/sendfriendrequest, v2/acceptfriendrequest, v1/addfriendwithcode, v1/mute|unmute|ignore|unignore (server accepts both GET query id and POST form PlayerId), api/messages/v2/get (MessageDto keys Id/FromPlayerId/SentTime/Type/Data/RoomId/PlayerEventId are exactly right), api/messages/v2/send, api/messages/v3/delete, api/messages/v1/favoriteFriendOnlineStatus.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/externalfriendinvite/v1/createplatforminvite` | application/x-www-form-urlencoded, single field: platformId=<String> (lowercase p) | {"Success":Boolean, "Error":String}. Deserializer also accepts "success"/"error". Note this DTO uses Error, NOT Message. |
| POST | `api/externalfriendinvite/v1/getplatformreferrers` | none — POST with NO body and NO params | BARE JSON ARRAY OF INTEGERS: [Int32, ...] (player ids). NOT an array of objects — the Utf8Json Int32 formatter will throw on an object/string element and fault the client task. |
| POST | `api/externalfriendinvite/v1/gettextmessagereferrers` | none — POST with NO body and NO params | BARE JSON ARRAY OF INTEGERS: [Int32, ...] |
| POST | `api/externalfriendinvite/v1/sendtextmessageinvite` | application/x-www-form-urlencoded, three fields: phoneNumber=<String>, friendCode=<String>, senderName=<String> | {"Success":Boolean, "Error":String} — same generic-instantiation as createplatforminvite (identical metadata token 0x188477F70 on the typed-parse call in both methods) |
| GET | `api/messages/v1/favoriteFriendOnlineStatus` | none | body is NOT parsed at all — BNDIAONDFFF.KDOPJCNKOOK only awaits status success. Any 2xx body (even empty) satisfies the client. |
| POST | `api/messages/v1/sendMultiple` | raw JSON body (Dictionary<String,Object> serialized by LFHNDFEFEOD.ODHIHDMAPDF with tag "SendMessageMultiple"): {"ToPlayerIds":[Int64,...], "Type":Int32, "Data":String, "RoomId":"< | body is NOT parsed (KDOPJCNKOOK, status-only) |
| GET | `api/messages/v2/get` | none (no query params) | BARE JSON ARRAY of Message (KJECOLODAFA). Element: {"Id":Int64, "FromPlayerId":Int32, "SentTime":DateTime (ISO-8601 string), "Type":Int32 enum MEPCALGGMJC, "Data":String, "RoomId":Int64\|null, "PlayerEventId":Int64\|null |
| POST | `api/messages/v2/send` | application/x-www-form-urlencoded fields: ToPlayerId=<Int64>, Type=<Int32 MEPCALGGMJC>, Data=<String>, RoomId=<Int64> (omitted entirely when the Nullable has no value — AFGEDDANEKP | body is NOT parsed (BNDIAONDFFF.KDOPJCNKOOK, status-only). Any 2xx is success. |
| POST | `api/messages/v3/delete` | raw JSON body from RecNet.DeleteMessagesRequestDTO: {"MessageIds":[Int64,...]}. Deserializer-side variants accepted by the same DTO are "messageIds"/"messageids", but the client al | body is NOT parsed (KDOPJCNKOOK, status-only) |
| POST | `api/offlineinvite/v1/send` | application/x-www-form-urlencoded, single field: PlayerId=<Int64>. No Data/RecipientId field is sent. | {"Message":String}  — a single-key object. Deserializer also accepts "message". The client projects it to the String result. |
| GET | `api/relationships/mutualfriends` | query: id=<Int32>  (NOT playerId) | BARE JSON ARRAY of Relationship (ECGNEHMCGCN) — same element shape as v2/get: {"PlayerID":Int32,"RelationshipType":Int32,"Muted":Int32,"Ignored":Int32,"Favorited":Int32}. Response is client-cached via BNDIAONDFFF.AMDPEBK |
| POST | `api/relationships/sendfriendintroductions` | JSON body produced by UnityEngine.JsonUtility.ToJson over EEGNOHOELBG/SendFriendIntroductionsRequest — exact keys: {"ToPlayerIds":[Int32,...], "AboutPlayerId":Int32}. Content is a  | {"Success":Boolean, "Message":String}. Deserializer also accepts "success"/"message". Missing keys default (false/null) without throwing. |
| GET | `api/relationships/v1/addfriendwithcode` | query: code=<String> (AFGEDDANEKP(String,String,bool,bool) overload) | single Relationship object (see v2/get element shape) |
| POST | `api/relationships/v1/bulkignoreplatformusers` | JSON body via JsonUtility.ToJson over EEGNOHOELBG/BulkBlockPlatformUsersRequest — exact keys: {"Platform":Int32 (HHJIBNMLOAC: 0 All,1 Steam,2 Oculus,3 PlayStation,4 Xbox,5 RecNet,6 | body is NOT parsed (BNDIAONDFFF.KDOPJCNKOOK, status-only) |
| GET | `api/relationships/v1/favorite` | query: id=<Int32> | single Relationship object (see v2/get element shape). Client discards it and projects to Boolean via a Func<Boolean> continuation. |
| GET | `api/relationships/v1/unfavorite` | query: id=<Int32> | single Relationship object (see v2/get element shape), projected to Boolean client-side |
| GET | `api/relationships/v2/acceptfriendrequest` | query: id=<Int32> | single Relationship object (see v2/get element shape) |
| GET | `api/relationships/v2/addfriend` | query: id=<Int32> (BNDIAONDFFF.AFGEDDANEKP("id", boxed Int32) on a GET request => query string) | single Relationship object, same shape as v2/get elements: {"PlayerID":Int32,"RelationshipType":Int32,"Muted":Int32,"Ignored":Int32,"Favorited":Int32} |
| GET | `api/relationships/v2/get` | none (no query params, no body) | BARE JSON ARRAY of Relationship (ECGNEHMCGCN). Each element: {"PlayerID":Int32, "RelationshipType":Int32 enum (0 None,1 FriendRequestSent,2 FriendRequestReceived,3 Friend), "Muted":Int32 enum (0 None,1 Local,2 Remote,3 M |
| GET | `api/relationships/v2/removefriend` | query: id=<Int32> | single Relationship object (see v2/get element shape) |
| GET | `api/relationships/v2/sendfriendrequest` | query: id=<Int32> | single Relationship object (see v2/get element shape) |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/relationships/` — Mute/unmute and block(ignore)/unblock from the player card and the personal block list (Assembly-CSharp/JKNCKAMFODO.txt, AGUI/StackedUI/PersonalBlockListScreen.txt)
- `friend/` — Generating the shareable friend-code URL when the player opens "share my friend code"

#### Defects

##### `POST api/externalfriendinvite/v1/getplatformreferrers` — SHAPE_MISMATCH (breaks-gameplay)

Verb OK (GET+POST registered; client uses body-less POST). But the response is an array of OBJECTS {InviteCode,Kind,Value,CreatedAt} (:54, :68-78) while the client deserializes List<Int32> — Utf8Json's Int32 formatter throws on an object element, faulting the client task during SessionManager login bootstrap. Today the list is empty ([]) for players who never used createplatforminvite, which parses fine — but as soon as a player creates one platform invite, every subsequent login of THAT player hits a non-empty array and the bootstrap task hard-faults. Semantics are also inverted: the route means 'player ids who referred ME', but the server returns MY OWN outgoing invite records.

Handler: `DorkNet.Server/Controllers/API/ExternalFriendInvite/ExternalFriendInviteController.cs:46`

**Fix.** In GetPlatformReferrers return a bare int array of referrer player ids for the CURRENT player as invitee. Requires tracking redemptions: when an externalinvite:platform code is redeemed by a new account, record (inviteePlayerId -> inviterPlayerId); the handler then selects those inviter ids as (int) and returns Ok(thatList). Until redemption tracking lands, returning Ok(Array.Empty<int>()) is wire-safe, but per the no-stubs rule implement the redemption record with the fix.

##### `POST api/externalfriendinvite/v1/gettextmessagereferrers` — SHAPE_MISMATCH (breaks-gameplay)

Identical defect to getplatformreferrers: returns array of {InviteCode,Kind,Value,CreatedAt} objects where the client requires a bare [Int32,...]. Non-empty response (any player who used sendtextmessageinvite) hard-faults the login bootstrap task; also returns outgoing invites instead of referrers.

Handler: `DorkNet.Server/Controllers/API/ExternalFriendInvite/ExternalFriendInviteController.cs:57`

**Fix.** Same as getplatformreferrers: return bare int array of the player ids who referred the current player via text invite, backed by a redemption record keyed on the externalinvite:text code.

##### `POST api/relationships/sendfriendintroductions` — SHAPE_MISMATCH (degraded)

Server binds [FromForm] long playerId + [FromForm] long introducedPlayerId, but the 2023 client posts a raw JSON body {"ToPlayerIds":[Int32,...],"AboutPlayerId":Int32} (JsonUtility.ToJson + FJLLPHFOOJJ raw body). With a JSON content type the form value provider never runs, the two non-nullable value-type params fail to bind, and [ApiController] returns 400 — the 'Introduce my friends' flow from the watch social UI always fails. The response shape is also wrong for the client's PHMHCPEMABG reader ({"Success":bool,"Message":string}): the current Ok(new { Success = true }) at :491 would pass on Success but never runs.

Handler: `DorkNet.Server/Controllers/API/Relationships/V2/RelationshipsController.cs:475`

**Fix.** Replace the [FromForm] binding with a [FromBody] DTO { public List<int>? ToPlayerIds; public int AboutPlayerId; }, create one MessageEntity per recipient (Type 130 FriendIntroduction per the client's MEPCALGGMJC enum, Body carrying AboutPlayerId), and return Ok(new { Success = true, Message = "" }).

##### `POST api/relationships/v1/bulkignoreplatformusers` — SHAPE_MISMATCH (degraded)

Server's BulkIgnoreRequest (:417-421) has property PlatformUserIds, but the client's JSON body key is PlatformIds ({"Platform":Int32,"PlatformIds":[String,...]}). System.Text.Json leaves PlatformUserIds null, the guard at :432 returns {Imported:0}, and the Steam block-list import at login silently imports zero rows. Client does not parse the response (status-only), so no crash — just a silently non-functional platform block sync.

Handler: `DorkNet.Server/Controllers/API/Relationships/V2/RelationshipsController.cs:427`

**Fix.** In BulkIgnoreRequest add a second property bound to the client's key — e.g. [JsonPropertyName("PlatformIds")] public List<string>? PlatformIds — and in the handler use req.PlatformIds ?? req.PlatformUserIds (keep the old name for any 2020-era caller).

##### `POST api/messages/v1/sendMultiple` — SHAPE_MISMATCH (degraded)

SendMultipleBody.RoomId is long? (:461) but the 2023 client stringifies RoomId into the JSON body as a STRING ("RoomId":"123", Int64.ToString at PDNMHAFBHIB.txt:4176-4183). Server JSON options (ServiceCollectionExtensions.cs:380-381) only null the naming policy — NumberHandling.AllowReadingFromString is NOT set — so System.Text.Json throws during body deserialization and [ApiController] returns 400. Every fan-out invite that carries a room (the party-invite path in SessionManager_NestedType_FMGMGPGPBKE) fails; the client is status-only so the failure is silent. sendMultiple WITHOUT a room (RoomId omitted when null) works today.

Handler: `DorkNet.Server/Controllers/API/Messages/V2/MessagesController.cs:467`

**Fix.** Annotate SendMultipleBody (MessagesController.cs:456) with [System.Text.Json.Serialization.JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] (scoped to this DTO, avoiding a global behavior change), or change RoomId to string? and long.TryParse it in the handler.

##### `POST api/externalfriendinvite/v1/createplatforminvite` — SHAPE_MISMATCH (degraded)

Request binding is fine ([FromForm] platformId matches the client's lowercase form field; platform defaults 0). But the response {InviteCode,InviteUrl,Platform,PlatformId} contains neither key of the client's DJMHAFPGLLN reader {"Success":bool,"Error":string} — Utf8Json leaves defaults, so the client always sees Success=false and treats every platform-friend invite as failed (no success feedback in the RRUI add-friends contact list).

Handler: `DorkNet.Server/Controllers/API/ExternalFriendInvite/ExternalFriendInviteController.cs:16`

**Fix.** Return the invite-record keys plus the contract keys, e.g. Ok(new Dictionary<string,object>{["Success"]=true,["Error"]="",["InviteCode"]=code,["InviteUrl"]=url}) in ExternalFriendInviteController.CreatePlatformInvite.

##### `GET api/relationships/v1/favorite` — SHAPE_MISMATCH (cosmetic)

Server returns RecNetResult {"success":bool,"error":string} (DorkNet.Models/Auth/RecNetResult.cs:29-36), but the 2023 client parses the body as a Relationship (ECGNEHMCGCN). Utf8Json tolerates the unknown keys, so the client gets a default Relationship (PlayerID=0, Favorited=0) instead of the updated row. Non-fatal — the flag IS persisted (SetPreference :385-415) and resurfaces via v2/get — but the immediate response object is all-defaults, so any client logic reading the returned Favorited bit sees 0.

Handler: `DorkNet.Server/Controllers/API/Relationships/V2/RelationshipsController.cs:354`

**Fix.** In RelationshipsController.cs, make SetPreference (for the favorite/unfavorite call sites at :356/:360) return the caller's merged Relationship dictionary — after SaveChangesAsync, reload the pair rows and return Ok(BuildMerged(me, otherId, pair)) instead of Ok(new RecNetResult...). The mute/ignore call sites can share this (their responses are status-only on the client).

##### `GET api/relationships/v1/unfavorite` — SHAPE_MISMATCH (cosmetic)

Identical to v1/favorite: returns RecNetResult instead of a Relationship object; client reads all-default Relationship. Flag persists correctly server-side.

Handler: `DorkNet.Server/Controllers/API/Relationships/V2/RelationshipsController.cs:358`

**Fix.** Same as favorite — return BuildMerged from SetPreference.

##### `GET api/relationships/mutualfriends` — SHAPE_MISMATCH (cosmetic)

TWO defects. (1) Binds [FromQuery] long playerId but the 2023 client sends ?id=<Int32>, so playerId binds 0 and the guard at :458 always returns an empty array — the 'X mutual friends' text is permanently 0/absent for every profile. (2) Even if the param bound, the element shape is {AccountId, Username, DisplayName, ProfileImage} (:462-471) while the client deserializes List<ECGNEHMCGCN> and reads PlayerID/RelationshipType/Muted/Ignored/Favorited — unknown keys are ignored, so every element would be an all-default Relationship (PlayerID=0). Empty/duplicate-zero list means the mutual-friends UI silently shows nothing; no crash.

Handler: `DorkNet.Server/Controllers/API/Relationships/V2/RelationshipsController.cs:454`

**Fix.** In MutualFriends: bind [FromQuery(Name = "id")] long id (keep playerId as a fallback alias if desired), and return the mutual ids as Relationship dictionaries — for each mutual id, load the caller's pair rows and emit BuildMerged(me, mutualId, pair) (or SynthFriend under global-friends mode) so each element carries PlayerID/RelationshipType=3/Muted/Ignored/Favorited.

##### `POST api/externalfriendinvite/v1/sendtextmessageinvite` — SHAPE_MISMATCH (cosmetic)

Server binds [FromForm] phoneNumber + message, but the 2023 client sends phoneNumber + friendCode + senderName (there is no 'message' field). Both server params are nullable so binding succeeds; friendCode and senderName are silently dropped, and the stored record has an empty message slot. Response {Success:true,InviteCode,PhoneNumber} accidentally satisfies the client's {"Success","Error"} reader (Success=true, Error defaults null), so the SMS-invite UI reports success. Defect is data loss (the friend code that was supposed to be texted is never persisted), not a client fault.

Handler: `DorkNet.Server/Controllers/API/ExternalFriendInvite/ExternalFriendInviteController.cs:32`

**Fix.** Change SendTextMessageInvite's binding to [FromForm] string? phoneNumber, [FromForm] string? friendCode, [FromForm] string? senderName; persist all three in the Value string; return {"Success":true,"Error":""} (Dictionary or PascalCase DTO, plus InviteCode if wanted).

##### `POST api/offlineinvite/v1/send` — SHAPE_MISMATCH (none)

Server binds [FromForm] RecipientId + Data, but this client sends a single form field PlayerId; recipientId binds null and the handler returns 200 {success:false,error:"invalid_recipient"} (lowercase keys). The client would read {"Message":string} => null. Fully mismatched — but NO call site exists anywhere in the 2023-03-21 dump (dead API surface), so nothing is currently reachable/broken for this client. Severity 'none' for this build; still wrong wire for completeness.

Handler: `DorkNet.Server/Controllers/API/Messages/V2/MessagesController.cs:513`

**Fix.** If closing the gap: additionally bind [FromForm(Name="PlayerId")] long? playerId, use it as the recipient when RecipientId is absent, and return Ok(new Dictionary<string,object>{["Message"]="ok"}) alongside the existing keys (a Dictionary sidesteps the camelCase anonymous-object trap).

### Player events, playlists and keepsakes

`events-playlists`

Verified all 64 real client routes against DorkNet.Server. Telemetry, report, keepsake categories/globalconfig/rooms, progression active/event, and most playlist reads are OK. Broken clusters: (1) PlayerEvents — GET v1 list, GET v1/{id}/responses, POST v2 create/edit, POST v2/delete/{id} are wrong-verb (405); respond/deleteResponse read form+return bare int vs JSON+{Result}; club/{id} lacks {Events,ContinuationToken} wrapper; v1/all emits raw entity keys; broadcast implements a different feature; every v2 field-PUT returns a flat event instead of the PHHAKLPGNGC wrapper, and the tags/multiinstance/club PUTs also misread the request. (2) Playlists — bulk is GET-only; warning/accessibility/restrictions/levelvoting are POST-only no-ops; name/description/image/tags PUTs have [FromBody] params that 415 the client's form bodies. (3) Keepsakes — POST create has wrong request DTO and returns an object where the client needs a bare Guid; DELETE and collect routes missing; events/{id} shape wrong. (4) Progression — record DTO keys wrong; collect, xpboosts, previewEarnedXp routes missing. (5) event/{id}/instances (matchmaking host) not registered. Server serializes with PropertyNamingPolicy=null (ServiceCollectionExtensions.cs:380), and the 2023 Utf8Json readers accept Pascal/camel casings, so casing itself is not a defect anywhere; missing keys and verbs are.

**Client-side notes.** METHOD: verbs read from the BestHTTP.HTTPMethods immediate (0=GET,2=POST,3=PUT,4=DELETE) passed to the shared request helpers (CBKANFIOBCF.OHOGDIIEIJK-family, NLDBPDCNNCF named-op helper where verb sits in r8, BNDIAONDFFF ctor where verb is rdx). JSON keys read from Utf8Json generated-formatter ctors in the ISIL (each formatter lists reader-accepted casings Pascal/camel/lower per member, then the written PascalCase key array); member CLR types from the DTO type's getter signatures and the formatter's JECENNBIMEI<T> typeof fetches. Enums (BAMLHAODDOG accessibility, AKFCIMLMAKA broadcast-perm, CAMMJINEJFD response type, BNCFHOOCHAI result, HMECHOKOCBB keepsake category, NGPHEBMBPIC warning, JHAOFLCJNAL accessibility) have no string-enum formatter in the dump -> serialized as integers. Servers may emit any accepted casing; PascalCase is what the client itself writes.

CLIENT ARCHITECTURE: CBKANFIOBCF = playerevents API client, EMMAMFINMMJ = PlayerEventService cache layer (its underscore literals are cache keys, not routes), NLDBPDCNNCF = rooms+playlists API client, NCCLEJPIABA = keepsakes client, CDBFONFHJDO = progressionEvents client, JDHIDKCKHDK = GameSight telemetry, RecNet.Matchmaking issues event/{id}/instances from the matchmaking host root. Two request-body styles coexist: Utf8Json DTO bodies (respond, report, v2 create/edit, broadcast, keepsake create) and UnityEngine.JsonUtility bodies (deleteResponse, bulkInvite) whose field names are NOT recoverable from the ISIL (fields only, no methods) - server should keep accepting multiple key spellings there.

SERVER GAPS FOUND (DorkNet.Server): (1) POST api/playerevents/v2 and POST api/playerevents/v2/{id} not registered (create/edit 405) and POST api/playerevents/v2/delete/{id} is HttpDelete-only - event creation/editing/deletion is broken for the 2023 client; (2) GET api/playerevents/v1 is POST-only; GET v1/{id}/responses is POST-only; (3) v1/respond + v1/deleteResponse read form fields and return bare ints, but 2023 sends JSON {PlayerEventId,Type} and expects {Result:int}; (4) v1/club/{id} must return {Events,ContinuationToken}; v1/all must return {Created:[PlayerEvent],Responses:[{PlayerEvent,PlayerEventResponse}]}; tagfilters missing TrendingFilters; broadcast is a different feature (set BroadcastingRoomInstanceId, request {PlayerEventId,BroadcastRoomInstanceId}, respond PHHAKLPGNGC); v2 field-PUTs should return the PHHAKLPGNGC wrapper; (5) playlists/bulk is GET-only but client POSTs form 'id'/'name'; playlists {accessibility,levelvoting,restrictions,warning} are POST-only but client PUTs (PlaylistsController.cs:118-122,242); (6) keepsakes: POST api/keepsakes must take {RoomId,SubRoomId,KeepsakeCategory} and return a bare Guid; DELETE api/keepsakes/{guid} and POST api/keepsakes/{guid}/collect ({TotalXp,SocialBoostXp}) are missing; api/keepsakes/events/{id} must return KeepsakeRoomInstanceIdsDTO lists; (7) progressionEvents record DTO keys wrong (client wants AccountId/Xp/GameMinutesToday/RewardsCollected/BonusRewardsCollected/XpBoostLastPurchasedAt); collect/{eventId}/{rewardIndex} (POST -> GiftDrop), {id}/xpboosts, {id}/xpboosts/{boostId}/previewEarnedXp missing; (8) event/{eventId}/instances (matchmaking-host instance browser, List<{RoomInstanceId,RoomId,SubRoomId,IsFull,CreatedAt,PlayerIds}>) not registered - only room/{roomId}/instances exists.

Also observed but outside this group's literal list: POST playlists (create, NLDBPDCNNCF.COBELCLDHFE, form 'name') and GET playlists (by name, query 'name') use the bare 'playlists' literal; rooms/curated_playlists GET -> List<Int64> is registered server-side.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/gamesight/event` | form field 'EventData' = Utf8Json-serialized object with snake_case properties: {"type":String,"user_id":String,"identifiers":{"resolution":String,"language":String,"timezone":Stri | ignored by client (plain Task) - 200 with any/empty body |
| POST | `api/keepsakes` | JSON body: {"RoomId":Int64,"SubRoomId":Int64?,"KeepsakeCategory":Int32(enum HMECHOKOCBB)} | bare JSON GUID string (the new KeepsakeInstanceId) - NOT an object |
| GET | `api/keepsakes/categories` | none | {"Results":[{"KeepsakeCategoryId":Int32(enum HMECHOKOCBB),"VisualId":String,"LimitPerRoom":Int32,"XpValue":Int32,"IconOutlineImageName":String,"IconFilledImageName":String}],"TotalResults":Int32} - paged wrapper, not a b |
| GET | `api/keepsakes/events/{progressionEventId}` | none | {"Instances":[{"RoomId":Int64,"KeepsakeInstanceIds":[Guid]}],"CollectionRecords":[{"RoomId":Int64,"KeepsakeInstanceIds":[Guid]}]} (both List<KeepsakeRoomInstanceIdsDTO>) |
| GET | `api/keepsakes/globalconfig` | none | {"KeepsakeFeatureEnabled":Boolean,"KeepsakeRoomLimit":Int32,"SocialXpBoostEnabled":Boolean} |
| GET | `api/keepsakes/rooms/{roomId}` | none | {"Instances":[{"KeepsakeInstanceId":Guid,"KeepsakeCategoryConfigId":Int32(enum HMECHOKOCBB),"PlacedByAccountId":Int32,"RoomId":Int64,"SubRoomId":Int64?}],"CollectionRecords":[{"AccountId":Int32,"KeepsakeInstanceId":Guid, |
| DELETE | `api/keepsakes/{keepsakeInstanceId:guid}` | none (path = String.Format("{0}/{1}", "api/keepsakes", guid)) | no body consumed; 200 suffices |
| POST | `api/keepsakes/{keepsakeInstanceId:guid}/collect` | none | {"TotalXp":Int32,"SocialBoostXp":Int32} |
| GET | `api/playerevents/v1` | none | JSON array of HPIOAGDJHDH (see v1/{0}) |
| GET | `api/playerevents/v1/all` | none | {"Created":[HPIOAGDJHDH],"Responses":[{"PlayerEvent":HPIOAGDJHDH,"PlayerEventResponse":{"PlayerEventResponseId":Int64,"PlayerEventId":Int64,"PlayerId":Int32,"CreatedAt":DateTime,"Type":Int32(enum CAMMJINEJFD)}}]} |
| GET | `api/playerevents/v1/all/{0}` | none | same as api/playerevents/v1/all |
| POST | `api/playerevents/v1/broadcast` | JSON body: {"PlayerEventId":Int64,"BroadcastRoomInstanceId":Int64?} (null clears) | PHHAKLPGNGC wrapper |
| POST | `api/playerevents/v1/bulk` | form field 'Ids' (generic list overload of request-builder AFGEDDANEKP; event ids) | JSON array of HPIOAGDJHDH |
| POST | `api/playerevents/v1/bulkInvite` | JSON body via JsonUtility.ToJson(RecNet.Events.BulkInviteRequest); field names not in ISIL (fields only); server-side DTO {PlayerEventId, InvitedPlayerIds:List<int>} matches the me | {"FailedInvites":[{"InvitedPlayerId":Int32,"Result":Int32(enum BNCFHOOCHAI)}],"Result":Int32(enum BNCFHOOCHAI)} |
| GET | `api/playerevents/v1/club/{0}` | query: take (Int32, only when non-null), continuationToken (String, only when non-null) | {"Events":[HPIOAGDJHDH],"ContinuationToken":String} (both required keys) |
| GET | `api/playerevents/v1/clubs` | query: repeated 'id' per club id | JSON array of HPIOAGDJHDH |
| POST | `api/playerevents/v1/deleteResponse` | JSON body via UnityEngine.JsonUtility.ToJson(RecNet.Events.DeleteResponseRequest) carrying eventId (Int64 at +0x10) and response type (enum at +0x18); exact JSON field names UNKNOW | {"Result":Int32(enum BNCFHOOCHAI)} |
| POST | `api/playerevents/v1/report` | JSON body: {"ReportCategory":Int32(enum),"PlayerEventId":Int64,"Details":String} | {"Success":Boolean,"Message":String} |
| POST | `api/playerevents/v1/respond` | JSON body (Utf8Json): {"PlayerEventId":Int64,"Type":Int32(enum CAMMJINEJFD)} | {"Result":Int32(enum BNCFHOOCHAI)} - an OBJECT, not the 2020 bare int |
| GET | `api/playerevents/v1/room/{0}` | none | JSON array of HPIOAGDJHDH |
| GET | `api/playerevents/v1/search` | query: query (String), sort (Int32 enum PJBLEKMMACM), scheduleFilter (Int32 enum BJBLPLKMLBE, omitted when null) | JSON array of HPIOAGDJHDH |
| GET | `api/playerevents/v1/searchlive` | query: query (String) | JSON array of CLDDIKOJMAM = HPIOAGDJHDH keys + {"PlayerCount":Int32,"IsFull":Boolean} |
| GET | `api/playerevents/v1/tagfilters` | none | {"PinnedFilters":[String],"PopularFilters":[String],"TrendingFilters":[String]} |
| GET | `api/playerevents/v1/{0}` | query: includeDetails (Boolean; lambda __c), OAKKDBLNKLG variant adds clubId (Int64; lambda DEIBMMABHIK keys 'includeDetails','clubId') | HPIOAGDJHDH: {"PlayerEventId":Int64,"CreatorPlayerId":Int32,"RoomId":Int64,"SubRoomId":Int64?,"ClubId":Int64?,"Name":String,"Description":String,"ImageName":String,"StartTime":DateTime,"EndTime":DateTime,"AttendeeCount": |
| GET | `api/playerevents/v1/{0}/responses` | none | JSON array of {"PlayerEventResponseId":Int64,"PlayerEventId":Int64,"PlayerId":Int32,"CreatedAt":DateTime,"Type":Int32(enum CAMMJINEJFD)} |
| POST | `api/playerevents/v2` | JSON body: {"RoomId":Int64,"SubRoomId":Int64?,"ClubId":Int64?,"Name":String,"Description":String,"Tags":[String],"ImageName":String,"StartTime":DateTime,"EndTime":DateTime,"Accessi | {"PlayerEvent":MDCBEPJCJPO(HPIOAGDJHDH+Tags),"Result":Int32(enum BNCFHOOCHAI),"TagModifyResult":{"Result":Int32(enum OFLPPEFGGOP),"Tags":[String]}} |
| POST | `api/playerevents/v2/delete/{0}` | none (empty POST) | PHHAKLPGNGC wrapper |
| POST | `api/playerevents/v2/{0}` | same JSON body as POST api/playerevents/v2 | PHHAKLPGNGC (see v2 create) |
| PUT | `api/playerevents/v2/{0}/accessibility` | form field: accessibility (Int32 enum) | PHHAKLPGNGC wrapper {PlayerEvent,Result,TagModifyResult} |
| PUT | `api/playerevents/v2/{0}/club` | form field: clubId (Int64, only when non-null; omitted = clear) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/description` | form field: description (String) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/image` | form field: imageName (String) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/multiinstance` | form fields: isMultiInstance (Boolean), supportsMultiInstanceRoomChat (Boolean), defaultBroadcastPermissions (Int32 enum), canRequestBroadcastPermissions (Int32 enum) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/name` | form field: name (String) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/room` | form fields: roomId (Int64), subRoomId (Int64, only when non-null) | PHHAKLPGNGC wrapper |
| PUT | `api/playerevents/v2/{0}/tags` | JSON body = bare array of tag strings (lambda serializes the List<String> via Utf8Json static ALHIJCJOLCB and sets it as body; no field name) | PHHAKLPGNGC wrapper (TagModifyResult carries accepted tags) |
| PUT | `api/playerevents/v2/{0}/time` | form fields: startTime, endTime (DateTime strings) | PHHAKLPGNGC wrapper |
| GET | `api/progressionEvents/active` | none | bare JSON integer = active progression event id (client wraps as Int64?) |
| POST | `api/progressionEvents/collect/{eventId}/{rewardIndex}` | none | gift-drop object: {"Id":...,"FromPlayerId":...,"ConsumableItemDesc":...,"AvatarItemType":...,"AvatarItemDesc":...,"EquipmentPrefabName":String,"EquipmentModificationGuid":...,"CurrencyType":...,"Currency":...,"Xp":Int32, |
| GET | `api/progressionEvents/event/{eventId}` | none | {"ProgressionEventId":Int64,"Name":String,"Rewards":[{"ProgressionEventRewardId":Int64,"GiftDropId":Int64,"ImageName":String,"Xp":Int32,"RewardIndex":Int32,"IsBonus":Boolean}],"KeepsakeRoomLists":[{"KeepsakeRoomListId":. |
| GET | `api/progressionEvents/record/{progressionEventId}` | none | {"AccountId":Int32,"Xp":Int32,"GameMinutesToday":Int32,"RewardsCollected":Int32,"BonusRewardsCollected":Int32,"XpBoostLastPurchasedAt":DateTime?} |
| GET | `api/progressionEvents/{eventId}/xpboosts` | none | JSON array of {"ProgressionEventPurchasableXpBoostId":Guid,"Cost":Int32,"XpMultiplier":number,"XpCap":Int32,"LookbackDurationTicks":Int64,"CooldownDurationTicks":Int64} |
| GET | `api/progressionEvents/{eventId}/xpboosts/{boostId}/previewEarnedXp` | none | bare JSON integer (XP the boost would grant) |
| POST | `data/event` | form fields: eventType (String), eventParams (serialized parameter bag) | ignored by client (fire-and-forget; error log 'Failed to send event data') - 200 empty is fine |
| GET | `event/{0}/instances` | none (path served from the matchmaking-host root, same builder as matchmake/* and room/{0}/instances) | JSON array of {"RoomInstanceId":Int64,"RoomId":Int64,"SubRoomId":Int64,"IsFull":Boolean,"CreatedAt":DateTime,"PlayerIds":[Int32]} |
| POST | `playlists/bulk` | form fields: repeated 'id' (Int64 overload) or repeated 'name' (String overload) | JSON array of CNIPGJIIFJF |
| GET | `playlists/cheeredby/me` | none | JSON array of CNIPGJIIFJF |
| GET | `playlists/createdby/me` | none | JSON array of CNIPGJIIFJF |
| GET | `playlists/favoritedby/me` | none | JSON array of CNIPGJIIFJF |
| GET | `playlists/visitedby/me` | none | JSON array of CNIPGJIIFJF |
| GET | `playlists/{0}` | details variant: query include (bitmask; __c <GetPlaylistWithDetails> lambda) | CNIPGJIIFJF: {"PlaylistId":Int64,"Name":String,"Description":String,"ImageName":String,"WarningMask":Int32(enum NGPHEBMBPIC),"CustomWarning":String,"CreatorAccountId":Int32,"State":Int32(enum),"Accessibility":Int32(enum  |
| DELETE | `playlists/{0}` | none | no body consumed (Task); 200 suffices |
| PUT | `playlists/{0}/accessibility` | form field: accessibility (Int32 enum JHAOFLCJNAL) | DAFNFCFDPJB |
| PUT | `playlists/{0}/description` | form field: description (String) | DAFNFCFDPJB |
| PUT | `playlists/{0}/image` | form field: imageName (String) | DAFNFCFDPJB |
| GET | `playlists/{0}/interactionby/me` | none | {"Cheered":Boolean,"Favorited":Boolean,"LastVisitedAt":DateTime?} |
| PUT | `playlists/{0}/interactionby/me/cheer` | none | no body consumed; 200 suffices. Client then invalidates cache key playlistinteraction/{id} |
| PUT | `playlists/{0}/interactionby/me/favorite` | none | no body consumed; 200 suffices |
| PUT | `playlists/{0}/levelvoting` | form field: supportsLevelVoting (Boolean) | DAFNFCFDPJB |
| PUT | `playlists/{0}/name` | form field: name (String) | DAFNFCFDPJB (full playlist details) |
| PUT | `playlists/{0}/restrictions` | form fields: supportsJuniors, supportsScreens, supportsTeleportVR, supportsWalkVR (Booleans) | DAFNFCFDPJB |
| PUT | `playlists/{0}/rooms/{1}` | none (empty PUT to add; DELETE to remove) | DAFNFCFDPJB |
| PUT | `playlists/{0}/tags` | form fields: repeated 'tag' and repeated 'autoTag' | DAFNFCFDPJB |
| PUT | `playlists/{0}/warning` | form fields: warningMask (Int32 enum), customWarning (String) | DAFNFCFDPJB |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/keepsakes/events` — Cache invalidation after keepsake place/collect
- `api/progressionEvents` — Cache invalidation
- `event/{0}/{1}` — Share-event code/link generation
- `event_responses/{0}` — RSVP-list cache in EMMAMFINMMJ
- `events_by_club_ids/` — Club-events cache
- `meetup/` — Meetup share-code link generation
- `playlistinteraction/{0}` — Playlist interaction cache
- `remote_player_event_list/{0}` — Profile events cache
- `room_events/{0}` — Room events cache

#### Defects

##### `GET api/playerevents/v1` — VERB_MISMATCH (breaks-gameplay)

Only [HttpPost("api/playerevents/v1")] (Create) is registered on this path; GET returns 405. The watch Events tab upcoming-events list (CBKANFIOBCF.HIHEFBMPOGC) fails entirely. The GET list handler that exists is on api/playerevents/v2 (line 32), which this client never GETs as a list.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:281`

**Fix.** Add [HttpGet("api/playerevents/v1")] returning a JSON array of full HPIOAGDJHDH-shaped events (reuse the fixed ToWire).

##### `GET api/playerevents/v1/club/{0}` — SHAPE_MISMATCH (breaks-gameplay)

Handler returns a bare JSON array (line 119: Ok(rows.Select(ToWire))). Client deserializes IOKLNPFOLGI, an OBJECT {"Events":[...],"ContinuationToken":string}; array-vs-object makes the Utf8Json reader throw and the club events tab errors out. take/continuationToken query params are also ignored (no paging).

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:99`

**Fix.** Return Ok(new { Events = rows.Select(ToWire), ContinuationToken = "" }) (non-null string), honoring take/continuationToken for paging.

##### `GET api/playerevents/v1/all` — SHAPE_MISMATCH (breaks-gameplay)

Created items are raw entity projections {Id,Title,Description,RoomId,StartsAt,EndsAt,Capacity} (lines 187-196) — none of the HPIOAGDJHDH keys match, so every created event deserializes as all-defaults. Responses items are flat {Id,EventId,Response,CreatedAt} (lines 201-207) but the client reads [{"PlayerEvent":{...},"PlayerEventResponse":{PlayerEventResponseId,PlayerEventId,PlayerId,CreatedAt,Type}}] — PlayerEvent comes back null and the My Events page breaks/NREs.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:177`

**Fix.** Emit Created via ToWire and Responses as {PlayerEvent:ToWire(ev), PlayerEventResponse:{PlayerEventResponseId:r.Id, PlayerEventId:r.EventId, PlayerId:(int)r.PlayerId, CreatedAt:r.CreatedAt, Type:r.Response}} joining each response row to its event.

##### `GET api/playerevents/v1/all/{0}` — SHAPE_MISMATCH (breaks-gameplay)

Identical raw-entity-key defect as v1/all (lines 217-243). Another player's profile events page gets null PlayerEvents.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:214`

**Fix.** Same wrapper/key fix as v1/all.

##### `GET api/playerevents/v1/{0}/responses` — VERB_MISMATCH (breaks-gameplay)

Path registered as [HttpPost] only (RespondList RSVP alias). Client GETs it for the attendee list -> 405; event details attendee list broken.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:250`

**Fix.** Add [HttpGet("api/playerevents/v1/{eventId:long}/responses")] returning [{PlayerEventResponseId,PlayerEventId,PlayerId,CreatedAt,Type}] from PlayerEventResponses (Type = stored Response int).

##### `POST api/playerevents/v1/respond` — SHAPE_MISMATCH (breaks-gameplay)

Handler binds [FromForm] EventId/Response, but the 2023 client sends a JSON body {"PlayerEventId":long,"Type":int} — the form provider yields nothing, eventId stays null, and the handler returns bare int 2 (NoSuchEvent) without ever writing the RSVP. The client then Deserialize<CEKABGOIOAF>-s an OBJECT {"Result":int} from the bare int and throws. RSVP button fully broken.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:344`

**Fix.** Accept JSON {PlayerEventId,Type} (keep form fallback for 2020), and return Ok(new { Result = 0 }).

##### `POST api/playerevents/v1/deleteResponse` — SHAPE_MISMATCH (breaks-gameplay)

Same defect pair as respond: form-only read of EventId (client sends a JsonUtility JSON body whose exact field names are unrecoverable from the ISIL) and bare-int response where the client expects {"Result":int}. Un-RSVP broken.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:363`

**Fix.** Parse the JSON body accepting multiple candidate keys (eventId/EventId/PlayerEventId/playerEventId and type/Type/response/Response), keep form fallback, return {Result:0}.

##### `POST api/playerevents/v2` — VERB_MISMATCH (breaks-gameplay)

Path only has [HttpGet] (ListUpcoming). The 2023 create-event flow POSTs a 14-field JSON body here -> 405. Event creation is impossible. (POST api/playerevents/v1 exists but the 2023 client never uses it.)

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:32`

**Fix.** Add [HttpPost("api/playerevents/v2")] binding BIEFKAOABMP {RoomId,SubRoomId,ClubId,Name,Description,Tags,ImageName,StartTime,EndTime,Accessibility,IsMultiInstance,SupportMultiInstanceRoomChat,DefaultBroadcastPermissions,CanRequestBroadcastPermissions} and returning the PHHAKLPGNGC wrapper {PlayerEvent:<full event+Tags>, Result:0, TagModifyResult:{Result:0,Tags:[...]}}.

##### `POST api/playerevents/v2/{0}` — VERB_MISMATCH (breaks-gameplay)

Path only has [HttpGet]. Edit-event save POSTs the same BIEFKAOABMP body -> 405. Editing impossible.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:48`

**Fix.** Add [HttpPost("api/playerevents/v2/{eventId:long}")] applying the full-body edit and returning the PHHAKLPGNGC wrapper.

##### `PUT api/playerevents/v2/{0}/tags` — SHAPE_MISMATCH (breaks-gameplay)

PUT registered, but the client body is a BARE JSON ARRAY of tag strings and ReadRequestFieldsAsync only parses JSON OBJECTS (line 742 checks ValueKind==Object) — the array is silently dropped, ReadStringFieldAsync returns null, and the handler stores empty string: saving tags always clears them. Tags are also stored in the side-table and never emitted; flat response instead of wrapper (whose TagModifyResult.Tags the client reads back).

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:468`

**Fix.** When Content-Type is JSON and root is an array, read it as List<string>; persist tags on the entity; return wrapper with TagModifyResult:{Result:0,Tags:accepted}.

##### `PUT api/playerevents/v2/{0}/multiinstance` — SHAPE_MISMATCH (breaks-gameplay)

PUT registered but reads field 'multiInstance' — the client sends 'isMultiInstance', 'supportsMultiInstanceRoomChat', 'defaultBroadcastPermissions', 'canRequestBroadcastPermissions'; none are read (case-insensitive dict, but names differ), so the setting is always stored false and the three other fields are dropped entirely. Flat response instead of wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:479`

**Fix.** Read the four actual field names, persist all four on the entity (they are also HPIOAGDJHDH response keys), return wrapper.

##### `POST api/playerevents/v1/broadcast` — SHAPE_MISMATCH (breaks-gameplay)

Handler implements a text-message broadcast: binds {PlayerEventId,Message} and messages all RSVPs, returning {Success,Sent}. The 2023 client's feature is 'set/clear the broadcasting room instance': body {"PlayerEventId":long,"BroadcastRoomInstanceId":long?} and response PHHAKLPGNGC. BroadcastRoomInstanceId is never stored, the response shape is wrong, and as a bonus every RSVP gets a spurious message.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:573`

**Fix.** Rewrite: bind {PlayerEventId,BroadcastRoomInstanceId?}, persist it as the event's BroadcastingRoomInstanceId (null clears), return the PHHAKLPGNGC wrapper.

##### `POST api/playerevents/v2/delete/{0}` — VERB_MISMATCH (breaks-gameplay)

Registered [HttpDelete] only; the 2023 client sends verb 2 (POST) -> 405, delete button broken. Response {success,error} is also wrong — client reads the PHHAKLPGNGC wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:375`

**Fix.** Add [HttpPost("api/playerevents/v2/delete/{id:long}")] (keep HttpDelete) and return {PlayerEvent:<deleted event wire>, Result:0, TagModifyResult:{Result:0,Tags:[]}}.

##### `POST playlists/bulk` — VERB_MISMATCH (breaks-gameplay)

[HttpGet] only, reading query 'id'/'ids'. The 2023 client POSTs a form with repeated 'id' (or repeated 'name' for the by-name overload) -> 405. Hot/featured playlist hydration fails. No 'name' lookup exists at all.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:242`

**Fix.** Add [HttpPost("/playlists/bulk")] reading form fields 'id' (repeated) and 'name' (repeated, resolve by Name), returning the same bare list of union entries.

##### `PUT playlists/{0}/name` — SHAPE_MISMATCH (breaks-gameplay)

PUT is registered, but the action declares a non-optional [FromBody] StringFieldRequest parameter alongside [FromForm]. The 2023 client sends application/x-www-form-urlencoded ('name'); with [ApiController], the JSON input formatter rejects the form content type and the request dies with 415 before the action runs. Rename broken. (Field-name case 'name' vs 'Name' would be fine — form binding is case-insensitive.)

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:77`

**Fix.** Drop the [FromBody] parameter and read form/JSON manually (like PlayerEventsController.ReadRequestFieldsAsync), then keep returning BuildDetailsResponseAsync (that response shape matches DAFNFCFDPJB).

##### `PUT playlists/{0}/description` — SHAPE_MISMATCH (breaks-gameplay)

Same [FromBody]+form 415 defect as playlists/{0}/name.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:84`

**Fix.** Same manual field-reading fix.

##### `PUT playlists/{0}/image` — SHAPE_MISMATCH (breaks-gameplay)

Same [FromBody]+form 415 defect; client sends form field 'imageName' (would bind to the [FromForm(Name="ImageName")] fallback case-insensitively once the FromBody parameter is removed).

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:91`

**Fix.** Same manual field-reading fix.

##### `PUT playlists/{0}/tags` — SHAPE_MISMATCH (breaks-gameplay)

Two defects: (a) same [FromBody]+form 415 rejection; (b) even past that, the client sends REPEATED form fields 'tag' and 'autoTag', while the server reads a single 'Tags' CSV — the actual fields would never bind. Tag saves broken.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:98`

**Fix.** Read form["tag"] and form["autoTag"] as repeated values, join into TagsCsv, return details response.

##### `PUT playlists/{0}/warning` — VERB_MISMATCH (breaks-gameplay)

[HttpPost] only -> client PUT gets 405. Additionally the handler is an explicit no-op ('field not persisted yet') — warningMask/customWarning are never stored, so even after adding PUT the setting is a stub (WarningMask always 0 in the union entry, line 3260 of RoomsController).

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:122`

**Fix.** Add [HttpPut], read form warningMask/customWarning, persist to PlaylistEntity columns and surface them in BuildPlaylistUnionEntry.

##### `PUT playlists/{0}/accessibility` — VERB_MISMATCH (breaks-gameplay)

[HttpPost] only -> 405 on the client's PUT, and the shared PlaylistAck handler is a no-op — publish/private (accessibility enum) is never persisted; union entry hardcodes Accessibility=1. Publishing a playlist is impossible.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:118`

**Fix.** Add [HttpPut], persist 'accessibility' form int on the entity, emit it in BuildPlaylistUnionEntry.

##### `PUT playlists/{0}/restrictions` — VERB_MISMATCH (breaks-gameplay)

[HttpPost] only + no-op stub. Client PUTs supportsJuniors/supportsScreens/supportsTeleportVR/supportsWalkVR booleans; 405 today, discarded after a verb-only fix.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:121`

**Fix.** Add [HttpPut], persist the four booleans, surface in union entry.

##### `PUT playlists/{0}/levelvoting` — VERB_MISMATCH (breaks-gameplay)

[HttpPost] only + no-op stub; client PUTs 'supportsLevelVoting'; union entry hardcodes SupportsLevelVoting=false.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:120`

**Fix.** Add [HttpPut], persist supportsLevelVoting, surface in union entry.

##### `POST api/keepsakes` — SHAPE_MISMATCH (breaks-gameplay)

Create() reads a legacy DTO {category,eventKey,title,description,imageName,earnedAt}; the 2023 client sends {"RoomId":long,"SubRoomId":long?,"KeepsakeCategory":int} — all three ignored, so the row is stored as category 'event' with a synthetic 'manual:{guid}' key. Response is a full object (ToWire) but the client does Deserialize<Guid> expecting a BARE JSON GUID string -> throws. Placing keepsakes is fully broken, and the knock-on is that GET api/keepsakes/rooms/{roomId} (which filters category=='room' with room:{id} EventKeys) can never surface a client-placed keepsake.

Handler: `DorkNet.Server/Controllers/API/Keepsakes/KeepsakesController.cs:117`

**Fix.** Bind {RoomId,SubRoomId,KeepsakeCategory}, store a real KeepsakeInstance row (guid id, room, subroom, category, placer), and return the bare Guid as the JSON body.

##### `DELETE api/keepsakes/{keepsakeInstanceId:guid}` — FIXED

Was: no DELETE route under api/keepsakes at all; a GUID path segment matched no template in KeepsakesController -> 404, so removing a placed keepsake failed.

**Fixed.** `KeepsakesController.Delete` ([HttpDelete("{keepsakeInstanceId:guid}")]) resolves the guid back to the row whose EventKey encodes it, gates on placer / room creator / accepted co-owner (RoomRoles.Role==0) / admin, deletes the instance plus any collection rows pointing at it, and returns a bodiless 200 — the client's issuing method returns the non-generic `LDGADANDBIO` promise (NCCLEJPIABA.JBKENCNIEPA, NCCLEJPIABA.txt:1544 + `Move rdx, 4`@1698) so no body is read.

##### `POST api/keepsakes/{keepsakeInstanceId:guid}/collect` — FIXED

Was: route not registered -> 404, so collecting a keepsake in-room failed.

**Fixed.** `KeepsakesController.Collect` ([HttpPost("{keepsakeInstanceId:guid}/collect")]) writes a collection row (`Category="collection"`, `EventKey="collect:{roomId}:{instanceGuid:N}"` — no schema change) and returns `{"TotalXp":int,"SocialBoostXp":int}` (DHNBKMHDANK; keys from its Utf8Json formatter, RecNet.Runtime/PKCMBJFBHBO.txt:42,69). TotalXp is the whole award — the client renders `TotalXp - SocialBoostXp` as the base figure (PDFJLLECNBE_NestedType_LKIPMJFEAFK.txt:82-99) — and is priced from the same category `XpValue` that `api/keepsakes/categories` serves. SocialBoostXp is 0 while SocialXpBoostEnabled=false. Re-collecting the same instance is idempotent and awards 0.

Two supporting corrections in the same controller: `api/keepsakes/categories` now emits the full `PIHCLHIKEPH` id set (0-8, dump.cs:1199524) instead of three entries keyed by DorkNet's internal account/event/room buckets — the client folds the list into a `Dictionary<PIHCLHIKEPH, KeepsakeCategoryConfigDTO>` that every placed instance is looked up in — and `api/keepsakes/rooms/{roomId}` now returns real `CollectionRecords` (`{AccountId, KeepsakeInstanceId, CollectedAt}`, dump.cs:1234642 / CNNAFLNKDCL.txt:48,75,99) scoped to the caller instead of an empty array.

##### `GET api/keepsakes/events/{progressionEventId}` — SHAPE_MISMATCH (breaks-gameplay)

Returns {KeepsakeProgressionEventId, Instances:<legacy keepsake rows {Id,PlayerId,Category,EventKey,Title,...}>, CollectionRecords:[], KeepsakeProgressionEventIds}. Client KeepsakeProgressionEventInstancesDTO wants Instances AND CollectionRecords as List<KeepsakeRoomInstanceIdsDTO> = [{"RoomId":long,"KeepsakeInstanceIds":[guid]}]. The row objects contain neither key -> RoomId=0 and KeepsakeInstanceIds=null per entry (NRE risk in the event hunt map).

Handler: `DorkNet.Server/Controllers/API/Keepsakes/KeepsakesController.cs:59`

**Fix.** Group event keepsake instances by room and emit {Instances:[{RoomId,KeepsakeInstanceIds:[...]}],CollectionRecords:[same shape for the caller's collected ones]}.

##### `POST api/progressionEvents/collect/{eventId}/{rewardIndex}` — MISSING (breaks-gameplay)

Route not registered anywhere -> 404. Claiming a reward chest on the progression track fails; client expects the standard GiftDrop object (same DTO as gifts/generate, singular GiftDrop keys per docs/recroom-2023-room-consumables.md).

**Fix.** Add [HttpPost("api/progressionEvents/collect/{eventId:long}/{rewardIndex:int}")] that increments the player's RewardsCollected and returns a GiftDrop object (reuse the gifts/generate wire builder).

##### `GET event/{0}/instances` — FIXED

Was: only /room/{roomId:long}/instances existed; no event/{eventId}/instances was registered anywhere -> 404 and the 'join' instance browser for multi-instance events never populated. Client wants List<{RoomInstanceId,RoomId,SubRoomId,IsFull,CreatedAt,PlayerIds}> — the 2023 Utf8Json reader accepts the camelCase keys the room variant already emits.

Verb/shape ground truth: `Matchmaking+<GetEventInstanceBrowser>b__0` (Matchmaking_NestedType_CBMHJMNIHNN.txt:14) formats `"event/{0}/instances"` at :296 with the PlayerEventId ([rdi+16] of the HPIOAGDJHDH it closes over) and news up BNDIAONDFFF with rdx=0 at :124 = HTTPMethods.Get. Return type `FGLDKEJLAKB<List<PNDCMIMEJLD>>` = bare JSON array; PNDCMIMEJLD's getters (PNDCMIMEJLD.txt:3-197) are Int64/Int64/Int64/Boolean/DateTime/List\<Int32\>/String/Int32/Boolean/String, i.e. the same SimpleRoomInstance the room browser returns (Matchmaking.txt:14237 uses an identical BNDIAONDFFF/verb-0 sequence). The trailing SubroomName/PlayerCount/HasModPresent/HashedInstanceId are populated locally on the watch and are not read off the wire.

**Fixed.** `[HttpGet("/event/{eventId:long}/instances")]` now lives on `PlayerEventsController.EventInstances` (the route is a matchmaking-host-root path but the data is a player-event one): it resolves the event's RoomId plus the SubRoomId persisted in the event's extras blob, then enumerates PlayerPresenceService instances for that room (filtered to the sub-room when the event names one) and merges in the caller's own PrivateInstances rows, emitting the same six camelCase keys as `room/{id}/instances`. `isFull` is computed from the instance's MaxCapacity vs its presenced player count.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs` (`EventInstances`)

##### `GET api/playerevents/v1/{0}` — SHAPE_MISMATCH (degraded)

Verb OK (HttpGet v1/{eventId} shares GetOne with v2). ToWire (lines 262-274) emits only {PlayerEventId,Name,Description,StartTime,EndTime,CreatorPlayerId,AttendeeCount,RoomId,ImageName,Accessibility}. Missing vs HPIOAGDJHDH: SubRoomId, ClubId, IsMultiInstance, SupportMultiInstanceRoomChat, DefaultBroadcastPermissions, CanRequestBroadcastPermissions, BroadcastingRoomInstanceId (Utf8Json leaves defaults: false/0/null). AttendeeCount hardcoded 0, Accessibility hardcoded 1, ImageName always "". includeDetails=true (MDCBEPJCJPO) never gets a Tags key -> Tags list null on the details page.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:49`

**Fix.** Extend ToWire with SubRoomId/ClubId/IsMultiInstance/SupportMultiInstanceRoomChat/DefaultBroadcastPermissions/CanRequestBroadcastPermissions/BroadcastingRoomInstanceId (persist them on PlayerEventEntity instead of the PlayerSettings side-table), compute AttendeeCount from PlayerEventResponses, and emit Tags:[{Tag,Type}] when includeDetails=true.

##### `POST api/playerevents/v1/bulk` — SHAPE_MISMATCH (degraded)

POST registered; ReadEventIdsAsync (line 627) reads repeated form 'Ids' correctly. Response uses the shared ToWire, so each event is missing the seven HPIOAGDJHDH keys listed for v1/{0} (multi-instance flags, ClubId, etc.) and AttendeeCount is always 0.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:152`

**Fix.** No route change; fix the shared ToWire shape.

##### `GET api/playerevents/v1/room/{0}` — SHAPE_MISMATCH (degraded)

Route and verb OK; array response OK; items use the shared incomplete ToWire (see v1/{0}).

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:87`

**Fix.** Fix shared ToWire.

##### `GET api/playerevents/v1/clubs` — SHAPE_MISMATCH (degraded)

Verb/array OK, but the client's repeated 'id' query params (one per club id) are completely ignored — the handler returns events derived from ALL club members' rooms, not the requested clubs. Items also use the incomplete ToWire, and ClubId is never emitted so the client can't bucket events per club.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:123`

**Fix.** Read the repeated 'id' query values, filter events by ClubId (requires persisting ClubId on the event entity), and emit ClubId in the wire object.

##### `GET api/playerevents/v1/search` — SHAPE_MISMATCH (degraded)

Verb OK; 'query' param handled (line 66). 'sort' (PJBLEKMMACM enum) and 'scheduleFilter' (BJBLPLKMLBE enum) are ignored — results always start-time ordered regardless of the chip the user picks. Items use incomplete ToWire.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:58`

**Fix.** Honor sort/scheduleFilter query ints; fix shared ToWire.

##### `GET api/playerevents/v1/searchlive` — SHAPE_MISMATCH (degraded)

Shares the Search handler. Client DTO CLDDIKOJMAM additionally reads PlayerCount (Int32) and IsFull (Boolean); neither is emitted, so every happening-now event shows 0 players and never-full. Handler also doesn't restrict to currently-live events (only EndsAt > now, includes future).

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:59`

**Fix.** Give searchlive its own handler filtered to StartsAt <= now < EndsAt, emitting ToWire + PlayerCount (from presence) + IsFull.

##### `GET api/playerevents/v1/tagfilters` — SHAPE_MISMATCH (degraded)

Emits PinnedFilters and PopularFilters only (lines 166-170). Client AKCLLEJNFFD also reads TrendingFilters:[String]; missing key deserializes to a null list — NRE risk when the chips UI iterates it.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:165`

**Fix.** Add TrendingFilters = new[]{...} (can duplicate PopularFilters).

##### `PUT api/playerevents/v2/{0}/accessibility` — SHAPE_MISMATCH (degraded)

PUT registered and 'accessibility' form field read, but (a) response is the flat ToWire event, not the PHHAKLPGNGC wrapper {PlayerEvent,Result,TagModifyResult} the client reads -> PlayerEvent=null/Result=0 after save; (b) the value is written to a PlayerSettings side-table (SetEventSettingAsync line 759) while ToWire hardcodes Accessibility=1, so the change never reflects on any read — an effective stub.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:455`

**Fix.** Persist Accessibility on PlayerEventEntity, return the PHHAKLPGNGC wrapper from all v2 field mutations.

##### `PUT api/playerevents/v2/{0}/name` — SHAPE_MISMATCH (degraded)

PUT + 'name' field OK and persisted to Title; response is flat ToWire instead of PHHAKLPGNGC wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:387`

**Fix.** Return the wrapper.

##### `PUT api/playerevents/v2/{0}/description` — SHAPE_MISMATCH (degraded)

PUT + 'description' field OK; flat ToWire response instead of wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:401`

**Fix.** Return the wrapper.

##### `PUT api/playerevents/v2/{0}/image` — SHAPE_MISMATCH (degraded)

PUT + 'imageName' read, but value goes to the PlayerSettings side-table while ToWire hardcodes ImageName="" (line 272) — image never persists visibly; flat response instead of wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:445`

**Fix.** Persist ImageName on the entity; return wrapper.

##### `PUT api/playerevents/v2/{0}/room` — SHAPE_MISMATCH (degraded)

PUT + 'roomId' OK and persisted; client's optional 'subRoomId' form field is ignored (SubRoomId not stored/emitted); flat response instead of wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:414`

**Fix.** Read+persist subRoomId; return wrapper.

##### `PUT api/playerevents/v2/{0}/club` — SHAPE_MISMATCH (degraded)

PUT registered, but client omits clubId entirely to CLEAR the club and the handler returns 400 missing_club when absent (line 496) — detaching a club is impossible. Value also goes to the side-table (never emitted, ToWire has no ClubId key); flat response instead of wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:490`

**Fix.** Treat absent clubId as clear (null), persist ClubId on the entity, emit it in ToWire, return wrapper.

##### `PUT api/playerevents/v2/{0}/time` — SHAPE_MISMATCH (degraded)

PUT + startTime/endTime fields OK and persisted; flat ToWire response instead of PHHAKLPGNGC wrapper.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:429`

**Fix.** Return the wrapper.

##### `GET playlists/visitedby/me` — STUB (degraded)

Hardcoded Ok(Array.Empty<object>()) — visits are never tracked, so the recently-visited playlists tab is permanently empty. Documented in-code as a placeholder.

Handler: `DorkNet.Server/Controllers/API/Playlists/PlaylistsController.cs:232`

**Fix.** Track playlist visits (PlaylistVisitEntity or derive from member-room visits) and return the visited union entries.

##### `GET api/progressionEvents/record/{progressionEventId}` — SHAPE_MISMATCH (degraded)

Emits {ProgressionEventId,Xp,ClaimedRewardIndex,PurchasedXpBoostCount,DailyBoostGameplayMinutes,XpBoostExpiresAt}; the 2023 ProgressionEventRecordDTO reads {AccountId,Xp,GameMinutesToday,RewardsCollected,BonusRewardsCollected,XpBoostLastPurchasedAt}. Only Xp overlaps (and it's hardcoded 0) — client sees an all-default record; progression track always shows zero progress/rewards. Route existing at least avoids the 404 dorm-loop trap.

Handler: `DorkNet.Server/Controllers/API/ProgressionEvents/ProgressionEventsController.cs:85`

**Fix.** Rename keys to AccountId/Xp/GameMinutesToday/RewardsCollected/BonusRewardsCollected/XpBoostLastPurchasedAt and back Xp/RewardsCollected with real stored values.

##### `GET api/progressionEvents/{eventId}/xpboosts` — MISSING (degraded)

Not registered -> 404. XP-boost purchase sheet fails to load; client expects a JSON array of {ProgressionEventPurchasableXpBoostId(guid),Cost,XpMultiplier,XpCap,LookbackDurationTicks,CooldownDurationTicks}. Degraded rather than fatal because event/{id} advertises UsesBoost=false and PurchasableXpBoostId=null, so the UI shouldn't normally open the sheet.

**Fix.** Add [HttpGet("api/progressionEvents/{eventId:long}/xpboosts")] returning [] (or real boosts if the feature is enabled).

##### `GET api/progressionEvents/{eventId}/xpboosts/{boostId}/previewEarnedXp` — MISSING (degraded)

Not registered -> 404. Boost purchase preview fails; client expects a bare JSON integer. Same mitigation as xpboosts (UsesBoost=false).

**Fix.** Add [HttpGet("api/progressionEvents/{eventId:long}/xpboosts/{boostId:guid}/previewEarnedXp")] returning Ok(0) or a computed value.

##### `POST api/playerevents/v1/bulkInvite` — CASING_MISMATCH (cosmetic)

Verb, request DTO {PlayerEventId,InvitedPlayerIds} and top-level response {FailedInvites,Result} all match. Failed-entry keys are {PlayerId,Error} (line 550) but the client FOOJAOBCLBA reads {InvitedPlayerId:int,Result:int} — only hit when an invited player doesn't exist (entry deserializes as defaults, no crash). Error paths NotFound()/Forbid() return non-JSON bodies the client can't parse.

Handler: `DorkNet.Server/Controllers/API/PlayerEvents/PlayerEventsController.cs:532`

**Fix.** Change failed entries to new { InvitedPlayerId = rid, Result = <BNCFHOOCHAI code> } and return {FailedInvites,Result:err} JSON instead of NotFound/Forbid.

### Inventions, images, video and studio assets

`inventions-media`

Audited 40 real client routes against DorkNet. Hard breaks: POST api/inventions/v4/addversion 400s on every call (server binds "BlobName", client sends "inventionDataFilename"); POST api/inventions/v1/settags 400s (server binds tag lists as strings and wrong key name); api/inventions/v1/dormskinsfromids returns invention objects where the client deserializes List<Int64>; POST api/images/v1/cheer and sendlink 400 (wrong request keys); modifyaccessibility silently forces every photo private; api/images/v5/cheered/bulk has no GET so cheer-states 404 in the common <100-id case; GET api/images/v6/{id} and POST api/images/v1/{id}/report are unrouted; showcase/{accountId} and remote-run/push-to-studio are missing entirely. Systemic shape gaps: the shared invention wire builder (InventionsController.ToWire) omits the 2023 client's Accessibility and IsCertifiedInvention keys and version objects omit ChipsCost/CloudVariablesCost/BlobHash; the GET-mutation family (update/delete/publish/unpublish/updateprice/addversion/cheer) returns bare invention objects instead of the {Status,Invention,InventionVersion} envelope; the shared image builder (ImagesController.BuildImageInfo) omits SavedImageId/SavedImageType/ClubId so photo ids read as 0 in feeds; every bulk endpoint's POST fallback (>=100 ids) expects JSON but the client posts form fields (415). updateprice is GET-only but the client POSTs (405). unity_assets/{id}/{t}/{v} serves raw bundle bytes where the traced rooms-service method expects a JSON descriptor.

**Client-side notes.** TRANSPORT CONVENTIONS (grounded): All RecNet.Runtime requests go through builder BNDIAONDFFF..ctor(HTTPMethods verb, GJDLNNLKDIJ host, string route) at RVA 0x30036A0 (recnet-runtime-decomp/BNDIAONDFFF.cs:194); verb is BestHTTP HTTPMethods (0=GET, 2=POST) moved into rdx immediately before the ctor call; host enum is almost always 1 (main API). AFGEDDANEKP(name,value) fields become the QUERY STRING on GET and form/body fields on POST (Build method FGHNOKLDOKO assembles \"?\"/\"&\" — BNDIAONDFFF.txt:3246-3251). FJLLPHFOOJJ(string) sets a raw JSON body. BPHHLAIILHP adds a multipart file part. Bulk endpoints pick the verb at runtime via ALHIJCJOLCB.JIECAFGCODK(count, threshold=100): GET when the id list is short, POST when >=100 (ALHIJCJOLCB.cs:110; e.g. KLJOGJHBONK_NestedType_HKPGODOKOJP.txt:222-224 'Move rsi,2 / Compare rax,100 / cmovl esi,r14d') — DorkNet must accept BOTH verbs on every bulk route. RESPONSE KEY CASING: generated per-DTO serializers cache three variants of every key (PascalCase/camelCase/lowercase) in their .ctor (e.g. BLADEIPEPOH.txt), so responses may use any of the three casings; PascalCase documented. REQUEST BODY CASING IS MIXED AND EXACT (JsonUtility uses raw field names): ModifyTagsRequest/UpdatePriceRequest/ReportRequest/CheerRequest are PascalCase, but NewInventionRequestDTO/AddVersionInventionRequestDTO are camelCase — matches memory note 'watch JSON key casing varies per DTO'. Mutations on inventions v1/update, v1/delete, v3/publish, v1/unpublish are genuinely GET-with-query in this client. DateTime wire format not verified from binary (reader parses a JSON string; format UNKNOWN — DorkNet's current ISO output is presumably accepted since these pages work). SERVER GAPS FOUND vs the server's reflected route table: (1) GET api/images/v6/{id} not registered (only bare api/images/v6) — photo tagged-players lookup will 404; (2) POST api/images/v1/{id}/report missing — photo reporting 404s; (3) api/images/v5/cheered/bulk registered POST-only but client sends GET when <100 ids (the common case); (4) api/inventions/v1/updateprice registered GET-only but client POSTs JSON UpdatePriceRequest; (5) api/inventions/v1/fulllineageowner and v1/fromcreators registered GET-only — fine until an id list reaches 100, then client switches to POST; (6) showcase/{accountId} (rooms-service client, returns JSON array of room ids) not registered; (7) unity_assets/{name}/{target}/{version} (returns {UnityAssetId,Target,Version,Filename,Hash}) not registered under that path; (8) remote-run/push-to-studio not registered (Rec Room Studio dev flow — optional for normal gameplay). Non-routes in the group: 'api/images/' and 'api/inventions/' are format prefixes; 'inventionsbycreators/{0}/{1}/{2}' is a memoization cache key; 'image/{0}' is a rec.net share deeplink. Enum value tables extracted for server use: CFHBHALNFKC (invention op Status, 0=Success... includes AlreadyCheered/InvalidPrice etc., CFHBHALNFKC.cs), OFLPPEFGGOP (settags Result), JJDFPDDKLMO (permission, gap-skipping 0/10/15/20/40/60/80/100), GGEKBCHLINO (invention accessibility 0=Private,1=Public,2=Unlisted), KIKIEHKBHNM (tag type), EIHGOODFAOG (report category, -1..4), OMMBGJMJJPN (creatorAccountRole), GCEMFHEDHPB (image accessibility), AOJNEMGCDPM (image type 0-7), GGOOKOOBDBL (sort), JMLFDGEMALD (room photo filter). Decompiled RecNet.Runtime sources live in C:/tmp/recnet-runtime-decomp (NOT in C:/tmp/recroom-2023-03-21-decompiled, which lacks RecNet.Runtime types).

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/images/v1/cheer` | JSON body: {"SavedImageId": Int64, "Cheer": Boolean} | response body not consumed |
| POST | `api/images/v1/deletesaved` | JSON body: {"ImageName": String} | response body not consumed |
| GET | `api/images/v1/listsaved` | none (auth user implied) | OJOIJKLINID object: {"Images": List<String>} — projected client-side to List<string> of image blob names |
| POST | `api/images/v1/modifyaccessibility` | JSON body: {"ImageName": String, "Accessibility": Int32 (0=Private,1=Public,2=FriendsOnly)} | response body not consumed (client awaits status only) |
| POST | `api/images/v1/sendlink` | JSON body: {"ImageName": String} | response body not consumed |
| GET | `api/images/v1/slideshow` | none | {"ValidTill":DateTime string,"Images":[{"SavedImageId":Int64,"ImageName":String,"Username":String,"PlayerId":Int32,"RoomName":String,"RoomId":Int64?}]} |
| POST | `api/images/v1/{id}/report` | none (empty body; id in path) | response body not consumed (fire-and-forget KDOPJCNKOOK) |
| GET | `api/images/v2/named` | none | JSON array: [{"FriendlyImageName":String,"ImageName":String,"StartTime":DateTime string,"EndTime":DateTime string}] |
| GET | `api/images/v4/room/{roomId}?sort&filter&take&skip` | path roomId (Int64); query fields "sort" Int32 (GGOOKOOBDBL), "filter" Int32 (JMLFDGEMALD 0=VisibleToPlayer,1=PublicOnly,2=PublicLocalPlayerTagged), "take" Int32, "skip" Int32 | JSON array of ICOFKEGOGOD (13 keys as in v5/bulk) |
| POST | `api/images/v4/uploadsaved` | multipart/form-data: file part name "image", filename "file.bin" (JPEG bytes, quality 80); form field "imgMeta" = JsonUtility JSON of SavedImageMetaDTO {"playerIds":List<Int32>,"sa | DABHPBPDBBK object: {"ImageName": String} — projected to string |
| GET | `api/images/v5/bulk` | field "ids" = list of Int64; sent as GET query when count<100, as POST form when >=100 (ALHIJCJOLCB.JIECAFGCODK(count,100)) — server must accept BOTH verbs | JSON array of ICOFKEGOGOD: [{"SavedImageId":Int64,"ImageName":String,"PlayerId":Int32,"RoomId":Int64?,"PlayerEventId":Int64?,"ClubId":Int64?,"Description":String,"Accessibility":Int32,"AccessibilityLocked":Boolean,"Saved |
| GET | `api/images/v5/cheered/bulk` | field "id" = list of Int64 saved-image ids; GET query when count<100 else POST form (JIECAFGCODK) | JSON array: [{"SavedImageId":Int64,"IsCheered":Boolean}] |
| GET | `api/images/v5/player/{playerId}?sort={int}` | path playerId (Int32 account id), query field "sort" Int32 (GGOOKOOBDBL 0=CreatedAt_Desc,1=CheerCount_Desc,2=CreatedAt_Asc) | JSON array of ICOFKEGOGOD (13 keys as in v5/bulk) |
| GET | `api/images/v6/{id}` | path id (Int64), no query | LGLCPNPJCEC object: {"Id":Int64,"ImageName":String,"PlayerId":Int32,"RoomId":Int64?,"PlayerEventId":Int64?,"Accessibility":Int32,"AccessibilityLocked":Boolean,"Type":Int32 (AOJNEMGCDPM),"CreatedAt":DateTime string,"Tagge |
| GET | `api/images/v6?name={imageName}` | query: name=String (image blob name) | single LGLCPNPJCEC object (same 12 keys as api/images/v6/{id}) |
| POST | `api/inventions/v1/cheer` | JSON body CheerRequest: {"InventionId":Int64,"Cheer":Boolean} | BDNCJIPHHOK |
| GET | `api/inventions/v1/delete?inventionId={id}` | query: inventionId=Int64 (mutation via GET) | BDNCJIPHHOK (Status/Invention/InventionVersion as above) |
| GET | `api/inventions/v1/details?inventionId={id}` | query: inventionId=Int64 | OIABGAKJABE: {"Tags":[{"Tag":String,"Type":Int32 (KIKIEHKBHNM 0=General,1=Auto,2=AGOnly,3=Banned)}]} |
| GET | `api/inventions/v1/dormskinsfromids` | field "ids" = list of Int64 invention ids; GET query when <100 else POST form (JIECAFGCODK) | JSON array of Int64 (the subset of ids that are dorm skins) |
| GET | `api/inventions/v1/featured` | none | JSON array of IFJONDCAKKM |
| GET | `api/inventions/v1/featureddormskins` | none | JSON array of IFJONDCAKKM |
| GET | `api/inventions/v1/fromcreators` | field "id" = list of Int32 creator account ids (GET query when <100 else POST via JIECAFGCODK), "skip"=Int32, "take"=Int32 | JSON array of IFJONDCAKKM (22-key objects) |
| GET | `api/inventions/v1/fulllineageowner` | field "id" = list of Int64 invention ids; GET query when <100 else POST (generic list AFGEDDANEKP overload) | bare JSON Boolean (true if player owns full lineage of all ids) |
| GET | `api/inventions/v1/personaldetails/{id}` | path id=Int64 | CEAFHBOOBKL: {"IsCheering":Boolean} |
| POST | `api/inventions/v1/report` | JSON body ReportRequest: {"InventionId":Int64,"Details":String,"ReportCategory":Int32 (EIHGOODFAOG -1=Unknown,0=CoC_Discriminatory,1=CoC_Sexual,2=CoC_Trolling,3=Misleading,4=Other) | PHMHCPEMABG: {"Success":Boolean,"Message":String} |
| GET | `api/inventions/v1/room?id={roomId}` | query: id=Int64 room id | JSON array of IFJONDCAKKM (inventions used/placed in the room) |
| POST | `api/inventions/v1/settags` | JSON body ModifyTagsRequest: {"InventionId":Int64,"AutoTags":[String],"CustomTags":[String]} (PascalCase, JsonUtility field names) | PNGLFHEAJIH: {"Result":Int32 (OFLPPEFGGOP 0=Success,1=TooManyTags,2=TagUseRestricted,3=InvalidTag,4=InappropriateTag,5=TagTooLong,6=TagNotFound,7=TagAlreadyExists,8=NoChange,9=TagRepeated,10=LacksPermission,11=RoomDoesNo |
| GET | `api/inventions/v1/tagfilters` | none (enclosing method takes an IFFEGNLLPDI context enum but no field is attached before send) | AKCLLEJNFFD: {"PinnedFilters":[String],"PopularFilters":[String],"TrendingFilters":[String]} — projected to one List<string> |
| GET | `api/inventions/v1/toptoday` | none | JSON array of IFJONDCAKKM |
| GET | `api/inventions/v1/unpublish?inventionId={id}` | query: inventionId=Int64 | BDNCJIPHHOK |
| GET | `api/inventions/v1/update` | query: inventionId=Int64 plus exactly one of name=String \| description=String \| imgName=String \| permission=Int32 (JJDFPDDKLMO 0=Unassigned,10=LimitedOneUseOnly,15=DisallowKeyLo | BDNCJIPHHOK: {"Status":Int32 (CFHBHALNFKC 0=Success,1=InvalidParameters,...26=PriceCannotBeChanged...),"Invention":IFJONDCAKKM object\|null,"InventionVersion":PLIKEBBPJGI object\|null} |
| POST | `api/inventions/v1/updateprice` | JSON body UpdatePriceRequest: {"InventionId":Int64,"Price":Int32} | BDNCJIPHHOK |
| GET | `api/inventions/v1/version?inventionId={id}&version={n}` | query: inventionId=Int64, version=Int32 (URL preformatted by client) | single PLIKEBBPJGI object (9 keys as above) |
| GET | `api/inventions/v1/versions?inventionId={id}` | query: inventionId=Int64 | JSON array of PLIKEBBPJGI: [{"InventionId":Int64,"ReplicationId":String (guid),"VersionNumber":Int32,"InstantiationCost":Int32,"LightsCost":Int32,"ChipsCost":Int32,"CloudVariablesCost":Int32,"BlobName":String,"BlobHash": |
| GET | `api/inventions/v1?inventionId={id}` | query: inventionId=Int64 (URL preformatted "{0}v1?inventionId={1}") | single IFJONDCAKKM object (22 keys) |
| GET | `api/inventions/v2/batch` | field "id" = list of Int64 invention ids; GET query when <100 else POST (JIECAFGCODK) | JSON array of IFJONDCAKKM |
| GET | `api/inventions/v2/mine` | none (auth user) | JSON array of IFJONDCAKKM |
| GET | `api/inventions/v2/search?value&skip&take` | query: value=String (search text), skip=Int32 (default 0), take=Int32 (default 100) | JSON array of IFJONDCAKKM: [{"InventionId":Int64,"ReplicationId":String,"CreatorPlayerId":Int32,"Name":String,"Description":String,"ImageName":String,"CurrentVersionNumber":Int32,"Accessibility":Int32 (GGEKBCHLINO),"Modi |
| GET | `api/inventions/v3/publish?inventionId&permissionLevel&accessibility&price` | query: inventionId=Int64, permissionLevel=Int32 (JJDFPDDKLMO), accessibility=Int32 (IFJONDCAKKM/GGEKBCHLINO 0=Private,1=Public,2=Unlisted), price=Int32 (nullable, only when set) | BDNCJIPHHOK |
| POST | `api/inventions/v4/addversion` | JSON body AddVersionInventionRequestDTO (camelCase): {"inventionId":Int64,"instantiationCost":Int32,"lightsCost":Int32,"chipsCost":Int32,"cloudVariablesCost":Int32,"aiCost":Int32," | BDNCJIPHHOK |
| POST | `api/inventions/v6/save` | JSON body NewInventionRequestDTO (camelCase JsonUtility field names): {"name":String,"description":String,"imageName":String,"instantiationCost":Int32,"lightsCost":Int32,"chipsCost | BDNCJIPHHOK |
| POST | `remote-run/push-to-studio` | JSON body FNAGBPCAGJD: {"SessionId":String,"RoomId":Int64,"SubRoomId":Int64,"UnityAssetId":String,"RoomData":{"Filename":String,"Hash":String,"OwnershipProof":String},"SubRoomData" | CEELGOLBHIL: {"SessionId":String,"RoomId":Int64?,"SubRoomId":Int64?,"UnityAssetId":String,"RoomDataFilename":String,"RoomDataHash":String,"SubRoomDataFilename":String,"SubRoomDataHash":String} |
| GET | `showcase/{0}` | path {0}=Int32 account id; no query. Issued on the rooms-service client (NLDBPDCNNCF, same client as rooms/, rooms/magic_door, photon_access_token) | bare JSON array of Int64 room ids |
| GET | `unity_assets/{0}/{1}/{2}` | path: {0}=URL-encoded unity asset name/id (String), {1}=target (Byte, platform), {2}=version (Int32); rooms-service client | PPBIFMDLDCB: {"UnityAssetId":String,"Target":Byte,"Version":Int32,"Filename":String,"Hash":String} |
| GET | `video/` | URL = {media base from PAPLNIPKAMG config} + "video/" + {filename}; plain file fetch (mp4 bytes), no query/body | raw video bytes (no JSON) |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/images/` — n/a
- `api/inventions/` — n/a
- `image/{0}` — Sharing a photo (generates web link)
- `inventionsbycreators/{0}/{1}/{2}` — memoization key for GetInventionsByCreators

#### Defects

##### `POST api/images/v1/modifyaccessibility` — SHAPE_MISMATCH (breaks-gameplay)

Client body is {"ImageName":String,"Accessibility":Int32} (0=Private,1=Public,2=FriendsOnly). Server's ModifyAccessibilityRequest has no Accessibility member — it reads bool IsPublic, which the client never sends, so isPublic evaluates to body.IsPublic default = false on EVERY request. Result: choosing "Public" in the photo privacy UI silently sets the photo private; the toggle can only ever privatize.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:626`

**Fix.** Add `public int? Accessibility { get; set; }` to ModifyAccessibilityRequest and compute isPublic = body.Accessibility is int a ? a == 1 : (body.IsPublic/form fallback). Treat 2 (FriendsOnly) as non-public until a 3-state column exists.

##### `POST api/inventions/v1/settags` — SHAPE_MISMATCH (breaks-gameplay)

Client body is {"InventionId":Int64,"AutoTags":[String],"CustomTags":[String]}. Server binds SetTagsRequest(long InventionId, string? AutoTags, string? PlayerAddedTags): AutoTags is a JSON ARRAY bound to string → System.Text.Json model-binding fails → automatic 400; and the second list key is CustomTags, not PlayerAddedTags, so it would be dropped anyway. Response is also wrong: bare ToWire invention instead of PNGLFHEAJIH {"Result":Int32,"Tags":[String]}. Net effect: setting tags in the publish/edit flow always fails.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:669`

**Fix.** Change SetTagsRequest to (long InventionId, List<string>? AutoTags, List<string>? CustomTags); store the union in TagsCsv; return Ok(new { Result = 0, Tags = combinedList }).

##### `POST api/inventions/v4/addversion` — SHAPE_MISMATCH (breaks-gameplay)

Client body keys are camelCase {"inventionId","instantiationCost","lightsCost","chipsCost","cloudVariablesCost","aiCost","creationRoomId","inventionDataFilename","referencedInventions"}. Server's AddVersionRequest requires "BlobName" — a key the client NEVER sends (its filename key is inventionDataFilename) — so BlobName binds null and the handler returns 400 "missing BlobName" on every call: saving a new version of any existing invention always fails (maker-pen re-save of an invention is dead). Response is also a bare ToWire, not the BDNCJIPHHOK envelope.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:512`

**Fix.** Add InventionDataFilename (and ChipsCost/CloudVariablesCost/AiCost/CreationRoomId/ReferencedInventions) to the request; use InventionDataFilename ?? BlobName as the blob; return {Status=0, Invention=ToWire(inv), InventionVersion=ToVersionWire(newVersion)}.

##### `POST api/images/v1/sendlink` — SHAPE_MISMATCH (degraded)

Client body is only {"ImageName":String} ('send ME a link'). Server's SendLinkRequest has PhotoId/ImageId/RecipientIds and no ImageName, so photoId stays 0 and the handler returns 400 BadRequest — the share action's promise rejects every time.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:701`

**Fix.** Add ImageName to SendLinkRequest; when photoId==0 resolve the photo by BlobName (scoped to caller), and when RecipientIds is empty deliver the link to the calling player (self-DM / notification), returning 200.

##### `POST api/images/v1/cheer` — SHAPE_MISMATCH (degraded)

Client body is {"SavedImageId":Int64,"Cheer":Boolean}. Server's CheerImageRequest only has ImageId/PhotoId, so photoId=0 → 400 on every request: photo cheering never works. Even with the id fixed, the Cheer=false (uncheer) case is unsupported — the handler only adds cheers.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:568`

**Fix.** Add SavedImageId (long) and Cheer (bool, default true) to CheerImageRequest; use SavedImageId as photoId; when Cheer==false delete the CheerEntity row and decrement Photos.CheerCount.

##### `GET api/images/v6/{id}` — MISSING (degraded)

Only bare GET api/images/v6 (query ?name=) is registered. api/images/v6/123 falls into the [HttpGet("api/images/{*path}", Order=100)] byte catch-all, which rejects "123" as a non-image filename → 404. The photo-detail tagged-players lookup (HLANOFILAEO) fails on every photo.

**Fix.** Add [HttpGet("api/images/v6/{id:long}")] to ImagesController resolving db.Photos by Id (public or owned) and returning BuildImageInfo(photo, photo.UploaderPlayerId) — BuildImageInfo already carries all 12 LGLCPNPJCEC keys (Id, ImageName, PlayerId, RoomId, PlayerEventId, Accessibility, AccessibilityLocked, Type, CreatedAt, TaggedPlayerIds, CheerCount, CommentCount).

**Status: FIXED.** `ImagesController.ImageByIdV6` (`[HttpGet("api/images/v6/{id:long}")]`, anonymous, public-or-owner) returns a single BuildImageInfo object. Key list re-confirmed against the generated serializer `IBILPLGNAJE.txt:819-1078`.

##### `GET api/images/v5/bulk` — SHAPE_MISMATCH (degraded)

Two defects. (1) Response: items are BuildImageInfo objects but the client deserializes ICOFKEGOGOD whose keys are SavedImageId / SavedImageType / ClubId / Description — the builder sends Id and Type instead of SavedImageId and SavedImageType and has no ClubId, so the client's photo id reads as default 0 (or the strict reader throws on the missing non-nullable Int64 — throw-vs-default UNKNOWN); every downstream per-photo action (cheer, detail, delete) then targets id 0. (2) POST fallback (>=100 ids): client posts FORM field "ids", but the action binds [FromBody] JSON → ASP.NET returns 415 for form content.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:283`

**Fix.** Add SavedImageId = p.Id, SavedImageType = 0, ClubId = (long?)null to BuildImageInfo (keep existing aliases). For POST, read Request.Form["ids"] when HasFormContentType instead of relying on [FromBody].

##### `GET (POST when >=100 ids) api/images/v5/cheered/bulk` — VERB_MISMATCH (degraded)

Registered POST-only. The client sends GET with query field "id" whenever the id list is <100 (the overwhelmingly common case) → the request falls into the api/images/{*path} catch-all and 404s, so photo cheer-state (feed hearts) never loads. The POST variant is also broken for this client: it binds [FromBody] JSON {SavedImageIds} but the client posts form field "id" → 415. Response shape itself is correct ([{SavedImageId, IsCheered}]).

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:539`

**Fix.** Add [HttpGet("api/images/v5/cheered/bulk")] (and v4) parsing ids from Request.Query["id"] (comma-split all values); on POST also accept Request.Form["id"].

**Status: FIXED.** `ImagesController.CheeredBulk` is now registered GET+POST on v4 and v5 and takes no bound parameter at all — ids come from `ReadCheerBulkIdsAsync`, which reads query values, then form values when the body is form-encoded, then a JSON body (array or object) otherwise. Dropping `[FromBody]` is what removes the 415 on the >=100-id form POST.

##### `GET api/images/v1/slideshow` — SHAPE_MISMATCH (degraded)

Top level {ValidTill, Images} is right, but each Images item must be NBNFGGCGCLP {SavedImageId, ImageName, Username, PlayerId, RoomName, RoomId}. BuildImageInfo supplies ImageName/PlayerId/RoomId but has NO SavedImageId (sends Id), NO Username, NO RoomName — the slideshow reader gets default 0/null for those (or throws on the non-nullable SavedImageId — UNKNOWN), so slideshow entries lose attribution or fail to render.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:338`

**Fix.** Build a slideshow-specific projection: join db.Players on UploaderPlayerId for Username and db.Rooms on RoomId for RoomName, and emit SavedImageId = p.Id.

##### `GET api/images/v5/player/{playerId}?sort={int}` — SHAPE_MISMATCH (degraded)

Route+verb exist and int sort binds (ParseSort accepts numerics). Two defects: (1) items are ICOFKEGOGOD → same missing SavedImageId/SavedImageType/ClubId keys as v5/bulk (shared BuildImageInfo); (2) client sort enum 2 = CreatedAt_Asc but ParseSort maps 2 → ViewCount-desc, so the 'oldest first' sort returns the wrong order (cosmetic).

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:408`

**Fix.** Fix BuildImageInfo keys (see v5/bulk) and map sort==2 to OrderBy(p => p.CreatedAt) ascending.

##### `GET api/images/v4/room/{roomId}?sort&filter&take&skip` — SHAPE_MISMATCH (degraded)

Route/verb/params exist (sort+filter bind as strings, ints parse; take/skip honored). Same two defects as the player list: ICOFKEGOGOD items miss SavedImageId/SavedImageType/ClubId (shared BuildImageInfo), and sort=2 (CreatedAt_Asc) maps to ViewCount-desc. filter is intentionally ignored (always PublicOnly semantics) — that drops filter=0 VisibleToPlayer (own private/tagged photos in the room list): minor behavioral gap.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:440`

**Fix.** Same BuildImageInfo key additions + sort==2 ascending; optionally honor filter=0 by including the caller's own private photos.

##### `POST api/images/v1/{id}/report` — MISSING (degraded)

No route matches; the request lands in the api/images/{*path} catch-all and 404s. The client fires-and-forgets so the UI doesn't visibly break, but every photo report is silently lost — a moderation-integrity defect on a multi-tenant server.

**Fix.** Add [HttpPost("api/images/v1/{id:long}/report")] [Authorize] that inserts a ReportEntity (ReporterPlayerId = caller, TargetPhotoId = id, TargetPlayerId = photo.UploaderPlayerId) and returns 200 with any body.

**Status: FIXED.** `ImagesController.ReportPhoto`. ReportEntity has no TargetPhotoId column and this round adds no schema, so the photo id + blob name ride in `Message` as `[photo {id} {blobName}] …` (the same convention `ClubService.AddClubReportAsync` uses for `[club {id}]`); TargetPlayerId = uploader, RoomId = the photo's room, Category = 5 (Other — the wire carries no category). Adding a `TargetPhotoId` column later would let the admin queue link straight to the image.

##### `GET api/inventions/v1/update` — SHAPE_MISMATCH (degraded)

Route, GET verb, and all four one-of query params (name/description/imgName/permission) bind and persist correctly. But the response is a bare ToWire invention object; the client deserializes BDNCJIPHHOK {"Status":Int32,"Invention":obj,"InventionVersion":obj} — none of those keys exist in the reply, so Status reads default (or the strict reader throws — UNKNOWN) and Invention is null, leaving the edit UI without the updated entity. Additionally the Invention payload itself is missing keys (see the ToWire finding).

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:545`

**Fix.** Return Ok(new { Status = 0, Invention = ToWire(inv), InventionVersion = (object?)null }) — same envelope SaveV4 already uses at line 499.

##### `GET (POST when >=100 ids) api/inventions/v1/dormskinsfromids` — SHAPE_MISMATCH (degraded)

GET route exists and the query parser picks up the "ids" field, but it is aliased onto the generic batch handler which returns an ARRAY OF INVENTION OBJECTS; the client deserializes List<Int64> (the subset of ids that are dorm skins) → object-where-number-expected breaks the reader, so dorm-skin filtering of owned/featured inventions fails. The POST alias (line 135) binds [FromBody] JSON while the client posts form "ids" → 415. There is also no dorm-skin flag on InventionEntity to compute the real subset.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:143`

**Fix.** Give dormskinsfromids its own handler returning long[]: add an IsDormSkin flag (or tag convention) to InventionEntity and return the requested ids whose inventions match; accept form "ids" on POST. Returning [] is shape-safe interim behavior but disables dorm skins.

##### `GET api/inventions/v1/details?inventionId={id}` — SHAPE_MISMATCH (degraded)

Route+verb+param OK, but response is {Invention, Versions}; the client deserializes OIABGAKJABE {"Tags":[{"Tag":String,"Type":Int32}]}. No Tags key → tags list null/empty (or reader throw — UNKNOWN); the invention detail page's tag display is dead.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:370`

**Fix.** Return Ok(new { Tags = inv.TagsCsv.Split(',', RemoveEmpty).Select(t => new { Tag = t, Type = 0 }) }) — extra keys are harmless, missing Tags is not.

##### `GET api/inventions/v1/versions?inventionId={id}` — SHAPE_MISMATCH (degraded)

Route OK; each item must be PLIKEBBPJGI with 9 keys but ToVersionWire emits only 6: ChipsCost (Int32), CloudVariablesCost (Int32), and BlobHash (String) are missing. The two non-nullable ints read as 0 (or throw — UNKNOWN); a missing BlobHash risks failing the client's blob integrity/download step for invention data.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:401`

**Fix.** Add ChipsCost/CloudVariablesCost/BlobHash columns to InventionVersionEntity (populate hash at upload time; costs from the save/addversion request's chipsCost/cloudVariablesCost) and emit all three in ToVersionWire (BlobHash may be computed on demand from stored bytes as a stopgap).

##### `GET api/inventions/v1/version?inventionId={id}&version={n}` — SHAPE_MISMATCH (degraded)

Route and both query params OK; single object uses the same ToVersionWire missing ChipsCost/CloudVariablesCost/BlobHash. This is the pre-download version resolve, so a hash-checking client path would fail here.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:411`

**Fix.** Same ToVersionWire fix as /versions.

##### `GET api/inventions/v1/delete?inventionId={id}` — SHAPE_MISMATCH (degraded)

GET mutation exists and soft-deletes correctly, but returns bare ToWire instead of the BDNCJIPHHOK envelope {Status,Invention,InventionVersion} the client deserializes.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:594`

**Fix.** Wrap: Ok(new { Status = 0, Invention = ToWire(inv), InventionVersion = (object?)null }).

##### `GET api/inventions/v3/publish?inventionId&permissionLevel&accessibility&price` — SHAPE_MISMATCH (degraded)

Route exists but reads only inventionId+permissionLevel: the accessibility query param (0=Private,1=Public,2=Unlisted) and nullable price are silently ignored — publishing as Unlisted/Private still publishes fully public, and paid publishes lose their price. Response is bare ToWire, not the BDNCJIPHHOK envelope.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:610`

**Fix.** Bind [FromQuery] int accessibility and [FromQuery] int? price; persist price and an Accessibility column (add to InventionEntity); wrap the response in {Status,Invention,InventionVersion}.

##### `GET api/inventions/v1/unpublish?inventionId={id}` — SHAPE_MISMATCH (degraded)

Works functionally; response is bare ToWire instead of the BDNCJIPHHOK envelope.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:630`

**Fix.** Wrap response in {Status=0, Invention=ToWire(inv), InventionVersion=null}.

##### `POST api/inventions/v1/updateprice` — VERB_MISMATCH (degraded)

Server registers GET-with-query only; the 2023 client POSTs JSON body {"InventionId":Int64,"Price":Int32} → ASP.NET answers 405 Method Not Allowed, so changing the price of a published paid invention always fails. Response would also need the BDNCJIPHHOK envelope.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:578`

**Fix.** Add a [HttpPost("api/inventions/v1/updateprice")] overload binding {InventionId, Price} from JSON (keep the GET for older branches) and return {Status=0, Invention=ToWire(inv), InventionVersion=null}.

##### `GET api/inventions/v2/search?value&skip&take` — SHAPE_MISMATCH (degraded)

Route+verb+value param OK; skip/take are not bound (results hard-capped at 50, skip ignored) so store-search pagination repeats the same page. Items are IFJONDCAKKM and inherit the ToWire missing-keys defect (Accessibility, IsCertifiedInvention — see the featured/toptoday finding for details).

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:84`

**Fix.** Bind [FromQuery] int skip = 0, int take = 100 and apply Skip/Take; fix ToWire keys.

##### `POST api/inventions/v1/cheer` — SHAPE_MISMATCH (degraded)

Client body is {"InventionId":Int64,"Cheer":Boolean}. Server's CheerRequest has only InventionId — Cheer is ignored, so uncheering (Cheer=false) still leaves the cheer in place. Response is {Id, CheerCount}, not the expected BDNCJIPHHOK envelope (Status/Invention).

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:687`

**Fix.** Add bool Cheer to the record; on false remove the CheerEntity and decrement CheerCount; return {Status=0, Invention=ToWire(inv), InventionVersion=null}.

##### `GET (POST when >=100 ids) api/inventions/v1/fromcreators` — SHAPE_MISMATCH (degraded)

GET exists but the id parser SelectMany's over EVERY query field, so the client's "skip" and "take" values are ingested as creator ids (e.g. take=100 adds player 100's inventions to the shelf), and skip/take paging is not honored. No POST registration for the >=100 case (405). Items inherit the ToWire missing-keys defect.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:169`

**Fix.** Parse only the "id" query key; bind skip/take and apply them; add a POST accepting form fields.

##### `GET api/inventions/v1/featured` — SHAPE_MISMATCH (degraded)

Route exists (shared 'Popular' handler). Systemic response defect for ALL IFJONDCAKKM-list endpoints: ToWire omits two of the 22 keys the 2023 deserializer reads — Accessibility (Int32, 0=Private/1=Public/2=Unlisted; server sends IsPublished bool under a different key instead) and IsCertifiedInvention (Boolean). Missing Accessibility defaults to 0=Private on the client (or the strict reader throws — UNKNOWN), so every published store invention can present as Private in the 2023 UI.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:43`

**Fix.** Add Accessibility = i.IsPublished ? 1 : 0 (or a real column) and IsCertifiedInvention = false to ToWire. One change fixes featured/featureddormskins/toptoday/mine/search/batch/fromcreators/room/v1-single and every envelope's Invention payload.

##### `GET api/inventions/v1/toptoday` — SHAPE_MISMATCH (degraded)

Exists but aliases the same 'Popular' query as featured (identical content on both shelves — not actually 'today') and inherits the ToWire missing Accessibility/IsCertifiedInvention keys.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:45`

**Fix.** Fix ToWire; optionally scope toptoday to a 24h cheer window for shelf variety.

##### `GET api/inventions/v1/featureddormskins` — SHAPE_MISMATCH (degraded)

Exists but returns generic popular inventions, not dorm skins (no dorm-skin flag exists), and inherits the ToWire key gaps. Combined with the broken dormskinsfromids shape, the dorm-skin store surface is effectively non-functional.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:44`

**Fix.** Add IsDormSkin to InventionEntity, filter here, fix ToWire keys.

##### `GET (POST when >=100 ids) api/inventions/v2/batch` — SHAPE_MISMATCH (degraded)

GET works (parses "id" from query). POST alias binds [FromBody] JSON InventionBatchRequest but the client posts form field "id" → 415 once an id list reaches 100 (large rooms / big owned lists). Items inherit ToWire key gaps.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:142`

**Fix.** Accept form "id" in the POST action; fix ToWire.

##### `GET api/inventions/v2/mine` — SHAPE_MISMATCH (degraded)

Route+verb OK, returns the caller's non-deleted inventions. Only defect is the shared ToWire missing Accessibility/IsCertifiedInvention keys.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:79`

**Fix.** Fix ToWire (shared).

##### `POST api/inventions/v6/save` — SHAPE_MISMATCH (degraded)

Route+verb OK; the camelCase body binds (ASP.NET JSON binding is case-insensitive) including inventionDataFilename, and the response IS correctly wrapped {Status, Invention, InventionVersion}. Remaining gaps: chipsCost/cloudVariablesCost from the request are dropped (no properties, no columns) and the InventionVersion payload lacks ChipsCost/CloudVariablesCost/BlobHash; the Invention payload lacks Accessibility/IsCertifiedInvention (shared ToWire).

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:441`

**Fix.** Add ChipsCost/CloudVariablesCost to SaveInventionV4Request + version entity and emit them; fix ToWire/ToVersionWire.

##### `GET api/inventions/v1?inventionId={id}` — SHAPE_MISMATCH (degraded)

Route+verb+param OK, single object returned; inherits only the shared ToWire missing Accessibility/IsCertifiedInvention keys.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:327`

**Fix.** Fix ToWire (shared).

##### `GET api/inventions/v1/room?id={roomId}` — SHAPE_MISMATCH (degraded)

Route+verb+"id" query OK, array returned; inherits the shared ToWire key gaps. Room-load invention resolution otherwise real.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:320`

**Fix.** Fix ToWire (shared).

##### `GET api/inventions/v1/personaldetails/{id}` — SHAPE_MISMATCH (degraded)

Route+verb OK but the response is {Invention, CanEdit}; the client deserializes CEAFHBOOBKL {"IsCheering":Boolean}. The key is absent → IsCheering reads default false (or throws — UNKNOWN), so the invention detail page never shows the player's own cheer state.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:388`

**Fix.** Return Ok(new { IsCheering = await db.Cheers.AnyAsync(c => c.FromPlayerId == pid && c.TargetInventionId == id), Invention = ToWire(i), CanEdit = ... }) — extra keys harmless.

##### `GET showcase/{accountId}` — FIXED

Was missing everywhere (404), so the profile 'Showcase Rooms' carousel (`RoomListModel.JMGLGEKHHAK.PlayerShowcaseRooms = 7`, fed by `AccountModelController.RoomsShowcaseLinkImpl`) never loaded for any player.

Now served by `RoomsController.RoomsShowcase` on both `showcase/{accountId:long}` and `roomserver/showcase/{accountId:long}` (the issuing client NLDBPDCNNCF carries the `roomserver/` prefix in this deployment).

Verb GET is the ordinal 0 moved into rdx before the dispatch call at `NLDBPDCNNCF.txt:4494`, with the literal at ISIL 021 `:4484`; no cmov, so GET only. The response is a **bare JSON array of Int64 room ids** — the issuing method returns `Task<List<System.Int64>>` (`NLDBPDCNNCF.txt:4422`). Room objects are a client-side projection: `IBEOONPEELF.EJPNHLIJNPM` (`IBEOONPEELF.txt:8952`) resolves the ids through the room cache into `FGLDKEJLAKB<IReadOnlyList<NEMINAEBALC>>`, and the cache entry `<GetRoomsShowcase>b__0` is typed `Task<IReadOnlyList<Int64>>` (`IBEOONPEELF_NestedType_HHDHJPIJBJB.txt:14`).

Backing data: `showcase/{0}` is the only showcase literal in the binary — the client reads the list but never writes it — so curation is stored in the existing player-settings table under the key `rooms:showcase` (CSV of room ids), writable via `PUT /settings/v1/{accountId}` with no new schema. Curated ids are re-validated against Rooms on each read (archived/privatised rooms drop out) and returned in the curated order; a player who never curated one falls back to the rooms they created, ordered by HotScore/VisitCount/UpdatedAt. Public rooms only, except the owner viewing their own profile.

Handler: `DorkNet.Server\Controllers\API\Rooms\V2\RoomsController.cs` (`RoomsShowcase`)

##### `GET unity_assets/{0}/{1}/{2}` — SHAPE_MISMATCH (degraded)

A route exists (no host gate, so it answers on every host) but it streams the raw .assetbundle bytes as application/octet-stream. The traced rooms-service method NLDBPDCNNCF.FKLDMPMFLBD deserializes a JSON PPBIFMDLDCB object {"UnityAssetId":String,"Target":Byte,"Version":Int32,"Filename":String,"Hash":String} — feeding it binary bundle bytes fails the JSON parse and the Studio-baked scene resolve. Note the server file's own comment asserts the client downloads bundle BYTES from this URL; the ISIL trace shows at least this metadata call site exists (serializer LKLKHEEHEBC), so whether a second bytes-fetch call site also hits this exact path is UNKNOWN — the fix must keep bytes available for the CDN host while serving JSON to the API-host metadata call.

Handler: `DorkNet.Server\Controllers\Cdn\CdnController.cs:169`

**Fix.** Split by host: on the API/rooms host (or a new [HttpGet("unity_assets/{assetId}/{target:int}/{version:int}")] in the API area matched first), return JSON { UnityAssetId = assetId, Target = (byte)target, Version = version, Filename = pick.Filename, Hash = <stored/computed blob hash> } using the same scene/bundle resolution already in ServeUnityAsset; keep the CDN-host byte streaming for the bare-filename .assetbundle catch-all (CdnController.cs:121) which the client can then fetch by Filename.

##### `GET api/images/v2/named` — STUB (cosmetic)

Hardcoded Ok(Array.Empty<object>()). An empty array is a shape-valid response (client caches an empty list), but named/time-windowed promo images can never be served. Per the no-stubs rule this is a defect, though nothing crashes.

Handler: `DorkNet.Server\Controllers\API\Images\ImagesController.cs:330`

**Fix.** Back it with a small NamedImageEntity table (FriendlyImageName, ImageName, StartTime, EndTime) + admin CRUD, and return the rows with those exact PascalCase keys.

##### `GET api/inventions/v1/tagfilters` — SHAPE_MISMATCH (cosmetic)

Returns {PinnedFilters, PopularFilters} but the 2023 DTO AKCLLEJNFFD also reads TrendingFilters:[String]; the missing list yields null/empty for callers projecting that list. The tag list is also hardcoded (functional stub).

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:205`

**Fix.** Add TrendingFilters = tags to the anonymous object; ideally derive lists from actual TagsCsv frequency instead of the hardcoded array.

##### `POST api/inventions/v1/report` — SHAPE_MISMATCH (cosmetic)

Request binds cleanly (InventionId/ReportCategory/Details match, case-insensitive). Response is {Reported:true} but the client deserializes PHMHCPEMABG {"Success":Boolean,"Message":String} — Success reads default false, so the report UI can show failure even though the report row persisted.

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs:720`

**Fix.** Return Ok(new { Success = true, Message = string.Empty }).

##### `POST remote-run/push-to-studio` — IMPLEMENTED

Handler: `DorkNet.Server\Controllers\API\Inventions\InventionsController.cs` → `PushToStudio`, routed at both `remote-run/push-to-studio` and `roomserver/remote-run/push-to-studio`.

Verb POST from EHIOLHBGODG.txt:411-417 (route literal into r9, `Move rdx, 2` = HTTPMethods.Post, no cmov). Body is a RAW JSON document — :433 passes the serialized DTO to `BNDIAONDFFF.FJLLPHFOOJJ` (RawJsonForm, `application/json`) — bound through `FormOrJsonModelBinder`. Request FNAGBPCAGJD keys are literal in the generated serializer OFMNNCMPEPA.txt:535-698: `SessionId, RoomId, SubRoomId, UnityAssetId, RoomData, SubRoomData, SavedByAccountId`; each blob ref is `{Filename, Hash, OwnershipProof}` (FLELPHJDLNG.txt:255/274/290). Response is a single CEELGOLBHIL — eight keys, literal in its reader PPHLKNGCGOE.txt:599-786: `SessionId, RoomId, SubRoomId, UnityAssetId, RoomDataFilename, RoomDataHash, SubRoomDataFilename, SubRoomDataHash`; RoomId/SubRoomId are `Nullable<Int64>` (CEELGOLBHIL.txt:25/51) and the two blob refs are FLATTENED (OwnershipProof is not echoed). A non-2xx or malformed reply shows the client's "Failed to push to Rec Room Studio" toast (EHIOLHBGODG.txt:445).

This is the Studio remote-run handoff, a sibling of the normal save commit, not a replacement for it (client-side: OGPDOMCNIFM.txt:271 `rooms/{id}/subrooms/{id}/data` vs :449 push) — so the server does NOT touch `RoomEntity.CurrentDataBlobName`. It authorizes the caller like a room save (creator / admin / accepted CoOwner), requires both pushed filenames to resolve to real `RoomDataBlobEntity` rows (the client uploads them immediately beforehand), stamps the sub-room save blob with its `RoomId`/`SubRoomId` so it shows up in that sub-room's `datahistory`, records the pushed `UnityAssetId` on the `RoomSceneEntity`, writes `SessionId` into `RoomEntity.StudioSessionId`, and keeps the full push under `remoterun:{SessionId}` in `PlayerSettingEntity`. `IsRoomLinkedToRecRoomStudio` is intentionally left alone (it changes in-room MakerPen UI and the client has no unlink call).

**Still out of scope:** live relay of the details to a second logged-in session. The client's receive side (`EHIOLHBGODG.MCPBJLHFOBC(CEELGOLBHIL)` → the `Action<CEELGOLBHIL>` subscribed at MBOJJFBIAGE.txt:366/978) has no call site anywhere in the IsilDump, so the notification id carrying it cannot be established from the binary; `PushNotificationId` gets no invented member for it.

### Storefront, purchases and subscriptions

`commerce-subscription`

Verified all 39 real HTTP routes against the DorkNet march-2023 branch. 4 hard breaks: POST/DELETE subscription/{accountId} (creator-club subscribe/unsubscribe) is entirely missing; PurchaseRoomKeyWithCurrency is GET-only while the client POSTs (405) and its response shape is wrong; buyInvention returns a balance wrapper without the InventionResponse envelope the client requires; trialInvention binds inventionId from query while the client sends it as a form field (always 404) and returns a bare invention instead of the {Status,Invention,InventionVersion} wrapper. 8 shape mismatches that degrade UI: season/{id} (nearly every key differs from GEEMFIMOPBH), v2/balance and buyProgressionEventXpBoost (Data must be a single object, server sends an array), buyPurchaseReminder ({Success} instead of JNPGKDJJPPM), initiatepurchase (TransactionId string vs Int64, and skuId form field never read), CampusCard UpdateAndGetSubscription (inner subscription missing Level/Period/ExpirationDate/etc.), subscription/details and top/creators/today (missing ClubId), trialInvention/duration (object vs bare int), apple musicpromotion active/code (object vs bare bool; missing Result/Url keys — iOS-only). Cross-cutting: every BalanceUpdateResponse container emits "BalanceType" where the 2023 client's wire key for that property is "Platform". Several buy endpoints are behavioral stubs (buyTier/buyElite/buyForFreeGiftButton ignore the request and grant nothing/wrong things).

**Client-side notes.** HTTP plumbing (applies to every endpoint): requests are built as BNDIAONDFFF objects via .ctor(BestHTTP.HTTPMethods verb, GJDLNNLKDIJ host, String route) — BNDIAONDFFF.txt:74. Verb enum is BestHTTP.HTTPMethods: 0=GET, 2=POST, 4=DELETE (observed values). Host enum values seen: 2 (purchase/campaign), 26 (api/storefronts, CampusCard), 13 (subscription/club), 1 (AppIntegrity/apple/playstationplus) — all should resolve to the same DorkNet host. BNDIAONDFFF.AFGEDDANEKP(name, value) appends KeyValuePair<String,String> params (BNDIAONDFFF.txt:450-646): sent as query string on GET/DELETE and form-urlencoded body on POST; BNDIAONDFFF.FJLLPHFOOJJ(string) sets a raw JSON body. Response DTOs are read by generated formatters whose key tables accept PascalCase, camelCase, AND all-lowercase variants and whose serializers write PascalCase (e.g. GBPHFPMCGHH.txt:203-238) — so PascalCase responses are always safe. Enums are numeric on the wire (JECENNBIMEI<T> converters, OLIHBGPDPHF.txt:780-822). Quirk: BalanceResponseDTO's third property (JLBBOFOPOGL BalanceType) uses wire key "Platform" (PECGEJAAMHB.txt) in all BalanceUpdateResponse containers, while JNPGKDJJPPM (buyPurchaseReminder) uses "BalanceType" — do not unify them. SERVER GAPS FOUND (march-2023 branch): (1) no POST/DELETE subscription/{accountId} — creator-club subscribe/unsubscribe 404s (client IKMMOCKDKAF.txt:6560/6855; server only has GET subscription/details|subscriberCount|mine|top per server-routes.json); (2) api/storefronts/v1/PurchaseRoomKeyWithCurrency is GET-only [FromQuery] on the server (StorefrontsBuyController.cs:217) but the 2023 client POSTs form fields RoomKeyId/RequestedPrice/RequestedPurchaseCurrencyId (DCFKEFHJAGC.txt:7452-7497); (3) purchase/v1/initiatepurchase server returns TransactionId as string "txn-…" (PurchaseController.cs:58) but the client deserializes {TransactionId: Int64} (HPGPBGLFHMA.txt + EAIGBBHMIKM_NestedType_GLBNBBCPFKF.txt Int64 getter) — must be a JSON number; (4) api/AppIntegrity/v1/iosproducts is GET on the server but POST in the client (iOS-only, harmless for PC preservation). Cache-key literal reuse: the route strings purchasecampaign/allcurrent/v2, purchase/v1/hasspentmoney, reminder/currentTokenBundles/v2, api/catalog/v1/all?onlyAvailableSkus=true also appear as cache keys in EAIGBBHMIKM.txt:41,2733-2777,3479 — those occurrences are not extra HTTP calls. Related route living in the same client class but grouped elsewhere: GET api/catalog/v1/all?onlyAvailableSkus=true -> List<EAIGBBHMIKM/GGKCJMIFLDJ> (SkuId:Int32, Name, Description, ImageName, Price:Int32, OculusSkuId, AppleProductId, PsnProductLabel, XboxProductId, XboxStoreId, GooglePlaySkuId, … keys in BGFBJCIOIFP.txt:999-1234) feeding the IAP flow; also api/roomCurrencies/v2/purchase and api/roomconsumables/v1/roomconsumable/{guid}/purchase/{tokens|currency} are issued from DCFKEFHJAGC.txt:7794/8252/8726 but belong to the room-currency/consumables subsystems.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/AppIntegrity/v1/iospaymentqueuefailed` | raw JSON body: the failure-description string (closure GAAPALMODOL/MEAMPIIMJDC serializes the single String arg, sent via FJLLPHFOOJJ) | {"Success": Boolean, "Message": String} (PHMHCPEMABG) |
| POST | `api/AppIntegrity/v1/iosproducts` | raw JSON body: array of {"Name": String, "Price": Single, "ProductId": String} (serialized product list, sent via BNDIAONDFFF.FJLLPHFOOJJ raw-JSON body helper) | {"Success": Boolean, "Message": String} (PHMHCPEMABG; deserializer accepts PascalCase/camelCase/lowercase key variants, serializer writes PascalCase — same for every DTO below) |
| GET | `api/CampusCard/PS5RecRoomPlusEnabledForAllPlayers` | none | bare JSON boolean (true/false) |
| POST | `api/CampusCard/v1/UpdateAndGetSubscription` | form/query pair 'accessToken': String (platform IAP token; AFGEDDANEKP adds KeyValuePair<String,String>, form-encoded on POST) | {"Subscription": {"SubscriptionId": Int64, "RecNetPlayerId": Int32, "PlatformType": Int32? (enum HHJIBNMLOAC), "PlatformId": String, "PlatformPurchaseId": String, "Level": Int32 (enum LOMOPBPNFCE), "Period": Int32 (enum  |
| GET | `api/apple/musicpromotion/active` | none | bare JSON boolean |
| GET | `api/apple/musicpromotion/code` | none | {"Result": Int32 (enum AppleMusicPromotionResponseDTO/CABJNOJGGJF), "Code": String, "Url": String, "RedemptionUrl": String} |
| POST | `api/playstationplus/expire` | none (no form fields; fire-and-forget via BNDIAONDFFF.KDOPJCNKOOK) | ignored — any 2xx works; body not parsed (only Func<String,String> error mapper) |
| POST | `api/storefronts/v1/PurchaseRoomKeyWithCurrency` | form fields: 'RoomKeyId': Int64, 'RequestedPrice': Int64, 'RequestedPurchaseCurrencyId': Guid | {"Balance": {"AccountId": ?, "CurrencyId": Guid, "Balance": Int64, "ModifiedAt": DateTime} (LGAMPMJNGFH room-currency balance), "RoomKeyResponse": {"Status": Int32, "RoomKey": {...}} (ONLIDBLNMCC)} |
| GET | `api/storefronts/v1/adcarouselitems` | none | [{"AdCarouselItemId": Int32, "ImageName": String, "Title": String, "Description": String, "PurchasableItemIds": [Int32], "PurchaseReminderId": Int32?}] |
| GET | `api/storefronts/v1/balanceAddType/{currencyType:int}/{tierId:int}` | none (path params Int32, Int32) | {"CurrencyType": Int32 (enum EAFDEJBEFJB), "BalanceAddType": Int32 (enum HPCEGKLGPHC), "BaseAward": Int32, "BonusAwardMin": Int32, "BonusAwardMax": Int32, "RateLimitType": Int32, "IgnorePartialMultiplier": Boolean, "MaxP |
| POST | `api/storefronts/v1/buyForFreeGiftButton` | raw JSON body (RequestPurchaseForFreeGiftButtonDTO): {"PurchasableItemId": Int32, "CouponConsumablePlayerMappingId": Int64?, "Count": Int32, "CreatorPlayerId": Int32, "RequestedPri | {"Balance": Int64, "CurrencyType": Int32 (enum), "Platform": Int32 (enum; wire key for BalanceType)} (BalanceResponseDTO) |
| POST | `api/storefronts/v1/buyProgressionEventXpBoost` | form fields: 'progressionEventId': Int64, 'purchasableXpBoostId': Guid, 'requestedPrice': Int32, 'expectedXp': Int32 | {"BalanceUpdates": [{"UpdateResponse": Int32, "Data": {"Xp": Int32}}], "Balance": Int64, "CurrencyType": Int32, "Platform": Int32} |
| POST | `api/storefronts/v1/buyPurchaseReminder` | form fields: 'purchaseReminderId': Int32, 'requestedPrice': Int64 | {"Balance": Int64, "CurrencyType": Int32 (enum EAFDEJBEFJB), "BalanceType": Int32 (enum JLBBOFOPOGL — note key is BalanceType here, not Platform), "Data": [FHMABOHAEED...]} (JNPGKDJJPPM) |
| GET | `api/storefronts/v1/buyRoomKey` | query params: 'RoomKeyId': Int64, 'RequestedPrice': Int64 (note PascalCase keys here, unlike buyInvention's camelCase — ASP.NET binding is case-insensitive but exact casing is as c | {"RoomKeyResponse": {"Status": Int32 (enum GDIOEPIOPEE), "RoomKey": {room-key DTO}} (ONLIDBLNMCC), "BalanceUpdateResponse": {"BalanceUpdates": [{"UpdateResponse": Int32, "Data": {PurchaseBalanceModificationDTO}}], "Balan |
| POST | `api/storefronts/v1/objectives` | raw JSON body: array of per-objective Dictionary<String,Object>: [{"objectiveType": Int32, "completionPercentage": Single, "roomId": Int64 (omitted when null)}] | ignored — any 2xx works; body not parsed |
| GET | `api/storefronts/v1/season/{storefrontType:int}` | none (path param Int32) | {"Season": Int32, "Name": String, "StartAt": DateTime, "EndAt": DateTime, "CurrencyType": Int32 (enum EAFDEJBEFJB), "EliteUpgrade": {"PurchasableItemId": Int32, "Type": Int32, "Prices": ?, "SubscriberPrices": ?, "IsFeatu |
| GET | `api/storefronts/v1/toptoday` | none | bare JSON array of Int32 purchasableItemIds, e.g. [101,102] |
| POST | `api/storefronts/v1/trialInvention` | form field: 'inventionId': Int64 | {"Status": Int32 (enum CFHBHALNFKC), "Invention": {IFJONDCAKKM — see buyInvention for the full 22-key shape}, "InventionVersion": {PLIKEBBPJGI — see buyInvention}} (BDNCJIPHHOK; client then maps to the Invention via Func |
| GET | `api/storefronts/v1/trialInvention/duration` | none | bare JSON integer (Int32 trial duration) |
| POST | `api/storefronts/v2/balance` | raw JSON body: UnityEngine.JsonUtility.ToJson(GrantBalanceRewardDTO) — field names are Unity-serialized public fields NOT visible in ISIL; the server's previously-validated shape i | {"BalanceUpdates": [{"UpdateResponse": Int32 (enum DCFKEFHJAGC/CABBDKFODEC purchase-result code), "Data": {"BalanceAddType": Int32, "BaseAward": Int32, "BonusAward": Int32, "RateLimit": Int32, "CurrentCount": Int32, "Tot |
| POST | `api/storefronts/v2/buyElite` | raw JSON body RequestPurchaseItemDTO (same as buyItem) | same as buyItem (PurchaseBalanceUpdateResponseDTO`1<FHMABOHAEED>) |
| GET | `api/storefronts/v2/buyInvention` | query params: 'inventionId': Int64, 'requestedPrice': Int32 (AFGEDDANEKP pairs on a GET request go to the query string) | {"InventionResponse": {"Status": Int32 (enum CFHBHALNFKC), "Invention": {"InventionId": Int64, "ReplicationId": Guid, "CreatorPlayerId": Int32, "Name": String, "Description": String, "ImageName": String, "CurrentVersionN |
| POST | `api/storefronts/v2/buyItem` | raw JSON body (RequestPurchaseItemDTO): {"StorefrontType": Int32 (enum BJGDDFLENAO), "PurchasableItemId": Int32, "CurrencyType": Int32 (enum EAFDEJBEFJB), "RequestedPrice": Int64?, | {"BalanceUpdates": [{"UpdateResponse": Int32 (enum CABBDKFODEC: maps to client toasts 'Not enough X', 'You already own this', 'The price ... has changed', 'Too many requests', 'Coupon could not be applied'), "Data": [FHM |
| POST | `api/storefronts/v2/buyTier` | raw JSON body RequestPurchaseItemDTO (same shape as buyItem, Gift null) | same as buyItem: {"BalanceUpdates": [{"UpdateResponse": Int32, "Data": [FHMABOHAEED...]}], "Balance": Int64, "CurrencyType": Int32, "Platform": Int32} |
| GET | `api/storefronts/v3/giftdropstore/{storefrontType:int}` | none (path param Int32 = storefront type) | {"SubscriberDiscountPercent": ?, "StorefrontType": Int32, "NextUpdate": DateTime, "NewUntil": DateTime, "StoreItems": [...]} (HOEGLKNEIOF; StoreItems entries carry the purchasable-item keys PurchasableItemId/Type/Prices/ |
| GET | `api/storefronts/v4/balance/{currencyType:int}` | none (path param Int32 = (int)EAFDEJBEFJB) | JSON array: [{"Balance": Int64, "CurrencyType": Int32 (enum EAFDEJBEFJB), "Platform": Int32 (enum JLBBOFOPOGL; NOTE the wire key for the BalanceType property is 'Platform')}] — client reduces List<BalanceResponseDTO> to  |
| POST | `purchase/v1/cancelpurchase` | form fields: 'transactionId': Int64, 'accessToken': String | ignored — any 2xx; body not parsed |
| POST | `purchase/v1/cleanuppending` | form field: 'accessToken': String | ignored — any 2xx; body not parsed |
| POST | `purchase/v1/completepurchase` | form fields: 'transactionId': Int64, 'accessToken': String | ignored — any 2xx; body not parsed |
| GET | `purchase/v1/hasspentmoney` | none | bare JSON boolean |
| POST | `purchase/v1/initiatepurchase` | form fields: 'skuId': Int32, 'accessToken': String, 'purchaseReminderId': Int32? | {"TransactionId": Int64} (GLBNBBCPFKF; client maps to Int64 via Func<GLBNBBCPFKF,Int64>) |
| POST | `purchase/v1/processpurchase` | form fields: 'purchaseDetails': String, 'accessToken': String, 'purchaseReminderId': Int32? | ignored — any 2xx; body not parsed |
| GET | `purchasecampaign/allcurrent/v2` | none | [{"PurchaseCampaignId": Int32, "PurchaseReminder": {GCNMNDLBPLO — same shape as reminder/currentTokenBundles/v2 entry} \| null, "Name": String, "TriggerFlags": Int32 (enum PDKALPDKNJC), "LastShown": DateTime, "ShowCount" |
| POST | `purchasecampaign/shown` | form field: 'purchaseCampaignId': Int32 | ignored — any 2xx; body not parsed |
| GET | `reminder/currentTokenBundles/v2` | none | [{"PurchaseReminderId": Int32, "SkuId": Int32?, "TokenPrice": Int32?, "Title": String, "Description": String, "ImageName": String, "EndDate": DateTime?, "BonusGiftDrops": [EFFIEFEFHHB: {"GiftDropId": ?, "FriendlyName": S |
| GET | `subscription/details/{accountId:int}` | none (path param Int32) | {"AccountId": Int32, "ClubId": Int64, "SubscriberCount": Int32} (single HHOCDLAFOKB object, not a list) |
| GET | `subscription/mine/member` | query params (sorted variant only): 'sort': Int32 (enum MJLECOMCJCN), 'skip': Int32, 'take': Int32; cache-refresh variant sends none | [{"AccountId": Int32, "ClubId": Int64, "SubscriberCount": Int32}] (List<HHOCDLAFOKB>) |
| GET | `subscription/subscriberCount/{accountId:int}` | none (path param Int32) | bare JSON integer (Int32) |
| GET | `subscription/top/creators/today` | none | [{"AccountId": Int32, "ClubId": Int64, "SubscriberCount": Int32}] |
| POST | `subscription/v1/cancel` | form field: 'accessToken': String | ignored — any 2xx; body not parsed |
| POST | `subscription/{accountId:int}` | POST: form field 'roomId': Int64 (current room id via ObscuredLong, omitted when -1). DELETE: none. | ignored — any 2xx; body not parsed (client then refreshes subscription cache and fires 'CreatorClubSubscriptionUpdate' handling) |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/storefronts/` — Not a route: this literal is a URL PREFIX combined via String.Format into 4 real routes (listed separately): '{0}v1/season/{1}', '{0}v4/balance/{1}', '{0}v1/balanceAddType/{1}/{2}', '{0}v3/giftdropstore/{1}'.
- `store/invention/{0}` — Same as store/item/{0}: rec.net share deeplink for an invention (formatted with Int64 InventionId), passed to HIHEDOIIHGG/LinkManager; not an API request.
- `store/item/{0}` — NOT an HTTP API route: rec.net web deeplink path formatted and handed to the sharing/link service HIHEDOIIHGG (implemented by RecRoom.Sharing.LinkManager) for share links/QR codes — no BNDIAONDFFF request is built.

#### Defects

##### `GET api/storefronts/v2/buyInvention` — SHAPE_MISMATCH (breaks-gameplay)

GET is registered and the query param inventionId binds, but the response is the generic balance wrapper {Balance,CurrencyType,BalanceType,BalanceUpdates:[{UpdateResponse,Data:[]}]}. The client deserializes InventionPurchaseResponseDTO: {InventionResponse:{Status (enum CFHBHALNFKC), Invention (IFJONDCAKKM, 22 keys), InventionVersion (PLIKEBBPJGI incl. BlobName/BlobHash)}, BalanceUpdateResponse:{...}}. InventionResponse is entirely absent, so Status/Invention/InventionVersion are null/default — the client cannot confirm the purchase or spawn the invention; MakerPen store buying is broken. Also 'requestedPrice' is ignored, and the ownership record goes to an ObjectiveProgress key rather than anything the inventions API reads back — UNKNOWN whether ownership is visible elsewhere.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:255`

**Fix.** Wrap the response: { InventionResponse = { Status = <success enum>, Invention = InventionsController.ToWire(inv), InventionVersion = <latest version wire incl. BlobName/BlobHash> }, BalanceUpdateResponse = { BalanceUpdates:[{UpdateResponse:0, Data:{BalanceAddType,Delta,Balance,BalanceType,CurrencyType} (single object, PurchaseBalanceModificationDTO)}], Balance, CurrencyType, Platform } } — reuse the invention wire builders from InventionsController, and record ownership where the inventions 'mine/saved' queries can see it.

##### `POST api/storefronts/v1/PurchaseRoomKeyWithCurrency` — VERB_MISMATCH (breaks-gameplay)

Server registers the route only as a [HttpGet] alias of BuyRoomKey with [FromQuery] roomKeyId/requestedPrice. The 2023 client POSTs form fields RoomKeyId/RequestedPrice/RequestedPurchaseCurrencyId → 405 Method Not Allowed; room keys priced in a room-specific currency cannot be bought at all. Even with POST accepted, the handler ignores RequestedPurchaseCurrencyId (charges tokens) and returns {RoomKeyResponse,BalanceUpdateResponse}, whereas the client's RoomKeyPurchaseWithCurrencyResponseDTO expects {Balance:{AccountId,CurrencyId (Guid),Balance (Int64),ModifiedAt (DateTime)} (room-currency balance LGAMPMJNGFH), RoomKeyResponse:{Status,RoomKey}}.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:217`

**Fix.** Add a dedicated [HttpPost("api/storefronts/v1/PurchaseRoomKeyWithCurrency")] handler reading form fields RoomKeyId (long), RequestedPrice (long), RequestedPurchaseCurrencyId (Guid); debit the player's room-currency balance for that currency GUID (room-currencies tables), grant the key, and return { Balance = {AccountId, CurrencyId, Balance, ModifiedAt}, RoomKeyResponse = RoomKeysController.RoomKeyResponse(status, key) }.

##### `POST api/storefronts/v1/trialInvention` — SHAPE_MISMATCH (breaks-gameplay)

POST exists but (1) binds inventionId with [FromQuery] while the 2023 client sends it as a form-urlencoded field (AFGEDDANEKP pairs go to the body on POST) → inventionId=0 → 404 NotFound on every trial attempt; and (2) even if it bound, the response is the bare invention wire object while the client deserializes BDNCJIPHHOK {Status (enum CFHBHALNFKC), Invention (IFJONDCAKKM), InventionVersion (PLIKEBBPJGI)} and then projects .Invention — a bare invention leaves Invention null. Invention free trials never work.

Handler: `DorkNet.Server/Controllers/API/Inventions/InventionsController.cs:345`

**Fix.** Accept the form field (e.g. [FromForm] long inventionId with [FromQuery] fallback) and return { Status = <success enum>, Invention = ToWire(i), InventionVersion = <latest version wire> }.

##### `POST subscription/{accountId:int}` — FIXED (was MISSING, breaks-gameplay)

Implemented in `DorkNet.Server/Controllers/API/Subscriptions/SubscriptionsController.cs`: `[HttpPost("/subscription/{accountId:long}")]` binds the optional `roomId` form field through `FormOrJsonModelBinder` and `[HttpDelete("/subscription/{accountId:long}")]` takes no body. Both write/remove the canonical player→player `Subscriptions` row AND mirror into `ClubSubscriptions` for the target's creator club (oldest club they own, the same resolution `subscription/details` reports as `ClubId`) so the client's post-write refresh of `subscription/mine/member` actually shows the subscription. Both are idempotent and return a bare 200 (the client parses no body). `roomId` is attribution only — no column exists for it, so it is logged rather than persisted.

Original finding: No handler accepts POST or DELETE on subscription/{accountId} anywhere in the server (verified by grepping all HttpPost/HttpDelete attributes under subscription/: only /subscription/v1/* POSTs exist, whose literal 'v1' segment cannot match a numeric account id; the follow-graph mutations live at api/playersubscriptions/v1/subscribe/{targetId} which the 2023 client never calls). The client's subscribe (POST, form field roomId:Int64, omitted when -1) and unsubscribe (DELETE, no body) both 404 → 'Failed to subscribe to {0}' / 'Failed to unsubscribe from {0}' toasts; creator-club subscribing is entirely broken from the 2023 client.

**Fix.** Add to PlayerSubscriptionsController (or ClubsController): [HttpPost("/subscription/{accountId:long}")] reading optional form field roomId and creating the subscription row (same semantics as Subscribe), and [HttpDelete("/subscription/{accountId:long}")] removing it. Return 200 with empty/any body (client ignores it, then refreshes via subscription/mine/member). Keep the row source consistent with what subscription/mine/member reads (ClubSubscriptions vs Subscriptions — currently mine/member reads ClubSubscriptions while the playersubscriptions endpoints write Subscriptions; the new handlers must write the table mine/member reads, or the subscribe will never show up).

##### `POST api/CampusCard/v1/UpdateAndGetSubscription` — SHAPE_MISMATCH (degraded)

POST exists, outer keys Subscription/PlatformAccountSubscribedPlayerId match, but the inner Subscription object (client IENPGNCIHOK) is missing PlatformType, PlatformId, PlatformPurchaseId, Level (Int32 enum LOMOPBPNFCE), Period (Int32 enum BHEIDLHDPKF), ExpirationDate (DateTime), IsAutoRenewing (Boolean), CreatedAt, ModifiedAt. Server instead emits isActive/startedAt/currentPeriodEnd/renewalDate, none of which exist in the client DTO. Missing Level defaults to 0 and ExpirationDate to default(DateTime), so the RR+ status refresh reads the subscription as no-tier/expired — RR+ never shows active — and the missing non-nullable DateTime fields risk a strict-reader throw. Also note the client sends only a form field 'accessToken'; the server's 'subscription'/'platformAccountSubscribedPlayerId' fields are never sent, so the handler always takes the self-target path (harmless).

Handler: `DorkNet.Server/Controllers/API/Compatibility/CompatibilityFeatureController.cs:44`

**Fix.** In CompatibilityFeatureController.UpdateAndGetCampusCardSubscription, replace the inner dictionary with the full IENPGNCIHOK shape: SubscriptionId (Int64), RecNetPlayerId (Int32), PlatformType (Int32? or null), PlatformId (String), PlatformPurchaseId (String), Level (Int32, non-zero = subscribed tier), Period (Int32), ExpirationDate (DateTime far-future), IsAutoRenewing (Boolean), CreatedAt, ModifiedAt. Keep PlatformAccountSubscribedPlayerId on the outer object.

##### `GET api/storefronts/v1/season/{storefrontType:int}` — SHAPE_MISMATCH (degraded)

Route exists but virtually every key differs from the client's GEEMFIMOPBH: server sends SeasonId/SeasonType/Active/StartsAt/EndsAt and Tiers entries {TierId,DisplayName,Description,Price,CurrencyType,ImageName}; client reads Season (Int32), Name, StartAt, EndAt (note no 's'), CurrencyType, EliteUpgrade (DIPHILPNLAN purchasable-item object), Tiers as [{Tier:Int32, Rewards:[purchasable items with PurchasableItemId/Type/Prices/SubscriberPrices/IsFeatured/AvailableAt/AvailableUntil/NewUntil]}], and PersonalDetails (BOMJCPNECFG {HasEliteUpgrade,CurrentSeasonTier,ModifiedAt}). Every client field lands on its default — the season/battle-pass page renders empty (no tiers, no elite upgrade, tier 0), and the non-nullable DateTime fields (StartAt/EndAt) risk strict-reader throws.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsController.cs:234`

**Fix.** Rewrite the v1/season handler to emit the GEEMFIMOPBH shape: {Season, Name, StartAt, EndAt, CurrencyType, EliteUpgrade:{PurchasableItemId,Type,Prices,SubscriberPrices,IsFeatured,AvailableAt,AvailableUntil,NewUntil}, Tiers:[{Tier, Rewards:[same purchasable shape, reuse StoreService.BuildPurchasableGiftDrop-style rows]}], PersonalDetails:{HasEliteUpgrade,CurrentSeasonTier,ModifiedAt}}. Note the path param is the storefront-type enum, not a season id.

##### `POST api/storefronts/v2/balance` — SHAPE_MISMATCH (degraded)

Handler exists and reads the request correctly (case-insensitive JSON {CurrencyType,BalanceAdds:[{Multiplier,BalanceAddType}]}), but the response nests ALL modifications as an ARRAY under one BalanceUpdates entry's Data. The client's BalanceUpdateResponseDTO`1<RewardBalanceModificationDTO> expects Data to be a SINGLE object per BalanceUpdates entry — an array where an object is expected makes the generated reader throw, so client-initiated reward grants (activity/event rewards) fail to parse even though the currency was actually granted server-side. Additionally the outer object emits "BalanceType" where the client's wire key for that property is "Platform" (PECGEJAAMHB quirk).

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsController.cs:341`

**Fix.** In StorefrontsController.BalanceUpdateResponse (or a v2/balance-specific variant): emit one BalanceUpdates entry per modification with Data as the bare modification object (RewardModification already has the right keys BalanceAddType/BaseAward/BonusAward/RateLimit/CurrentCount/Total/BalanceType/BalanceInGiftBox), and add "Platform": 0 to the outer object.

##### `POST api/storefronts/v2/buyTier` — STUB (degraded)

POST exists and the response parses (PurchaseBalanceUpdateResponseDTO family, empty Data list), but the handler deducts a fixed 1-token cost regardless of the tier's real price and records nothing — no season-tier progress is granted, so the season page's CurrentSeasonTier never advances after a 'buy tier skip'. Outer "Platform" key also missing (BalanceType emitted instead).

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:188`

**Fix.** Charge req.PurchasableItemId's actual price and persist tier progress (e.g. bump the value backing PersonalDetails.CurrentSeasonTier used by the v1/season response) so the season UI reflects the purchase; add Platform key.

##### `POST api/storefronts/v2/buyElite` — STUB (degraded)

POST exists, response parses, but the handler charges nothing and records nothing — HasEliteUpgrade never flips in the season PersonalDetails, so the elite upgrade purchase appears to succeed then reverts. Outer "Platform" key missing.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:203`

**Fix.** Deduct the elite price and persist a per-player elite flag that the (to-be-fixed) v1/season handler surfaces as PersonalDetails.HasEliteUpgrade = true.

##### `POST api/storefronts/v1/buyForFreeGiftButton` — STUB (degraded)

POST exists and the response parses well enough (Balance/CurrencyType present; "Platform" key missing, extra keys skipped), but the handler binds [FromQuery] currencyType/price while the client sends a raw JSON body {PurchasableItemId,CouponConsumablePlayerMappingId,Count,CreatorPlayerId,RequestedPrice}. The body is never read: price=0 (nothing charged) and, critically, the requested PurchasableItemId is never granted — instead a hardcoded 25-token 'FreeGiftButton' gift package is inserted. In-room free-gift-button gadgets hand out the wrong reward every time.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:283`

**Fix.** Read the JSON body into a RequestPurchaseForFreeGiftButtonDTO (PurchasableItemId, CouponConsumablePlayerMappingId?, Count, CreatorPlayerId, RequestedPrice?), resolve the store item by PurchasableItemId and grant THAT item (Count times), charge RequestedPrice when set, and return { Balance, CurrencyType, Platform } (BalanceResponseDTO trio with the Platform wire key).

##### `POST api/storefronts/v1/buyPurchaseReminder` — SHAPE_MISMATCH (degraded)

POST exists but is a stub: it ignores the client's form fields purchaseReminderId/requestedPrice, writes an ObjectiveProgress marker, and returns {Success:true}. Client deserializes JNPGKDJJPPM {Balance:Int64, CurrencyType:Int32, BalanceType:Int32 (key IS 'BalanceType' here, not 'Platform'), Data:[FHMABOHAEED gift packages]} — every field missing → defaults (balance shows 0) and nothing is charged or granted, so buying the reminder-advertised item silently no-ops. Low real-world impact since reminder/currentTokenBundles/v2 returns an empty list, so no reminder popups exist to buy from.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:329`

**Fix.** Read form fields purchaseReminderId (int) / requestedPrice (long); charge and grant the reminder's item, then return { Balance, CurrencyType, BalanceType, Data = [gift package objects in the FHMABOHAEED shape used by buyItem] }.

##### `POST api/storefronts/v1/buyProgressionEventXpBoost` — SHAPE_MISMATCH (degraded)

POST exists but binds [FromQuery] currencyType/price; the client sends form fields progressionEventId/purchasableXpBoostId/requestedPrice/expectedXp which never bind → nothing charged, no XP granted. Response reuses BalanceUpdateResponse whose BalanceUpdates[0].Data is an empty ARRAY, while the client's BalanceUpdateResponseDTO`1<PurchasedProgressionEventXpBoostDTO> expects Data as a single OBJECT {"Xp":Int32} — array-vs-object makes the reader throw. Outer "Platform" key also missing.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsBuyController.cs:308`

**Fix.** Read the four form fields, charge requestedPrice, grant expectedXp via LevelService.AwardXpAsync, and return { BalanceUpdates:[{UpdateResponse:0, Data:{ Xp = expectedXp }}], Balance, CurrencyType, Platform = 0 }.

##### `GET api/storefronts/v1/trialInvention/duration` — SHAPE_MISMATCH (degraded)

Server returns {Duration:300} (a shape chosen for the 2020.12 client per its own doc comment), but the 2023 client on this branch deserializes a BARE JSON integer (Task<Int32>, no projection) — an object where an int is expected fails the read, so the trial-length fetch errors.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsController.cs:95`

**Fix.** On the march-2023 branch return Content(duration.ToString(), "application/json") — a bare integer body.

##### `POST purchase/v1/initiatepurchase` — SHAPE_MISMATCH (degraded)

POST route exists but is contract-incompatible twice over: (1) the client sends form fields skuId/accessToken/purchaseReminderId; the server binds [FromForm] PurchaseRequest(ItemId,Slug,Quantity) so nothing matches, ResolveItemAsync returns null, and the reply is {success:false,error:"item_not_found"} with NO TransactionId key; (2) even the success branch returns TransactionId as the STRING "txn-…" while the client's GLBNBBCPFKF.TransactionId is Int64 — the strict reader needs a JSON number. Every real-money token-bundle/RR+ checkout initiation therefore rejects its promise (error toast). Steps 2-5 of the flow are ack-only so this is the only blocking one.

Handler: `DorkNet.Server/Controllers/API/Store/PurchaseController.cs:43`

**Fix.** In PurchaseController.Initiate read form fields skuId (int), accessToken (string), purchaseReminderId (int?) and return { TransactionId = <numeric long id> } (e.g. a DB row id or ticks) as a JSON number.

##### `GET subscription/details/{accountId:int}` — SHAPE_MISMATCH (degraded)

GET exists but returns {accountId,subscriberCount,subscribedCount,isSubscribed}. The client's HHOCDLAFOKB reads AccountId, ClubId (Int64), SubscriberCount — ClubId is absent so it defaults to 0, and the calling method is literally GetCreatorClubIdForSubscription: resolving a creator's club id from their profile/subscribe UI always yields 0, breaking the profile→club navigation for subscriptions.

Handler: `DorkNet.Server/Controllers/Clubs/ClubsController.cs:207`

**Fix.** Add ClubId to the response: look up the creator's club (db.Clubs.Where(c => c.CreatorPlayerId == accountId)) and emit { AccountId, ClubId, SubscriberCount } (keep the extra keys if the 2020 watch needs them — extra keys are skipped).

##### `GET api/apple/musicpromotion/active` — SHAPE_MISMATCH (cosmetic)

Client deserializes the body as a BARE JSON boolean; server returns an object {Active,StartAt,EndAt}. If fired, the Boolean reader fails on '{'. Call site is the iOS Apple Music promo, so the PC build likely never issues it.

Handler: `DorkNet.Server/Controllers/API/Apple/AppleMusicPromotionController.cs:13`

**Fix.** Return Content("false","application/json") (or "true" if the promo should show) instead of the object.

##### `GET api/apple/musicpromotion/code` — SHAPE_MISMATCH (cosmetic)

GET is registered (plus an unused POST). Server returns {Code,Redeemed}; client AppleMusicPromotionResponseDTO reads Result (Int32 enum, non-nullable — defaults to 0 or throws), Code, Url, RedemptionUrl. Missing Result/Url/RedemptionUrl. iOS-only on this build.

Handler: `DorkNet.Server/Controllers/API/Apple/AppleMusicPromotionController.cs:22`

**Fix.** Return { Result = <success enum value>, Code = row.Value, Url = "", RedemptionUrl = "" }.

##### `GET api/storefronts/v1/adcarouselitems` — STUB (cosmetic)

Returns a hardcoded empty array. Shape-safe (client parses an empty List<StorefrontAdCarouselItem> and simply shows no banner) and documented as deliberate after a duplicate-AdCarouselItemId crash, but the shop's promo carousel is permanently empty.

Handler: `DorkNet.Server/Controllers/API/Store/StorefrontsController.cs:70`

**Fix.** If banners are wanted: emit rows {AdCarouselItemId (unique Int32), ImageName, Title, Description, PurchasableItemIds:[Int32], PurchaseReminderId:null}.

##### `GET subscription/top/creators/today` — SHAPE_MISMATCH (cosmetic)

GET exists; rows carry AccountId/SubscriberCount in both casings but omit ClubId, which the client's HHOCDLAFOKB also reads — every 'top creator' row has ClubId 0, so opening a top creator's club from the discovery list fails. List itself renders.

Handler: `DorkNet.Server/Controllers/API/PlayerSubscriptions/PlayerSubscriptionsController.cs:54`

**Fix.** Join each top AccountId to its creator club (db.Clubs by CreatorPlayerId) and add ClubId/clubId to the per-row dictionary.

##### `POST api/AppIntegrity/v1/iosproducts` — FIXED (was VERB_MISMATCH)

Server registered GET only; the client POSTs a raw JSON product array (verb `rdx=2` at GAAPALMODOL.txt:106, route literal :104, raw-JSON body via BNDIAONDFFF.FJLLPHFOOJJ :138) and expects {Success:Boolean,Message:String} (PHMHCPEMABG.txt:3/23, keys GBPDOLJBABB.txt:191-218). The POST 405'd, and the GET body (list of {ProductId,Price,CurrencyType,Name}) is not the expected shape either. iOS-only StoreKit report — never fired by the PC build.

Handler: `DorkNet.Server/Controllers/API/Compatibility/CompatibilityFeatureController.cs` (POST) — the pre-existing GET at `DorkNet.Server/Controllers/API/AppIntegrity/AppIntegrityController.cs:13` is left in place; the two verb constraints are disjoint so they do not collide.

**Fixed.** The POST reads the top-level array of {Name:String,Price:Single,ProductId:String} (GAAPALMODOL/KOLPHINEHDD, key table KAONMCEONPF.txt:255-306) and stores the reported StoreKit catalogue as the caller's `appintegrity:iosproducts` PlayerSettings row (`{productId}={price}` pairs, entries dropped once the 1024-char column budget is spent), replacing any earlier report. Returns {Success:true, Message:""}; a malformed body returns Success=false with Message `malformed_product_list`.

### Progression, quests, leaderboards and misc

`progression-misc`

Verified all 30 real HTTP routes of progression-misc against DorkNet source. 24 are OK with exact wire shapes (most live-validated on the march-2023 branch). Defects: (1) BREAKS-GAMEPLAY — POST leaderboard/CheckAndSetStat is not registered anywhere (grep-confirmed); the 2023 client has no SetStat literal, so every leaderboard score write is lost to the catch-all. (2) PUT rooms/{roomId}/playerdata/me (form field 'data') is missing — per-room player save blobs never persist, and the existing GET returns a hardcoded \"CAE=\" stub blob. (3) api/freegifts/v1/sendmultiple never reads the client's ToPlayerIds key → always 400 missing_recipients. (4) api/objectives/v1/cleargroup binds [FromForm(\"group\")] but the client sends JSON {\"Group\":int} → always clears/echoes group 0. (5) GET api/incentivizedreferrals/ serves the progress shape instead of the referral-list DTO, and /claim returns a slim [{rewardSelectionId, rewardType:\"Currency\"}] that conflicts with the proven GLOFCEJBIGB shape (\"Currency\" is not an LJGADMBNNMC enum member). (6) UNKNOWN/at-risk: /incentivizedreferrals/progress (client expects int + milestone list; server emits 6 scalars) and subscriptionseasons/current (client DTO starts with a Guid; server sends \"yyyyMM\") — both need JSON keys extracted from client metadata before they can be authored correctly.

**Client-side notes.** HOW REQUESTS ARE BUILT (applies to whole subsystem): every call goes through builder BNDIAONDFFF (recnet-runtime-decomp/BNDIAONDFFF.cs). Its ctor (RVA 0x30036A0, seen as `Call 0x1830036A0`) takes (BestHTTP.HTTPMethods verb in rdx: 0=GET,2=POST,3=PUT,4=DELETE; GJDLNNLKDIJ host enum in r8; route string in r9). Host enum (GJDLNNLKDIJ.cs): 0=Auth, 1=API, 3=Matchmaking, 9=Leaderboard, 26=Econ. Subsystem host split: PlayerCheer/charades/communityboard/quickPlay -> API host; challenge/checklist/objectives/gamerewards/freegifts/referrals/influencer/wishlists/royale/seasons/earnings -> Econ host (26) — DorkNet must answer these on whatever subdomain maps to Econ (or a catch-all host); leaderboard/* -> Leaderboard host (9); invite/{id} DELETE -> Matchmaking host; role/{role}/{id} -> Auth host. AFGEDDANEKP(key,value) = form-urlencoded field for POST / query param for GET; FJLLPHFOOJJ(string) = raw JSON body; KDOPJCNKOOK = fire-and-forget send; FDKKOPAPDGF/AMMGOPHAAAC = typed JSON sends. Request DTOs serialized with JsonUtility or Newtonsoft KEEP original C# names (ObjectiveGroupRequest.Group, ChecklistCompletionDTO.ItemIndex, MatchCompleteStats.*, SetStatRequestDTO.*, MultiRecipientFreeGiftRequestDTO.*) — these exact PascalCase keys are on the wire. Response DTO property names are obfuscated and their JSON keys live in metadata attributes (NOT visible in Cpp2IL ISIL/decomp); the server's per-endpoint doc comments cite the client's generated readers (which probe Pascal/camel/lower variants) and most are live-validated on the march-2023 branch. GAPS/DISCREPANCIES FOUND (server work needed): (1) leaderboard/CheckAndSetStat is NOT registered — 2023 client has no SetStat literal, so all 2023 stat writes 404; add POST /leaderboard/CheckAndSetStat (+ /api/ prefix) accepting SetStatRequestDTO JSON {StatChannel,RoomId,StatValue,CurrentStatValue?} and returning enum scalar 0/"Success". (2) PUT rooms/{roomId}/playerdata/me (form field `data`) is not registered — only the GETs are — so per-room player save data never persists. (3) api/objectives/v1/cleargroup binds [FromForm(Name="group")] but the 2023 client sends JSON {"Group":int} — group always binds 0. (4) api/freegifts/v1/sendmultiple field probe list lacks ToPlayerIds/toPlayerIds (client's preserved DTO key) — recipients parse empty -> 400. (5) GET api/incentivizedreferrals/ (list, take+continuationToken -> {entries,continuation}) is served by the progress-shaped handler; and /progress's PAJCMPNMKCL {int, List<milestones>} shape is unverified. (6) api/subscriptionseasons/v1/seasons/current: client DTO IGBGJKMGDDK starts with a Guid; server emits a "yyyyMM" string SeasonId — type-mismatch risk, unverified. Cache-key literals mistaken for routes in groups.json: ownedby/{0}, player_room_data/{0}, api/itemWishlists/v1/wishlist/me, search/, search_live/ (real routes documented per entry); format-arg prefixes: api/itemWishlists, api/roomEarningsDistributions.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/PlayerCheer/v1/SetSelectedCheer` | form-urlencoded: CheerCategory (Nullable<Int32> enum; omitted/empty clears selection) | body ignored (LDGADANDBIO fire-and-forget task); server returns {success:true,error:""} |
| POST | `api/PlayerCheer/v1/create` | form-urlencoded fields: PlayerIdTo (Int64), CheerCategory (Int32 enum), RoomId (Int64, only when in a room), Anonymous (Boolean) | object {success:bool, message:string} — JSON keys obfuscated in binary; DorkNet dual-cases Success/success, Message/message, Error/error via Dictionary (validated live) |
| GET | `api/activities/charades/v1/words/{0}` | none; {0} = card-source enum name formatted into path (String.Format of boxed enum) | JSON array of GEFMIBEPMKJ; DorkNet serves [{EN_US:string, Difficulty:int}] from CharadesWordListService (validated live per charades memory/admin library) |
| GET | `api/challenge/v2/getCurrent` | none | object; DorkNet emits {ChallengeMapId:int, CompletedRequired:bool, StartAt/EndAt/ServerTime:DateTime, FallbackGiftName:string, ChallengeThemeString:string, Challenges:[{ChallengeId,Name,Config,Description,Tooltip,Complet |
| POST | `api/challenge/v2/updateProgress` | raw JSON body via BNDIAONDFFF.FJLLPHFOOJJ (Newtonsoft SerializeObject at 0x1817F9920); exact key literals in metadata attributes (not in ISIL). DorkNet binds Id/ChallengeMapId/Chal | body ignored (KDOPJCNKOOK = send-with-no-response) |
| POST | `api/checklist/v1/complete` | JSON body {"ItemIndex": Int32} (DTO name + property preserved in decomp) | gift/balance-update object; DorkNet returns BalanceUpdateResponse(balance, RecCenterTokens, credit, 303) — validated live |
| GET | `api/checklist/v1/current` | none | bare JSON array; DorkNet emits [{Order:int, Objective:int, Count:int, CreditAmount:int}] (superset of the 3 client fields; extras ignored) — validated live |
| GET | `api/communityboard/v2/current` | none | object with board sections; DorkNet emits InstagramImages:[{ImageName,ImageUrl}], FeaturedRoomGroup:{FeaturedRoomGroupId,Name,Rooms:[{RoomName,RoomId,ImageName}]}, video/thumbnail fields (CommunityBoardController documen |
| POST | `api/freegifts/v1/sendmultiple` | raw JSON body = Newtonsoft-serialized MultiRecipientFreeGiftRequestDTO: {"ToPlayerIds":[int], "Message":string, "GiftContext":int} | body ignored (fire-and-forget); server returns {Success,Sent} |
| GET | `api/gamerewards/v1/pending` | none | bare JSON array of reward selections; DorkNet emits [{RewardSelectionId:long, Message:string, GiftContext:int, RewardType:int, GiftDrop1/2/3:EFFIEFEFHHB-shaped store gift drops (AvatarItemId Int32 strict), CreatedAt:Date |
| POST | `api/gamerewards/v1/request` | form-urlencoded: rewardType (enum), Message (string, capital M), giftContext (enum, only when non-null) | body ignored (LDGADANDBIO); server returns {Success,Error} |
| POST | `api/gamerewards/v1/select` | form-urlencoded: rewardSelectionId (Int64, from GLOFCEJBIGB.FEFHNOHNMJL), giftDropId (Int32) | body ignored (LDGADANDBIO); server returns {Success,Error} and grants the item |
| GET | `api/incentivizedreferrals/` | query: take (Int32, optional), continuationToken (string, optional) | object {referral-entry list + continuation token} — exact JSON keys UNKNOWN (obfuscated properties, no reader literals located). DISCREPANCY: DorkNet maps GET api/incentivizedreferrals ('' route) to the same progress-sha |
| POST | `api/incentivizedreferrals/claim` | form-urlencoded: ReferralRewardId (Int32, PascalCase) | bare JSON array of reward selections (empty array = nothing to claim; an object crashes the strict array reader per server doc). DorkNet returns [{rewardSelectionId:long, rewardType:string}] |
| GET | `api/incentivizedreferrals/progress` | none (cached client-side via AMDPEBKIHOH TimeSpan) | object {int + milestone list}; exact JSON keys UNKNOWN (obfuscated). DorkNet emits {ReferralCount,RequiredReferralCount,CanClaim,Claimed,RewardCurrencyType,RewardCurrency} — shape not verified against the 2023 reader |
| GET | `api/influencerpartnerprogram/influencer` | query: accountId (Int32) | bare JSON Int32; 0 means 'not an influencer' (client converter maps 0 -> null); empty body throws 'Response was empty' |
| GET | `api/influencerpartnerprogram/influencers` | query: take (Int32, client sends 1000), continuationToken (string, omitted when null) | object {InfluencerIds:int[], ContinuationToken:string\|null} — MUST be an object not a bare array; non-null ContinuationToken triggers recursive next-page fetch (server doc cites reader FLEDNKHKIND.txt:215-265 registerin |
| GET | `api/influencerpartnerprogram/myinfluencer` | none | bare JSON Int32; 0 = supporting nobody (converter maps 0 -> null) |
| POST | `api/influencerpartnerprogram/remove` | form-urlencoded: influencerAccountId (Int32) | body ignored (fire-and-forget) |
| POST | `api/influencerpartnerprogram/support` | form-urlencoded: influencerAccountId (Int32) | body ignored (fire-and-forget) |
| GET | `api/itemWishlists/v1/wishlist/` | none; accountId concatenated onto path -> GET api/itemWishlists/v1/wishlist/{accountId} | bare JSON array of BFJNGMGONED (empty array valid; 404/empty body crashes with 'Response was empty') |
| POST | `api/objectives/v1/cleargroup` | raw JSON body via JsonUtility.ToJson: {"Group": Int32} | object; DorkNet emits {Group:int, IsCompleted:bool, ClearedAt:string(ISO), RequiresCompleteOnServer:bool, IsRewarded:bool}. DISCREPANCY: server binds [FromForm(Name="group")] (ProgressionController.cs:386-390) but the 20 |
| POST | `api/objectives/v1/completegroup` | raw JSON body {"Group": Int32} (JsonUtility) | object; DorkNet emits {Group:int, IsCompleted:bool, ClearedAt:string, RequiresCompleteOnServer:bool, IsRewarded:bool, Rewarded:bool} via [FromBody] binding — validated live |
| GET | `api/objectives/v1/myprogress` | none | object; DorkNet emits {Objectives:[{Index:int,Group:int,Progress:float,VisualProgress:float,IsCompleted:bool,HasClaimedReward:bool}], ObjectiveGroups:[{Group,IsCompleted,ClearedAt,RequiresCompleteOnServer,IsRewarded}]} — |
| POST | `api/objectives/v1/updateobjective` | raw JSON body (Newtonsoft serialize at 0x1817F9920 -> FJLLPHFOOJJ); DorkNet binds UpdateObjectiveRequest {Group,Index,Progress,IsCompleted} case-insensitively | loosely handled — client uses FPCPAJAAHME (raw response, no strict typed parse); DorkNet returns {Index,Group,Progress,VisualProgress,IsCompleted,HasClaimedReward} |
| GET | `api/quickPlay/v1/getandclear` | none | object with OBFUSCATED keys — DorkNet's QuickPlayLaunchTarget emits {OKCGKFJELAC:bool, INMKAFBAOPC:string, FIOPJIIOCGA:int?, ICKOEFDNOPM:string, KLDCJBLLDKN:int?, OFBMKDAONNE:bool, FMKHKNCEBCN:object?, EEFNKAFLPLG:long?} |
| POST | `api/roomEarningsDistributions/v1/earningsDistribution` | raw JSON body = serialized distribution {RoomId:long, mapping Dictionary<accountId:int, percent:byte>, method enum} (Newtonsoft; exact key literals in metadata — DorkNet reads Room | the saved distribution object (same shape as GET); server echoes BuildDto(roomId, method, mapping) |
| GET | `api/royale/v1/current` | none | object {TotalXP:long, Level:int, RankIdx:int, RankName:string, CurrentLevelXPThreshold:long, NextLevelXPThreshold:long, NextLevelAcornReward:int} (CLR types match PLCAPKKIEAK exactly; validated live) |
| POST | `api/royale/v2/matchcomplete` | raw JSON body (JsonUtility.ToJson): {"Rank":int, "NumEliminations":int, "SecondsAlive":int, "WalkGame":bool, "CustomGame":bool, "ChestsOpened":int, "ShieldPotionsConsumed":int, "He | object {XPAwardStrings:List<string>, TotalXPAwarded:long, NewProgress:[PLCAPKKIEAK]} — CLR types match MFDLADFOKPN exactly; validated live |
| GET | `api/subscriptionseasons/v1/seasons/current` | none (client caches with TimeSpan.FromHours) | object; exact JSON keys UNKNOWN (obfuscated properties). RISK: first property is a Guid — DorkNet's SubscriptionSeasonsController emits SeasonId as "yyyyMM" string plus {Name, StartAt, EndAt, ActiveSubscriberCount, Rewar |
| DELETE | `invite/{0}` | none; {0} = invite/message id in path. Issued against host 3 = Matchmaking (match.* subdomain) | body ignored (KDOPJCNKOOK fire-and-forget) |
| POST | `leaderboard/CheckAndSetStat` | raw JSON body = Newtonsoft-serialized SetStatRequestDTO: {"StatChannel":int, "RoomId":long, "StatValue":int, "CurrentStatValue":int\|null} | deserialized into enum KLLFFIDADCN — a bare JSON scalar works (0 or "Success"); Newtonsoft enum reader accepts int or name string. GAP: DorkNet registers only leaderboard/SetStat (LeaderboardController.cs:75-76); the 202 |
| POST | `leaderboard/GetNearbyScores` | raw JSON body (closure pre-serializes DTO to string, passed via FJLLPHFOOJJ): keys are the preserved DTO field names above | single-list wrapper; DorkNet emits {rows:[{playerId:int, score:long, rank:int}]} (2020-validated SingleLeaderboard shape; must be an object with the list key, never a bare array) |
| POST | `leaderboard/GetPlayerRank` | raw JSON body = serialized GetRankRequestDTO (preserved field names) | single entry object; DorkNet emits {playerId:int, score:long, rank:int} (camelCase per its FullLeaderboard.Entry evidence; zero-entry {playerId:0,score:0,rank:0} valid) |
| POST | `leaderboard/GetRanks` | raw JSON body = serialized GetRanksRequestDTO (preserved field names) | {rows:[{playerId:int, score:long, rank:int}]} wrapper (same as GetNearbyScores) |
| GET | `role/{0}/{1}` | none; path = role/{roleName-UriEscaped}/{accountId}; roleName literals passed by the two call sites are "developer" and "moderator". Issued against host 0 = Auth (auth.* subdomain) | bare JSON true/false |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `api/itemWishlists` — Add/remove item on own wishlist from the store item detail page
- `api/itemWishlists/v1/wishlist/me` — Cache invalidation after wishlist add/remove
- `api/roomEarningsDistributions` — Opening the room co-owner earnings-split settings page
- `ownedby/{0}` — 'My Rooms' / another player's created-rooms list (cached)
- `player_room_data/{0}` — Per-room player save data (room progress blob) load on room join / save on change
- `search/` — Player-events search on the watch events tab (cached)
- `search_live/` — Live/ongoing player-events search (cached)

#### Defects

##### `POST leaderboard/CheckAndSetStat` — MISSING (breaks-gameplay)

CONFIRMED absent: repo-wide grep for 'CheckAndSetStat' matches only docs and client-routes.txt — no controller registers it. LeaderboardController only has POST /leaderboard/SetStat (LeaderboardController.cs:75-76), a literal the 2023 GMHDPPLGMDP does not contain. The 2023 client's only stat-write route therefore falls through to GlobalCatchAllController (200 {} per Data/url-coverage.md), the enum deserialize of '{}' into KLLFFIDADCN fails, and NO leaderboard score from the 2023 client is ever persisted (Stunt Runner times etc. silently lost). Note the existing SetStat ack {success:true,error:""} would ALSO be wrong for this caller — the 2023 response type is the bare enum scalar.

**Fix.** Add [HttpPost("/leaderboard/CheckAndSetStat")] (+"/api/leaderboard/CheckAndSetStat") to LeaderboardController binding a DTO {StatChannel:int, RoomId:long, StatValue:int, CurrentStatValue:int?} (extend SetStatRequest with CurrentStatValue), reuse the SetStat upsert logic, and return the bare enum scalar — Content("0", "application/json") for Success.

##### `POST api/freegifts/v1/sendmultiple` — SHAPE_MISMATCH (degraded)

Request-side key mismatch, CONFIRMED: the 2023 client's Newtonsoft body uses the preserved DTO key ToPlayerIds (RecNet.MultiRecipientFreeGiftRequestDTO), but ReadLongList at line 22 probes only recipientPlayerIds/RecipientPlayerIds/playerIds/PlayerIds. The JSON reader (ReadFieldsAsync) does capture 'ToPlayerIds' into the case-insensitive field dict, but no probed name matches it, so recipients parse empty and line 23 returns 400 'missing_recipients' — every free-gift send from the 2023 client fails. Message binds fine ('Message' matches case-insensitively); GiftContext is not read (gift stored with GiftContext=0, minor). Response body is ignored by the client so the 400 surfaces only as a failed send.

Handler: `DorkNet.Server/Controllers/API/FreeGifts/FreeGiftsController.cs:17`

**Fix.** In FreeGiftsController.cs:22 add "ToPlayerIds" (and "toPlayerIds") to the ReadLongList probe list; optionally also read GiftContext into the stored gift.

##### `GET api/incentivizedreferrals/` — SHAPE_MISMATCH (degraded)

CONFIRMED route-shape confusion: [HttpGet("")] and [HttpGet("progress")] share one handler returning the progress object {ReferralCount,RequiredReferralCount,CanClaim,Claimed,RewardCurrencyType,RewardCurrency}. The client's GET on the bare route expects the referral LIST wrapper GPJIMAHMAIO {List<JIMHNOHLOFM> entries + continuationToken} and passes take/continuationToken query params, which the handler ignores. The client gets an object with no list key — the entries list binds missing (default/null), so the refer-a-friend list page shows nothing or NREs. Exact client JSON keys are UNKNOWN (obfuscated properties, no reader literals located), so the correct emission cannot be fully authored yet.

Handler: `DorkNet.Server/Controllers/API/IncentivizedReferrals/IncentivizedReferralsController.cs:18`

**Fix.** Split GET "" into its own handler accepting take+continuationToken and returning the list DTO; first extract JIMHNOHLOFM/GPJIMAHMAIO JSON keys from the client's metadata-attribute readers (global-metadata / generated reader dump), then emit those keys (dual-cased via Dictionary if ambiguous).

##### `POST api/incentivizedreferrals/claim` — SHAPE_MISMATCH (degraded)

Client deserializes the response as List<AFEFBIKADAP/GLOFCEJBIGB> — the SAME reward-selection DTO as gamerewards/pending, whose working live-validated wire shape is {RewardSelectionId,Message,GiftContext,RewardType(int),GiftDrop1/2/3,CreatedAt}. The server instead returns [{rewardSelectionId, rewardType:"Currency"}]: (a) 'Currency' is not a member of the LJGADMBNNMC enum (FirstActivityOfDay..ReferralReward) so an enum-name parse fails; (b) all other GLOFCEJBIGB fields (Message, GiftContext, GiftDrops, CreatedAt) are absent and will crash if the strict reader requires them. Empty-array no-op path is correct. Also ignores the client's ReferralRewardId form field (harmless — single reward). PLAUSIBLE-crash, not live-verified.

Handler: `DorkNet.Server/Controllers/API/IncentivizedReferrals/IncentivizedReferralsController.cs:43`

**Fix.** Rebuild the claim response using the same ToWire shape as GameRewardsController (RewardSelectionId long, RewardType=6 int for ReferralReward, Message, GiftContext, GiftDrop1-3 via StoreService.BuildGiftDrop or null, CreatedAt) — ideally create a real GameRewardSelection row and return it.

##### `GET api/incentivizedreferrals/progress` — UNKNOWN (degraded)

Handler exists on the right verb, but the client DTO PAJCMPNMKCL is {int, List<ALOFEHAHGII>} (an int plus a MILESTONE LIST whose rows are {DateTime?,DateTime?,enum,enum,bool}), while the server emits six scalars ({ReferralCount,RequiredReferralCount,CanClaim,Claimed,RewardCurrencyType,RewardCurrency}) and no list at all. The obfuscated JSON keys are unlocated so a definitive verdict is impossible, but a required list key missing would make the referral progress meter empty or crash the strict reader. Unverified against the 2023 reader.

Handler: `DorkNet.Server/Controllers/API/IncentivizedReferrals/IncentivizedReferralsController.cs:18`

**Fix.** Extract PAJCMPNMKCL/ALOFEHAHGII JSON keys from client metadata, then emit {count, milestones:[{start?,end?,enumA,enumB,claimed}]} under those keys; keep the current shape only if metadata proves the reader tolerates missing keys.

##### `POST api/objectives/v1/cleargroup` — SHAPE_MISMATCH (degraded)

Request-side binding mismatch, CONFIRMED: handler binds only [FromForm(Name="group")] with no JSON fallback, but the 2023 client sends a raw JSON body {"Group":int} (JsonUtility.ToJson of ObjectiveGroupRequest). With a JSON content type the form provider is never populated, group binds null, and the handler clears/rewards group 0 and echoes Group=0 regardless of which group the player claimed — daily-objective group claims hit the wrong group and the echoed ObjectiveGroupProgress desyncs the watch's group state.

Handler: `DorkNet.Server/Controllers/API/Progression/ProgressionController.cs:386`

**Fix.** Change ClearGroup to accept the JSON body: bind [FromBody] CompleteGroupBody (like completegroup) with a form fallback for older clients, or reuse the ReadChallengeRequestAsync-style dual reader.

##### `GET api/subscriptionseasons/v1/seasons/current` — UNKNOWN (degraded)

Handler exists on the right verb, but the response is unverified against the 2023 strict reader and has a concrete type-mismatch risk: client DTO IGBGJKMGDDK is {Guid, string, string, DateTime, DateTime?, List<AEDILOEPMFC>} while the server emits SeasonId="yyyyMM" (NOT parseable as a Guid), only one name string, plus ActiveSubscriberCount (extra) and Rewards rows ({RewardId,Slug,Name,Category,ImageName}) whose shape was never checked against AEDILOEPMFC. If the reader Guid-parses the season id or requires the second string/reward keys, the Rec Room Plus season UI errors. JSON keys are obfuscated and unlocated — cannot confirm either way.

Handler: `DorkNet.Server/Controllers/API/SubscriptionSeasons/SubscriptionSeasonsController.cs:10`

**Fix.** Emit the season id as a real stable Guid string (e.g. deterministic Guid from yyyyMM) and add a second string field; extract IGBGJKMGDDK/AEDILOEPMFC JSON keys from client metadata before finalizing the reward-row shape.

##### `PUT rooms/{roomId}/playerdata/me` — MISSING (degraded)

The 2023 client saves its per-room player blob via PUT rooms/{roomId}/playerdata/me with form field data=<base64> (NLDBPDCNNCF.txt:2566-2586, verb 3 PUT; form key at NLDBPDCNNCF_NestedType_ANPKFGHOOAJ.txt:57). Grep confirms the server registers only the GETs ([HttpGet("player_room_data/{roomId:long}")] + [HttpGet("rooms/{roomId:long}/playerdata/me")] at :2307-2308) — the PUT falls to the catch-all and per-room save data never persists. Compounding it, the GET is a partial STUB for this client: it returns a hardcoded Data="CAE=" blob (:2331) rather than anything previously saved, so even after adding the PUT the round-trip needs real storage.

Handler: `DorkNet.Server/Controllers/API/Rooms/V2/RoomsController.cs:2307 (GET only)`

**Fix.** Add [HttpPut("rooms/{roomId:long}/playerdata/me")] reading form field 'data' and persisting it per (playerId, roomId) (new entity or PlayerSettings key), and change PlayerDataForMe to return the stored blob in Data (falling back to "CAE=" when none), keeping the existing extra keys.

### Moderation, reporting and safety

`moderation-safety`

18 client endpoints audited against DorkNet server source (all handlers read directly). 10 are fine. Breaking defects: POST api/playerwarnings gets 405 (server registers GET only); POST api/playerwarnings/acknowledge always 400s (server demands warningId the 2023 client never sends); api/banappeal/generateCode is POST-only server-side while the client GETs it (405) and both generateCode endpoints return JSON objects where the client deserializes a bare string; api/PlayerReporting/v1/instantKick never reads the client's raw-JSON KickPlayerDTO body so every kick no-ops with success:false; v3/voteToKick is an acknowledged stub that tallies nothing. Data-loss defects: screensharereports binds targetPlayerId/reportCategory names the client never sends (report stored with no target/image), and clubreporting reads the report text from "message" while the client sends "details" (text dropped).

**Client-side notes.** All 19 literals in the moderation-safety group are real HTTP requests (none are cache keys/deeplinks). The four v1/{mute,unmute,ignore,unignore} literals are suffixes concatenated with "api/relationships/" at EEGNOHOELBG.txt:4182. Verb encoding proven from BNDIAONDFFF..ctor(BestHTTP.HTTPMethods, GJDLNNLKDIJ, String) (BNDIAONDFFF.txt:74): rdx 0=GET, 2=POST; r8 is a host-selector enum (1=main api host everywhere here except the two voice/* routes which use 16 = the voice/ToxMod service host — DorkNet must keep serving them at root path). Request bodies are x-www-form-urlencoded via BNDIAONDFFF.AFGEDDANEKP(key,value) with exact key literals in the ISIL, EXCEPT v1/instantKick which posts raw JSON (JsonUtility.ToJson of RecNet.KickPlayerDTO via FJLLPHFOOJJ). Response DTO JSON keys are Newtonsoft-attribute metadata and never appear in the ISIL — only field counts/types are recoverable (established DorkNet workaround: dual-cased dictionaries; Newtonsoft matches case-insensitively). SERVER ACTION ITEMS found while auditing (all verified against controller source): (1) POST api/playerwarnings is 405 — server only registers GET (PlayerWarningsController.cs:13); the 2023 client uses POST to create moderator warnings. (2) POST api/playerwarnings/acknowledge always 400s — client sends no warningId (PlayerWarningsController.cs:31-32 requires it); should acknowledge the pending warning implicitly. (3) api/banappeal/generateCode is called with GET but server is POST-only (CompatibilityFeatureController.cs:15) → 405; and both generateCode endpoints must return a bare JSON string, not an object (AgeVerificationController.cs:29 / CompatibilityFeatureController.cs:28) — client deserializes TResponse=String. (4) api/PlayerReporting/v1/instantKick: 2023 client sends a JSON KickPlayerDTO body which the form/query bindings never read (InGameModerationController.cs:116-134) → kick silently no-ops with success:false. (5) api/screensharereports/v1/report: server binds targetPlayerId/reportCategory which the client never sends; real keys are ImageName/ReportedPlayerId/RoomId/RoomInstanceId/RoomInstanceType/Details (ScreenShareReportsController.cs:15-18). (6) api/clubreporting/v1/report: report text arrives under "details" but server reads "message" (CompatibilityFeatureController.cs:158) → text dropped. Non-breaking deltas: v3/create's RoomInstanceType and voteToKick's Reason are sent but unbound; hile's ReportedPlayer is unpersisted; moderationBlockDetails' 2023 DTO gained ~5 fields (extra bools, string, DateTime?, float) whose keys are unknown but default safely.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `api/PlayerReporting/v1/deviceId` | form fields: oldDeviceId=String, newDeviceId=String (current device id from static getter), platform=Int32 (HHJIBNMLOAC enum). Note lowercase keys. | Body ignored — client uses BNDIAONDFFF.KDOPJCNKOOK (non-generic Task); any 2xx suffices. |
| POST | `api/PlayerReporting/v1/hile` | form fields: Message=String, Type=Int32 (HHKINDNBLEE cheat-type enum), ReportedPlayer=boxed Nullable<Int32> (omitted/empty when null) | Bare JSON boolean (true/false) — parsed via BNDIAONDFFF.FDKKOPAPDGF<Boolean>, then Action<Boolean> continuation. |
| POST | `api/PlayerReporting/v1/instantKick` | RAW JSON body (NOT form): UnityEngine.JsonUtility.ToJson of RecNet.KickPlayerDTO with field at offset16=Int64 game session id (same Matchmaking+16 field posted as GameSessionId els | PHMHCPEMABG { Boolean, String }. Failure toast: "Failed instant kick". |
| GET | `api/PlayerReporting/v1/moderationBlockDetails` | none | Single JSON object (NOT an array) deserialized into PIBLFGPOCIB. 2023 DTO has 12 properties (getters in PIBLFGPOCIB.txt): report-category enum (FPIBGPIAOBI/BDOGOIGCKMK), Int32, Int64, Boolean, String, Nullable<Int32>, 3x |
| GET | `api/PlayerReporting/v1/voteToKickReasons` | none | JSON array List<FINKMCHCBMP>; FINKMCHCBMP (nested in FPIBGPIAOBI) = { BDOGOIGCKMK category enum (getter CEECFKOHHHC), String reason text (getter MKDJAFJMMOL) }. Exact keys not in ISIL; server serves [{ReportCategory:int, |
| POST | `api/PlayerReporting/v3/create` | form fields (BNDIAONDFFF.AFGEDDANEKP): PlayerIdReported=Int32, ReportCategory=Int32 (BDOGOIGCKMK enum), Details=String, HeightReporter=String (float ToString "F2" InvariantCulture) | PHMHCPEMABG { Boolean success-flag (getter GJLFIFEJDEH), String message (getter ABGLLJPIMIO) }. Exact JSON keys not in ISIL (attribute metadata); Newtonsoft matches case-insensitively — DorkNet convention is dual-cased S |
| POST | `api/PlayerReporting/v3/voteToKick` | form fields: PlayerId=Int64, Response=Boolean, Reason=String, GameSessionId=Int64 (Matchmaking current session, offset +16) | PHMHCPEMABG { Boolean, String } as above. Failure toast: "Failed vote to kick". |
| POST | `api/ageverification/generateCode` | none | Bare JSON string (the code), e.g. "123456" — TResponse is String inside the IPCJLCNIBEG envelope (same parse family as hile's bare Boolean). Failure toast: "Failed to generate action code". |
| GET | `api/banappeal/generateCode` | none (gated on a logged-in check before issuing) | Bare JSON string (the appeal code) — continuation is Func<IPCJLCNIBEG<String>, FGLDKEJLAKB<String>>. Failure toast: "Failed to generate action code". |
| POST | `api/clubreporting/v1/report` | form fields (note camelCase, unlike PlayerReporting): clubId=Int64, reportCategory=Int32, details=String | PHMHCPEMABG { Boolean, String } (continuation Func<PHMHCPEMABG, LDGADANDBIO>). |
| POST | `api/playerwarnings` | form fields: WarnedPlayerId=Int32, ReportCategory=Int32, DisplayReason=String, ModeratorNote=String. Gated client-side behind an ObscuredBool (moderator capability flag); non-moder | DJMHAFPGLLN { Boolean (getter GJLFIFEJDEH), String (getter BGKMGILIJCM) }. Exact keys UNKNOWN (attribute metadata); plausibly Success + WarningId given the server's acknowledge response shape. |
| POST | `api/playerwarnings/acknowledge` | none — no form fields, no query. The client acknowledges its pending warning implicitly. | DJMHAFPGLLN { Boolean, String }. Failure toast: "Failed to acknowledge warning". |
| POST | `api/screensharereports/v1/report` | form fields: ImageName=String, ReportedPlayerId=Int32, RoomId=Int64 (Matchmaking+24), RoomInstanceId=Int64 (Matchmaking+16), RoomInstanceType=Int32 (APFAHOMCEHP enum), Details=Stri | PHMHCPEMABG { Boolean, String } (continuation Func<PHMHCPEMABG, LDGADANDBIO>). |
| POST | `v1/ignore` | full route api/relationships/v1/ignore; form field PlayerId=Int32 | Body ignored; any 2xx. |
| POST | `v1/mute` | Literal is a suffix: full route = String.Concat("api/relationships/", "v1/mute"). Form field: PlayerId=Int32. | Body ignored (BNDIAONDFFF.KDOPJCNKOOK non-generic Task; the returned Boolean is a local optimistic value). Any 2xx suffices. |
| POST | `v1/unignore` | full route api/relationships/v1/unignore; form field PlayerId=Int32 | Body ignored; any 2xx. |
| POST | `v1/unmute` | full route api/relationships/v1/unmute; form field PlayerId=Int32 | Body ignored; any 2xx. |
| GET | `voice/config` | none. NOTE: issued against host enum GJDLNNLKDIJ=16 (a separate voice-service base URL, not the value-1 api host used by everything else in this subsystem). | JSON object deserialized into MCNMMGPAJPL/FOACAAFJOIK = { String (getter ADDIBBGJMJO), String (getter KCEEJCOHCKE) } — two strings, plausibly endpoint + API key; exact keys UNKNOWN (attribute metadata, no literals in ISI |
| GET | `voice/requiresModeration` | none. Same voice host enum 16. | Bare JSON boolean (true/false) via BNDIAONDFFF.FDKKOPAPDGF<Boolean>. |

#### Defects

##### `POST api/PlayerReporting/v1/instantKick` — SHAPE_MISMATCH (breaks-gameplay)

Request-body mismatch: the 2023 client posts a RAW JSON body — JsonUtility.ToJson(RecNet.KickPlayerDTO) = presumed {"GameSessionId":long,"PlayerIds":[int]} (exact field spelling UNKNOWN, but definitely JSON, not a form) via BNDIAONDFFF.FJLLPHFOOJJ. The server only reads [FromQuery playerId], [FromForm PlayerId], [FromForm PlayerIds] (:117-119); with a JSON body none of these bind, ids stays empty, and :134 answers {success:false,error:"missing_target"} — every moderator instant-kick silently no-ops (client shows 'Failed instant kick').

Handler: `DorkNet.Server/Controllers/API/Moderation/InGameModerationController.cs:114-144`

**Fix.** In InGameModerationController.InstantKick, when !Request.HasFormContentType read Request.Body as JSON (case-insensitive) into a DTO with long GameSessionId + List<long> PlayerIds (also tolerate playerIds/gameSessionId casings since exact JsonUtility field names are unverified — accept any member whose name matches case-insensitively) and merge those ids into the kick list before the ids.Count==0 check.

##### `POST api/PlayerReporting/v3/voteToKick` — STUB (degraded)

Handler exists, accepts POST, binds PlayerId/Response/GameSessionId, and returns {success:true,error:""} — the wire contract is satisfied so no toast fires — but the comment at :99-102 admits it accepts the vote 'without side-effects': no per-(session,target) tally, no threshold, no kick ever issued. Vote-to-kick therefore silently never kicks anyone. Client-sent 'Reason' form field is also unbound (cosmetic).

Handler: `DorkNet.Server/Controllers/API/Moderation/InGameModerationController.cs:88-104`

**Fix.** In InGameModerationController.VoteToKick, tally yes-votes per (gameSessionId, playerId) (in-memory or DB), and once a majority/threshold of the room instance's players vote yes, invoke notifications.KickPlayerAsync(target, reason) like InstantKick does (:141). Also bind [FromForm(Name="Reason")] and pass it into the kick message.

##### `POST api/playerwarnings` — VERB_MISMATCH (degraded)

The 2023 client only POSTs this route (moderator creates a warning: form WarnedPlayerId/ReportCategory/DisplayReason/ModeratorNote) — but the server registers [HttpGet("api/playerwarnings")] only, so the POST gets HTTP 405 and warning creation always fails (client shows local failure DJMHAFPGLLN). The existing GET list handler is a 2020-era shape the 2023 client never calls. Grep confirms no other file registers this route.

Handler: `DorkNet.Server/Controllers/API/PlayerWarnings/PlayerWarningsController.cs:13`

**Fix.** Add [HttpPost("api/playerwarnings")] CreateWarning to PlayerWarningsController binding [FromForm] WarnedPlayerId(int)/ReportCategory(int)/DisplayReason(string)/ModeratorNote(string), gate on IsAdmin||IsDeveloper, store a warning row (existing PlayerSettings 'warning:{id}' scheme with Value 'message|category|createdAt|false'), push a warning notification to the target, and return the DJMHAFPGLLN-compatible object {Success:true, WarningId:"..."} (exact 2023 keys UNKNOWN — case-insensitive bool 'Success' is the established safe bet, matching the acknowledge handler's shape at :39).

##### `POST api/playerwarnings/acknowledge` — SHAPE_MISMATCH (degraded)

Route+verb exist, but the 2023 client sends NO body and NO query (implicit 'acknowledge my pending warning') while the server requires warningId and returns BadRequest("missing_warning") at :32 when absent — so every acknowledge 400s, the promise rejects, and the 'Failed to acknowledge warning' toast fires with the warning never marked read (modal re-appears every login).

Handler: `DorkNet.Server/Controllers/API/PlayerWarnings/PlayerWarningsController.cs:27-40`

**Fix.** In PlayerWarningsController.Acknowledge, when no warningId is supplied, fall back to the caller's newest unacknowledged 'warning:*' PlayerSettings row (OrderByDescending(Id), Acknowledged==false), mark it acknowledged, and return {Success:true, WarningId:...}; return {Success:true} (not 4xx) even when there is nothing pending so the modal always dismisses.

##### `POST api/ageverification/generateCode` — SHAPE_MISMATCH (degraded)

Verb OK (POST+GET both registered), but the response is a JSON object {Code,VerificationCode,ExpiresAt} (:29) while the client's TResponse is String — Newtonsoft DeserializeObject<string> of a JSON object throws, the promise rejects, and the 'Failed to generate action code' toast fires; the age-verification code is never shown. (PLAUSIBLE severity — the throw-vs-null behavior of the client's parse helper isn't binary-proven, but an object can never yield the string the UI displays.)

Handler: `DorkNet.Server/Controllers/API/AgeVerification/AgeVerificationController.cs:16-30`

**Fix.** In AgeVerificationController.GenerateCode return the bare JSON string instead of an object: Content(JsonSerializer.Serialize(code), "application/json") (i.e. body '"123456"'), mirroring ReportsController.HileResult's bare-scalar pattern.

##### `GET api/banappeal/generateCode` — VERB_MISMATCH (degraded)

Two defects: (1) CONFIRMED verb mismatch — client calls GET (FPIBGPIAOBI.txt:2980-2982, verb rdx=0) but the server registers [HttpPost] only (:15-16) → HTTP 405, ban-appeal code generation always fails. (2) Even via POST the response {value=code} (:28) is a JSON object where the client deserializes a bare String (Func<IPCJLCNIBEG<String>,...> continuation) — same bare-scalar problem as ageverification.

Handler: `DorkNet.Server/Controllers/API/Compatibility/CompatibilityFeatureController.cs:15-29`

**Fix.** In CompatibilityFeatureController.GenerateBanAppealCode add [HttpGet("api/banappeal/generateCode")] alongside the existing HttpPost, and change the response to the bare JSON string body '"BA-123456"' (Content(JsonSerializer.Serialize(code), "application/json")).

##### `POST api/clubreporting/v1/report` — SHAPE_MISMATCH (degraded)

Request-binding mismatch: client sends form keys clubId/reportCategory/details (IKMMOCKDKAF.txt:25303-25322); the server reads clubId and reportCategory correctly but takes the report text from form["message"]/["Message"] (:158) — 'details' is never read, so every club report is stored with an empty message (only the '[club {id}]' prefix). Response {Success,Message} satisfies PHMHCPEMABG case-insensitively, so the client UI shows success while the report body is silently dropped.

Handler: `DorkNet.Server/Controllers/API/Compatibility/CompatibilityFeatureController.cs:99-124, 147-168`

**Fix.** In ReadClubReportAsync (:158) extend the fallback chain: form["details"] / form["Details"] before or after the message keys (and add Details to ClubReportRequest for the JSON path).

##### `POST api/screensharereports/v1/report` — SHAPE_MISMATCH (degraded)

Request-binding mismatch: client sends form keys ImageName/ReportedPlayerId/RoomId/RoomInstanceId/RoomInstanceType/Details (KAEAEIODGBG.txt:281-360); server binds targetPlayerId/roomId/reportCategory/details (:15-18). Form binding is case-insensitive so RoomId and Details do land, but 'ReportedPlayerId' never binds to 'targetPlayerId' (target stored as 0), 'reportCategory' is never sent (defaults to 5), and ImageName/RoomInstanceId/RoomInstanceType are dropped entirely — the stored report identifies neither the offender nor the offending image. Response {Success,Error} satisfies PHMHCPEMABG, so the client shows success.

Handler: `DorkNet.Server/Controllers/API/ScreenShareReports/ScreenShareReportsController.cs:13-39`

**Fix.** In ScreenShareReportsController.Report bind the client's exact keys: [FromForm(Name="ReportedPlayerId")] long?, [FromForm(Name="ImageName")] string?, [FromForm(Name="RoomId")] long?, [FromForm(Name="RoomInstanceId")] long?, [FromForm(Name="RoomInstanceType")] int?, [FromForm(Name="Details")] string?; store ReportedPlayerId as TargetPlayerId and prefix/append ImageName+RoomInstanceId into the Message (or dedicated columns) so the report is actionable.

### Config, announcements, feed and telemetry

`config-telemetry`

Audited all 27 real HTTP routes of the config-telemetry subsystem against DorkNet (march-2023-03-21 branch), reading every server handler and spot-verifying disputed client shapes in the ISIL dump. 11 routes are fully OK (freegiftbutton, gameconfigs, emoji whitelist, pageview/consume, testpass reads, claim/unclaim, announcement delete/read). WORST FINDINGS: (1) PUT announcements/club/{clubId}/{announcementId} — the client's EDIT — is bound to the DELETE handler (ClubsController.cs:762), so editing a club announcement silently destroys it; (2) POST announcements/club/{clubId} (create) is not registered at the collection URL, so posting announcements 404s; (3) three deserializer-breaking shape mismatches verified against client formatters: config/categories returns a {Results,TotalResults} envelope where the 2023 client needs a bare array (LELAJKMOMIA.txt:1552), settings/partyinvite returns a bare int where the client needs {InviteLinkLifetimeInMinutes} (AIEIPLDGFCF.txt:151), and GET announcements/club/{clubId} returns an array where the client needs a single {ClubId,LastReadAnnouncementId,Announcements} object (IKMMOCKDKAF.txt:1170) — plus LastReadAnnouncementId serialized as JSON null into a non-nullable Int64. MISSING ROUTES: POST feed/query (2023 home feed), all five actionlink/datalink endpoints (share-code flows), GET announcements/mine + announcements/subscription/mine, POST testcase/{id}/comment; also POST testcase/{id}/status exists but binds [FromBody] object while the client sends a bare-integer body, so it always 400s. LOSSY-BUT-BENIGN: api/config/v2 lacks StorefrontConfig/RoomKeyConfig/RoomCurrencyConfig/GiftDropId; azurespeech sends SubscriptionKey instead of the client's "Key"; amplitude lacks RudderStack/StatSig keys; backtrace lacks 7 of 9 keys; cohortnux lacks the two Description keys; announcement v1 lacks LinkButtonLabel. The 2023 tri-cased generated readers make key casing forgiving and missing keys default, so unlike 2020 none of the lossy gaps throw — the defects that matter are the wrong-handler PUT, missing routes, and array-vs-object / object-vs-scalar mismatches.

**Client-side notes.** HOW REQUESTS ARE BUILT (applies to every endpoint here): RecNet.Runtime.BNDIAONDFFF is the shared request builder — ctor (BestHTTP.HTTPMethods verb, GJDLNNLKDIJ host, string route) per C:\tmp\recnet-runtime-decomp\BNDIAONDFFF.cs:194 and delegate line 14. In ISIL call sites the ctor is Call 0x1830036A0 with rdx=verb (0=GET, 2=POST, 3=PUT, 4=DELETE — BestHTTP order), r8=host enum, r9=route. Host enum GJDLNNLKDIJ (GJDLNNLKDIJ.cs): 1=API, 8=Chat, 10=Accounts, 11=Link, 13=Clubs, 15=PlatformNotifications, 19=Discovery. AFGEDDANEKP(key,value) = query param on GET (server's working [FromQuery] backtrace handler corroborates) and url-encoded form field on POST/PUT; FJLLPHFOOJJ(string) = raw JSON body; FDKKOPAPDGF<T> = send expecting JSON T; KDOPJCNKOOK = send ignoring response body; AMDPEBKIHOH = client-side cache TTL. FGLDKEJLAKB<T> is the client promise type; the generic argument at each call site IS the response DTO.

DESERIALIZATION: every DTO has a generated formatter that registers each JSON key three times — exact PascalCase, camelCase, and all-lowercase (e.g. CDEDNEPPPND.txt:599-793) — so server key casing is forgiving for these DTOs, unlike some legacy 2020 readers. Unrecognized keys are ignored; missing keys default (no throw observed in these generated readers, unlike 2020 Util.GetKey readers).

KEY 2023-vs-2020 DELTA: api/config/v2 reader (CONDPLKKBPI via CDEDNEPPPND) reads ONLY LevelProgressionMaps/DailyObjectives/ServerMaintenance/AutoMicMutingConfig/StorefrontConfig/RoomKeyConfig/RoomCurrencyConfig/ShareBaseUrl. The 2020-era keys DorkNet also sends (MessageOfTheDay, CdnBaseUri, PhotonConfig, MatchmakingParams, ConfigTable, ServiceUrls) are ignored by the 2023 client. New nested key: LevelProgressionMaps[].GiftDropId (Int32?).

SERVER GAPS FOUND (routes the 2023 client can hit that DorkNet does not register, per the server's reflected route table): (1) POST actionlink, GET actionlink/{code}, POST actionlink/{code}/consume — Link host [NOW FIXED, LinkController.cs]; (2) PUT datalink, GET datalink/{code} — Link host [NOW FIXED, LinkController.cs]; (3) POST feed/query — Discovery host (the existing GET "feed" is api/photos/v1/feed, unrelated); (4) GET announcements/mine and GET announcements/subscription/mine — Clubs host; (5) POST announcements/club/{clubId} (create at collection URL — ClubsController only registers POST at .../{announcementId}); (6) POST api/testcasemanagement/v1/testcase/{id}/comment. Also verify announcements/v2/*/unread handlers honor ?sendAnnouncements=true by embedding Announcements arrays.

SERVER SHAPE MISMATCHES (working but lossy): api/config/v1/azurespeech — client key is "Key", server sends "SubscriptionKey" (client gets null Key); api/config/v1/amplitude — client also reads RudderStackKey/UseRudderStack/StatSigKey; api/config/v1/backtrace — client's 9 keys (ANRThresholdMs, CaptureNativeCrashes, FilterType, LogLineCount, MessageCount, MessageRegex, ReportBudget, SampleRate, VersionRegex) are mostly absent from the server response (only SampleRate/ReportBudget present); all fields silently default today.

Announcement Meta quirk: JDPPAFLFNBD's "Meta" is serialized on the wire as a JSON STRING (custom formatter MCAOGKPHCAD delegates to the String formatter), parsed client-side by MetaData.EHHMPPCEFKA; MetaData's own fields are name-preserved (Type, JsonData) so the inner JSON very likely uses those keys (inner-key casing not literal-verified — marked as inferred).

Trigger-verification caveat: gameplay-flow attributions are grounded in the client layer itself (method/error-string evidence such as "Failed to download ConfigSettings", "GameConfig.Refresh", "GetWhitelistedDisplayNameEmojis", ExternalShareImpl) rather than full Assembly-CSharp caller walks, except feed/{0}/{1} and feed/query where RRUI callers were located.

#### Endpoints

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `actionlink` | form fields (url-encoded body): data=String, validHours=Int32, maxCount=Int32 (omitted when null), codeType=Int32 (enum HHNMLNJHFNG), extraDataId=Int64 (omitted when null). Host =  | bare JSON string — the generated action-link code |
| GET | `actionlink/{code}` | none (code concatenated into path). Host = Link (11) | {CreatorPlayerId:Int32, Data:String, IsValid:Boolean} — tri-casing accepted |
| POST | `actionlink/{code}/consume` | path: actionlink/{WebUtility.UrlEncode(code)}/consume; form fields: id=String(code), validHours=Int32 (omitted when null), codeType=Int32, newPlayer=Boolean, newInstall=Boolean. Ho | {CreatorPlayerId:Int32, Data:String, IsValid:Boolean} |
| POST | `announcements/club/{clubId}` | form fields: title=String, body=String, imageName=String, meta=String (MetaData serialized to a JSON string). Host = Clubs (13). Client validates title/body length first (JBIDNCFHA | bare JSON integer (Int64) — the new AnnouncementId |
| GET | `announcements/club/{clubId} (GET)` | none. Host = Clubs (13) | {ClubId:Int64, LastReadAnnouncementId:Int64, Announcements:[{AnnouncementId:Int64, ClubId:Int64, CreatorAccountId:Int32, Title:String, Body:String, ImageName:String, Meta:String (raw JSON string; parsed client-side by JD |
| PUT | `announcements/club/{clubId}/{announcementId}` | form fields: announcementId=Int64, title=String, body=String, imageName=String. Host = Clubs (13) | none consumed; 2xx status only (LDGADANDBIO/void task) |
| DELETE | `announcements/club/{clubId}/{announcementId} (DELETE)` | none. Host = Clubs (13) | none consumed; 2xx status only |
| POST | `announcements/club/{clubId}/{announcementId}/read` | none (empty body). Host = Clubs (13) | none consumed; 2xx status only |
| GET | `announcements/mine` | none. Host = Clubs (13) | JSON array of announcement objects (same JDPPAFLFNBD shape as club board: AnnouncementId, ClubId, CreatorAccountId, Title, Body, ImageName, Meta, CreatedAt) |
| GET | `announcements/subscription/mine` | none. Host = Clubs (13) | JSON array of JDPPAFLFNBD announcement objects |
| GET | `announcements/v2/mine/unread (and announcements/v2/subscription/mine/unread, no query)` | none. Host = Clubs (13) | same wire shape as the sendAnnouncements variant: array of {ClubId, LastAnnouncementId, LastReadAnnouncementId, Announcements} — client only needs ClubIds here; Announcements may be empty arrays |
| GET | `announcements/v2/mine/unread?sendAnnouncements=true (and announcements/v2/subscription/mine/unread?sendAnnouncements=true)` | none beyond the literal query string. Host = Clubs (13) | JSON array of {ClubId:Int64, LastAnnouncementId:Int64, LastReadAnnouncementId:Int64, Announcements:[JDPPAFLFNBD objects]} — tri-casing accepted |
| GET | `api/announcement/v1/get` | none. Host = API (1) | JSON array of {AnnouncementId:Int64, AnnouncementType:Int32 (enum OHPHHEMBICB), Title:String, Body:String, ImageName:String, LinkType:Int32 (enum FNPKEEDHKFA), LinkName:String, LinkUri:String, LinkButtonLabel:String, Pla |
| GET | `api/config/v1/amplitude` | none | {AmplitudeKey:String, RudderStackKey:String, UseRudderStack:Boolean, StatSigKey:String} — tri-casing accepted |
| GET | `api/config/v1/azurespeech` | none | {Enabled:Boolean, Key:String, Region:String} — tri-casing accepted. NOTE: client key is "Key", NOT "SubscriptionKey" |
| GET | `api/config/v1/backtrace` | query params: platformType=String (from platform enum PAPLNIPKAMG via OLEJHIBGHEB), allocate=Boolean | {ANRThresholdMs, CaptureNativeCrashes, FilterType, LogLineCount, MessageCount, ReportBudget: Int32s; SampleRate:Single; MessageRegex:String, VersionRegex:String} — tri-casing accepted. Exact key↔field pairing among the s |
| GET | `api/config/v1/cohortnux/{cohortId}` | none (cohort id in path, Int32) | JSON array of {ButtonNumber:Int32, Version:Int32 (enum MDPAPEEJFED), Override:Int32 (enum IAEGEKLOMHF), CustomTitle:String, CustomDescription:String, CustomRoomName:String, DefaultTitle:String, DefaultDescription:String, |
| GET | `api/config/v1/freegiftbutton` | none | bare JSON boolean: true\|false |
| GET | `api/config/v2` | none | Object with exactly 8 recognized keys (each accepted in PascalCase/camelCase/lowercase): LevelProgressionMaps: [{Level:Int32, RequiredXp:Int32, GiftDropId:Int32?}], DailyObjectives: OIMDBINAEGE[][] i.e. array-of-arrays o |
| GET | `api/gameconfigs/v1/all` | none | JSON array of {Key:String, Value:String, StartTime:DateTime? (ISO8601), EndTime:DateTime?} — tri-casing accepted |
| GET | `api/testcasemanagement/v1/testcase/{id}` | none (string id concatenated into path) | {Id:String, Key:String, Title:String, Description:String, RoomName:String, Status:Int32 (enum LIHDCCJNKGC), AssignedPlayerNames:[String], Tags:[String], JiraUrl:String, Comments:[{Comment:String, CreatedAt:DateTime}]} —  |
| POST | `api/testcasemanagement/v1/testcase/{id}/claim` | none (empty body) | none consumed (BNDIAONDFFF.KDOPJCNKOOK = fire, ignore body); 2xx status only |
| POST | `api/testcasemanagement/v1/testcase/{id}/comment` | raw JSON body = JsonConvert.SerializeObject(comment) → a JSON string literal, e.g. "\"my comment\"" | none consumed; 2xx status only |
| POST | `api/testcasemanagement/v1/testcase/{id}/status` | raw JSON body = the enum's integer as text (Int32.ToString → BNDIAONDFFF.FJLLPHFOOJJ), e.g. body "2" | none consumed; 2xx status only |
| POST | `api/testcasemanagement/v1/testcase/{id}/unclaim` | none (empty body) | none consumed; 2xx status only |
| GET | `api/testcasemanagement/v1/testpass/{id}` | none (UInt32 id in path) | single test-pass object, same shape as testpasssummary entry |
| GET | `api/testcasemanagement/v1/testpasssummary` | none | JSON array of {Id:UInt32, Name:String, Description:String, StartDate:DateTime, EndDate:DateTime?, WasManuallyClosed:Boolean, TestCases:[testcase objects, see testcase/{id}], Tags:[String], NumTestCases:Int32, NumPassedTe |
| GET | `config/categories` | none. Host = GJDLNNLKDIJ.PlatformNotifications (15) — DorkNet serves it at platformnotifications host / config/categories | JSON array of {CategoryId:Int32 (enum CCOKJMJOIJF), Importance:Int32 (enum ODEOILNDKIO), Name:String, Description:String, IsMuteable:Boolean} — tri-casing accepted |
| PUT | `datalink` | form field: data=String. Host = Link (11) | bare JSON string — the generated datalink code |
| GET | `datalink/{code}` | none (code in path). Host = Link (11) | {Data:String} — tri-casing accepted |
| GET | `emojiConfig/whitelistedEmojis` | none. Host = GJDLNNLKDIJ.Accounts (10) | bare JSON array of strings (emoji), e.g. ["😀","😁",...] |
| POST | `feed/query` | JSON body: {Take:Int32, ContinuationToken:String?, InjectedFeedItems:[{FeedItemType:Int32 (enum DAAEHNDDCCH), FeedItemId:Int64}], FeedInstanceId:String?}. Host = GJDLNNLKDIJ.Discov | {Success:Boolean, Error:String?, Value:{Items:[{FeedItemType:Int32 (enum DAAEHNDDCCH), FeedItemId:Int64, FeedAlgorithmVersion:Int16}], FeedInstanceId:String, FeedContext:String, FeedAlgorithmVersion:Int16}} — tri-casing  |
| POST | `pageview/consume` | none (empty body). Host = GJDLNNLKDIJ.Link (11) | {Url:String, FreshnessSeconds:Double} — accepted as Url/url and FreshnessSeconds/freshnessSeconds/freshnessseconds. Empty Url = no deep-link |
| GET | `settings/partyinvite` | none. Host = GJDLNNLKDIJ.Chat (8) | {InviteLinkLifetimeInMinutes:Int32} — tri-casing accepted |

#### Not HTTP routes

These literals look like paths but are cache keys, deeplinks or MIME types:

- `actionlink/` — Prefix constant
- `api/config/` — Prefix constant only
- `api/testcasemanagement/` — Prefix constant only
- `datalink/` — Prefix constant
- `feed/{0}/{1}` — External share (copy link / OS share sheet) of a feed image

#### Defects

##### `POST announcements/club/{clubId}` — MISSING (breaks-gameplay)

Creating a club announcement POSTs to the COLLECTION URL announcements/club/{clubId} with form fields title/body/imageName/meta and expects a bare Int64 (the new AnnouncementId). DorkNet registers no POST at that template — and worse, the POST it does register at announcements/club/{clubId}/{announcementId} (ClubsController.cs:761) is bound to AnnouncementDelete. Create attempts 404 (route template requires two ids), so posting an announcement from the club UI always fails.

Handler: `none (nearest: DorkNet.Server\Controllers\Clubs\ClubsController.cs:761 registers POST only at .../{announcementId})`

**Fix.** Add [HttpPost("/announcements/club/{clubId:long}")] handler in ClubsController: authorize, check owner/moderator perms, read form fields title/body/imageName/meta (meta is a JSON string — store verbatim), insert ClubAnnouncementEntity, return Ok(newId) as a bare Int64. Remove the HttpPost binding from AnnouncementDelete.

##### `PUT announcements/club/{clubId}/{announcementId}` — SHAPE_MISMATCH (breaks-gameplay)

The PUT verb is registered — but it is bound to AnnouncementDelete: [HttpDelete]/[HttpPost]/[HttpPut] all decorate the same handler that calls clubs.DeleteAnnouncementAsync. The 2023 client PUTs form fields announcementId/title/body/imageName to EDIT an announcement (IKMMOCKDKAF.GHFAFMKHLCN); on DorkNet that request silently DELETES the announcement and returns 200, so the client believes the edit succeeded while the record is destroyed. Destructive data loss on a normal user action.

Handler: `DorkNet.Server\Controllers\Clubs\ClubsController.cs:762-768`

**Fix.** In ClubsController split the bindings: keep [HttpDelete] on AnnouncementDelete; add a dedicated [HttpPut("/announcements/club/{clubId:long}/{announcementId:long}")] edit handler that reads form fields announcementId/title/body/imageName, permission-checks, updates the ClubAnnouncementEntity, and returns 200 with empty body (client consumes nothing). Move the POST binding to the mark-read/create semantics it actually needs (client never POSTs this URL, so simply dropping it is also fine).

##### `GET api/config/v2` — SHAPE_MISMATCH (degraded)

Handler exists (GET api/config + api/config/v2) and returns RecRoomConfig, but the model (DorkNet.Models\Config\RecRoomConfig.cs) has no StorefrontConfig, RoomKeyConfig, or RoomCurrencyConfig properties, and LevelProgressionEntry has no GiftDropId. The 2023 reader (CDEDNEPPPND) registers exactly these keys; missing keys default, so the client's StorefrontConfig/RoomKeyConfig/RoomCurrencyConfig getters return null objects and every level's GiftDropId is null — currency-award cooldown, room-key cap, and room-currency gifting gate all run on nulls/zero (consumer NRE risk on the null nested objects is UNKNOWN but this exact pattern crashed boot for AutoMicMutingConfig). Present keys are fine: ShareBaseUrl, ServerMaintenance, AutoMicMutingConfig (8 floats), LevelProgressionMaps Level/RequiredXp, DailyObjectives (lowercase type/score accepted by tri-casing formatter DKMBKPLABAG). Extra 2020 keys (MessageOfTheDay, PhotonConfig, etc.) are ignored by the 2023 reader — harmless.

Handler: `DorkNet.Server\Controllers\API\Config\V2\ConfigController.cs:12`

**Fix.** In DorkNet.Models\Config\RecRoomConfig.cs add StorefrontConfig {AwardCurrencyCooldownSeconds:float}, RoomKeyConfig {MaxKeysPerRoom:int}, RoomCurrencyConfig {MinPlayerLevelForGifting:int} properties (JsonPropertyName PascalCase) with sane defaults, and add nullable int GiftDropId to LevelProgressionEntry.

##### `GET config/categories` — SHAPE_MISMATCH (degraded)

Server wraps the categories in a paged envelope {Results:[...],TotalResults:N}. The 2023 client deserializes a BARE array — method signature is FGLDKEJLAKB<List<PlatformNotificationCategoryConfigDTO>> (verified at LELAJKMOMIA.txt:1552); its generated List formatter expects a JSON array, gets an object, and the promise fails — the platform-notification category list in the settings UI never loads. Item keys themselves (CategoryId/Importance/Name/Description/IsMuteable) are correct per JNFDPPCIPHG.txt:371-446.

Handler: `DorkNet.Server\Controllers\PlatformNotifications\PlatformNotificationsController.cs:138-159`

**Fix.** In PlatformNotificationsController.Categories() return Ok(categories) (the bare array) instead of the {Results,TotalResults} wrapper on this march-2023 branch (both route aliases /config/categories and /platformnotifications/config/categories).

##### `GET settings/partyinvite` — SHAPE_MISMATCH (degraded)

Server returns a BARE integer (Content(value.ToString())) per a 2020-era comment claiming the contract is a bare Int32. The 2023 client deserializes an OBJECT: DICKGMODLGI with key InviteLinkLifetimeInMinutes (formatter AIEIPLDGFCF registers InviteLinkLifetimeInMinutes/inviteLinkLifetimeInMinutes/invitelinklifetimeinminutes — verified at AIEIPLDGFCF.txt:151-170). A JSON number where an object is expected fails the generated reader; DLDKCILCKNA's cached getter never resolves and party/chat invite-link creation from the watch runs with a default (0-minute) lifetime or fails outright.

Handler: `DorkNet.Server\Controllers\PlayerSettings\PlayerSettingsController.cs:52-59`

**Fix.** In PlayerSettingsController.GetPartyInvite return Ok(new { InviteLinkLifetimeInMinutes = <value, e.g. 60> }) on this branch. The stored bare-int setting row can stay; only the wire shape changes. (Client never POSTs this route, so SetPartyInvite is unused but harmless.)

##### `POST feed/query` — MISSING (degraded)

No handler anywhere in DorkNet.Server registers feed/query (verified server-routes.json + repo-wide grep; the only 'feed' routes are GET feed = api/photos/v1/feed alias in PhotosController.cs:96, api/site/v1/feed, discover/v2/feed — all unrelated). The 2023 home-screen personalized feed (KGLCPCEGCFF.PJGDPOLDBCJ, Discovery host) POSTs a JSON body {Take, ContinuationToken, InjectedFeedItems, FeedInstanceId} and expects {Success, Error, Value:{Items:[{FeedItemType,FeedItemId,FeedAlgorithmVersion}], FeedInstanceId, FeedContext, FeedAlgorithmVersion}}. A 404 rejects the promise and the home-tab feed surface shows empty/error content.

**Fix.** Add POST /feed/query (new controller or DiscoveryController): parse the request body, return {Success:true, Error:(string?)null, Value:{Items:[...], FeedInstanceId:<guid string>, FeedContext:"", FeedAlgorithmVersion:1}} with Items sourced from rooms/images the server wants to surface (FeedItemType per enum DAAEHNDDCCH, FeedItemId Int64); honor Take and return an empty Items list at end of pagination.

##### `GET announcements/club/{clubId} (GET)` — SHAPE_MISMATCH (degraded)

Server returns GroupByClub(rows) — a JSON ARRAY of per-club envelopes (List<Dictionary>, ClubsController.cs:937-953). The 2023 client deserializes a SINGLE object FIAKMDGGIHH {ClubId, LastReadAnnouncementId, Announcements:[...]} (sig FGLDKEJLAKB<FIAKMDGGIHH>, IKMMOCKDKAF.txt:1170). Array where object is expected fails the reader — the club announcement board never renders. Secondary defects in the envelope: LastReadAnnouncementId is serialized as explicit JSON null into a non-nullable client Int64 (throw-vs-default behavior of the generated reader on explicit null is UNKNOWN), and when the club has zero announcements GroupByClub returns [] instead of an object at all. Inner announcement objects (ToWireAnnouncement, :876-886) match JDPPAFLFNBD (AnnouncementId/ClubId/CreatorAccountId/Title/Body/ImageName/Meta-as-string/CreatedAt) — those are fine.

Handler: `DorkNet.Server\Controllers\Clubs\ClubsController.cs:737-742`

**Fix.** Make AnnouncementsForClub return one object: Ok(new { ClubId = clubId, LastReadAnnouncementId = <caller's last-read id or 0>, Announcements = rows.OrderByDescending(CreatedAt).Select(ToWireAnnouncement) }) — always an object, never an array, LastReadAnnouncementId always a number.

##### `GET announcements/mine` — FIXED

`ClubsController.AnnouncementsMine` → `ClubService.AnnouncementsForMemberClubsAsync`. Flat `List<JDPPAFLFNBD>` of `ToWireAnnouncement` rows, newest first — no per-club envelope and no unread filter (the issuing method is `FGLDKEJLAKB<List<JDPPAFLFNBD>> IOCFHCFLIMJ()` at IKMMOCKDKAF.txt:996, described in-binary as "get all announcements for clubs I'm in"). Membership rows carrying the pending (128) or ban (256) marker are excluded so a pending/banned account never sees a club's board, and disbanded clubs are re-filtered on State.

##### `GET announcements/subscription/mine` — FIXED

`ClubsController.AnnouncementsSubscriptionMine` → `ClubService.AnnouncementsForSubscribedClubsAsync`: same flat shape (`IIOMOLFOLPD` at IKMMOCKDKAF.txt:1083, route literal :1162), scoped to `ClubSubscriptions` rather than memberships.

##### `GET announcements/v2/mine/unread (+ announcements/v2/subscription/mine/unread, with and without ?sendAnnouncements=true)` — SHAPE_MISMATCH (degraded)

Routes and verb exist and Announcements payloads are always embedded (so ?sendAnnouncements=true is satisfied). Two row-shape defects vs client DTO NCHLBFPHFJE {ClubId, LastAnnouncementId, LastReadAnnouncementId, Announcements} (keys verified CAKCKAMAAPP.txt:331-406): (1) GroupByClub omits LastAnnouncementId entirely (defaults to 0 — tolerable for current client call sites, which only project ClubIds or read Announcements); (2) LastReadAnnouncementId is serialized as explicit JSON null while the client field is a non-nullable Int64 — whether the generated reader throws on explicit null (killing the whole unread poll) or defaults is UNKNOWN; do not ship a null.

Handler: `DorkNet.Server\Controllers\Clubs\ClubsController.cs:151-167 (rows built at 937-953)`

**Fix.** In ClubsController.GroupByClub emit LastAnnouncementId = ordered.First().Id and LastReadAnnouncementId = <caller's read-marker or 0L> (a number, never null). Key casing is already fine (tri-cased reader).

##### `GET actionlink/{code}` — FIXED

Was unrouted (PageviewController.cs:28-32 documented actionlink/* as out-of-scope). Now `DorkNet.Server\Controllers\Link\LinkController.cs` — `[HttpGet("/actionlink/{code}")]`, `[AllowAnonymous]` because `ActionCode.ModifyPreAuthLaunchTarget` resolves an inbound deep link during BootSequence, before the player holds a token. Verb from CKBKHENHCAN.txt:793 (`045 Move rdx, 0`), host 11 at :798. Response is the camelCase anonymous object `{creatorPlayerId, data, isValid}`, which the tri-cased generated formatter DPIECEMDKPL accepts (:267/:278/:286 CreatorPlayerId, :294/:302 Data, :310/:318/:326 IsValid). Unknown/expired codes answer 200 with `IsValid:false` (never 404) so the promise resolves. Reading a code does not spend a use.

##### `POST actionlink` — FIXED

Now `LinkController.ActionLinkCreate` — `[HttpPost("/actionlink")]`, `[Authorize]`. Verb from CKBKHENHCAN.txt:1226 (`106 Move rdx, 2`), host `105 Move r8, 11` at :1225. Form fields bound through `FormOrJsonModelBinder`: data (:1234), validHours (:1249), maxCount (:1261), codeType (:1273), extraDataId (:1287). Return type is `FGLDKEJLAKB<System.String>` (:860) so the body is a bare JSON string — emitted with `Content(JsonSerializer.Serialize(code), "application/json")`, because `Ok(string)` goes out as unquoted text/plain via StringOutputFormatter.

Codes are 8 characters from the unambiguous alphabet `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` (no O/0/I/1 — players re-type these into ActionCodeConsumptionModel), reserved with a global uniqueness check before insert.

##### `POST actionlink/{code}/consume` — FIXED

Now `LinkController.ActionLinkConsume` — `[HttpPost("/actionlink/{code}/consume")]`, `[AllowAnonymous]` (can fire from the pre-auth launch-target path). Verb from CKBKHENHCAN.txt:1675 (`077 Move rdx, 2`); path built by `String.Concat("actionlink/", WebUtility.UrlEncode(code), "/consume")` at :1665-1669. Form fields: id (:1684), validHours (:1692), codeType (:1720), newPlayer (:1732), newInstall (:1745).

Semantics established from the call sites: `validHours` is the creating ActionCode's `Configuration.AutoRenewHours`, loaded immediately before dispatch (`043 Call Configuration.get_AutoRenewHours`, RoomCode_NestedType_IBLIEDEHMPI.txt:168; ClubCode_NestedType_EAGCMJOEANN.txt:78) and null when AutoRenew is off — so a present value renews the code's expiry instead of letting it age out. `codeType` is verified against the stored type (CBKKGBIJHAL: Unknown=-1, Friend=0, Referral=1, Meetup=2, Club=3, PlayerEvent=4, Room=5, Influencer=6, Photo=7; RoomCode passes 5 at IBLIEDEHMPI.txt:175, ClubCode passes 3 at EAGCMJOEANN.txt:85) so a room payload cannot be handed to the club handler. Expiry and maxCount are enforced; a successful redeem increments the use counter.

Referral codes are the only type with no other server touchpoint — ReferralCode has no HTTP routes of its own and `IncentivizedReferralsController` counts `referral:credited:{inviterId}:{inviteeId}` rows that nothing wrote. Consuming a Referral code as a signed-in `newPlayer` now writes that row (on the invitee, matching the `externalinvite:redeemed:*` convention), so incentivized-referral progress is no longer permanently zero.

##### `GET datalink/{code}` — FIXED

Now `DorkNet.Server\Controllers\Link\LinkController.cs` — `[HttpGet("/datalink/{code}")]`, `[AllowAnonymous]` (resolved from a cold-boot deep link). Verb from CKBKHENHCAN.txt:352 (`045 Move rdx, 0`), literal `"datalink/"` at :345. Response is `{data}`; the generated formatter IKBPJACLIHH registers a single member Data (:139) plus the camelCase alias (:150). An unknown code returns an empty Data rather than 404 so the strict reader still gets an object.

##### `PUT datalink` — FIXED

Now `LinkController.DataLinkCreate` — `[HttpPut("/datalink")]`, `[Authorize]`. Verb from CKBKHENHCAN.txt:560 (`033 Move rdx, 3` = PUT), literal `"datalink"` at :558, sole form field `data` at :569, bound through `FormOrJsonModelBinder`. Return type `FGLDKEJLAKB<System.String>` (:419) — bare JSON string, emitted with `Content(JsonSerializer.Serialize(code), "application/json")`. Codes here are 10 characters (nobody types a datalink code; it only travels inside a URL).

##### Link-host storage note

All five link routes persist in `PlayerSettings` — the server's existing general-purpose per-player key/value table already used for ban-appeal codes and external friend invites — under `actionlink:{CODE}` / `datalink:{CODE}` keys owned by the creator's row, so codes survive restarts without a new table. Action-link rows pack `{d:data, e:expiry, m:maxCount, u:uses, t:codeType, x:extraDataId}` into the single Value column with one-character keys; because `PlayerSettingEntity.Value` is `[MaxLength(1024)]` (a real `varchar(1024)` on Postgres), both create routes reject over-long payloads with a logged 400 instead of blowing up at SaveChanges.

##### `POST api/testcasemanagement/v1/testcase/{id}/status` — SHAPE_MISMATCH (degraded)

Route+verb exist, but the server binds [FromBody] StatusUpdateRequest {NewStatus:int} while the client sends a RAW JSON body that is just the enum integer as text (Int32.ToString → FJLLPHFOOJJ, e.g. body '2'). System.Text.Json cannot bind a bare number to an object, so model binding fails and ASP.NET returns 400 — setting pass/fail status from the QA tooling never persists. (Dev-only surface, hence degraded not breaks-gameplay.)

Handler: `DorkNet.Server\Controllers\API\TestCaseManagement\TestCaseManagementController.cs:131-142`

**Fix.** Change UpdateStatus to accept the bare int: either [FromBody] int newStatus, or read Request.Body as text and int.Parse it (the latter also tolerates a missing JSON content-type). Keep the 0-3 range check.

##### `POST api/testcasemanagement/v1/testcase/{id}/comment` — MISSING (degraded)

No /comment route — adding a comment from the QA tooling 404s. Related shape gap on GET testcase/{id}: the client DTO JOKHLOPJBLN also reads Comments:[{Comment:String, CreatedAt:DateTime}], and ToWireCase (TestCaseManagementController.cs:160-174) never emits a Comments key, so even stored comments would never render.

Handler: `none (controller: TestCaseManagementController.cs registers claim/unclaim/status only)`

**Fix.** In TestCaseManagementController add [HttpPost("api/testcasemanagement/v1/testcase/{id}/comment")] reading the raw body as a JSON string literal (client sends JsonConvert.SerializeObject(comment), e.g. "\"text\"") and persisting {Comment, CreatedAt}; add Comments = [...] to ToWireCase.

##### `GET api/config/v1/azurespeech` — SHAPE_MISMATCH (cosmetic)

Server sends SubscriptionKey (both casings) but the 2023 DTO LAKPIECKPOO reads the key literal "Key" (verified COIELAHKALG.txt:262-270 — Key/key registered, no SubscriptionKey). Client's Key stays null. Harmless today because the server also sends Enabled:false, which the client does read, so speech-to-text stays off.

Handler: `DorkNet.Server\Controllers\API\Config\V1\ConfigController.cs:80-102`

**Fix.** Add ["Key"] = string.Empty (alongside the existing keys) to the azurespeech dictionary in ConfigController.GetAzureSpeech; populate it with a real key if Azure speech is ever enabled.

##### `GET api/config/v1/amplitude` — SHAPE_MISMATCH (cosmetic)

Server sends only {AmplitudeKey:""}. The 2023 DTO FGJMMNJGDKB also reads RudderStackKey, UseRudderStack, StatSigKey (verified NNAGOPNGJFE.txt:343-418); all three default (null/false). Since AmplitudeKey is empty anyway, analytics is inert — no user-visible impact on a private server.

Handler: `DorkNet.Server\Controllers\API\Config\V1\ConfigController.cs:21-22`

**Fix.** Extend GetAmplitude to Ok(new { AmplitudeKey = "", RudderStackKey = "", UseRudderStack = false, StatSigKey = "" }) for exact-shape completeness.

##### `GET api/config/v1/backtrace` — SHAPE_MISMATCH (cosmetic)

Route, verb, and both query params (platformType, allocate — [FromQuery]) match. Of the 2023 DTO FDEGPICBIHO's key set {ANRThresholdMs, CaptureNativeCrashes, FilterType, LogLineCount, MessageCount, MessageRegex, ReportBudget, SampleRate, VersionRegex} the server sends only SampleRate and ReportBudget (both 0); the other seven default. With SampleRate=0 crash reporting is disabled by design, so defaults are benign. The extra 2020-era keys the server sends are ignored.

Handler: `DorkNet.Server\Controllers\API\Config\V1\ConfigController.cs:31-78`

**Fix.** Add the seven missing keys with disable-safe values (ANRThresholdMs:0, CaptureNativeCrashes:false, FilterType:0, LogLineCount:0, MessageCount:0, MessageRegex:"", VersionRegex:"") to the GetBacktrace response dictionary.

##### `GET api/config/v1/cohortnux/{cohortId}` — SHAPE_MISMATCH (cosmetic)

Handler returns four hardcoded (but functional) button configs with ButtonNumber/Version/Override/CustomTitle/CustomRoomName/DefaultTitle/DefaultRoomName. The 2023 DTO KNLHJKBMDIM additionally reads CustomDescription and DefaultDescription, which the server omits (default null) — NUX buttons render without description text. Types and the enum ints match; anonymous objects serialize PascalCase (global PropertyNamingPolicy=null, ServiceCollectionExtensions.cs:380-381) and the reader is tri-cased anyway.

Handler: `DorkNet.Server\Controllers\API\Config\V1\ConfigController.cs:168-195`

**Fix.** Add CustomDescription = "" and DefaultDescription = "<short blurb>" to each of the four entries in GetCohortNux.

##### `GET api/announcement/v1/get` — SHAPE_MISMATCH (cosmetic)

Handler exists and sends AnnouncementId/AnnouncementType/Title/Body/ImageName/LinkType/LinkName/LinkUri/Platform/CreatedAt (real data from CommunityBoardService). The 2023 DTO MMEIGLPHBMD additionally reads LinkButtonLabel (verified AKDFPANIAFL.txt:910) — absent, defaults null, so link buttons on announcement banners show no custom label. All other keys/types match; 2023 reader tolerates missing keys (no 2020-style required-key throw).

Handler: `DorkNet.Server\Controllers\API\Announcements\AnnouncementsController.cs:21-44`

**Fix.** Add LinkButtonLabel = "" (or an admin-configurable value) to the anonymous object in AnnouncementsController.Current.

##### `GET api/testcasemanagement/v1/testcase/{id}` — SHAPE_MISMATCH (cosmetic)

All keys present except Comments (client DTO JOKHLOPJBLN reads Comments:[{Comment,CreatedAt}]); defaults to null client-side — comments panel empty (pairs with the missing POST /comment route reported above). Extra keys (MinNumAssignedPlayers, AssignedPlayerIds, JiraBugUrl) are ignored.

Handler: `DorkNet.Server\Controllers\API\TestCaseManagement\TestCaseManagementController.cs:71-77, 160-174`

**Fix.** Add Comments = <stored comments>.Select(c => new { c.Comment, c.CreatedAt }) to ToWireCase once comment storage exists (see /comment finding).

## Residual literals not matched to a server route

Path-shaped literals in the client that no DorkNet route matches. Most are not
HTTP routes at all — the per-subsystem tables above classify each one.

- `accounts/{0}/receives/{1}`
- `actionlink/`
- `announcements/mine`
- `announcements/subscription/mine`
- `club_events/{0}`
- `comments/create/{0}`
- `comments/delete/{0}`
- `comments/get/{0}`
- `comments/read/{0}/{1}`
- `datalink/`
- `event/{0}/instances`
- `event/{0}/{1}`
- `event_responses/{0}`
- `events_by_club_ids/`
- `friend/`
- `hot_rooms/{0}&skip={1}&take={2}`
- `image/{0}`
- `inventionsbycreators/{0}/{1}/{2}`
- `leaderboard/CheckAndSetStat`
- `magic_door/{0}`
- `meetup/`
- `my_visited_rooms/skip={0}&take={1}`
- `ownedby/{0}`
- `platformid/{0}`
- `playlistinteraction/{0}`
- `remote-run/push-to-studio`
- `remote_player_event_list/{0}`
- `role/{0}/{1}`
- `room_events/{0}`
- `roominteraction/{0}`
- `roomsbycreators/{0}&skip={1}&take={2}`
- `search/`
- `search_live/`
- `search_rooms/{0}&skip={1}&take={2}`
- `showcase/{0}`
- `store/invention/{0}`
- `store/item/{0}`
- `subscription/{0}`
- `unity_assets/{0}/{1}/{2}`
- `video/`
- `visited_rooms/account_id={0}&skip={1}&take={2}`

## Keeping this accurate

`DorkNet.Server.Tests/EndpointContractDiscovery.cs` enumerates the server route
table by reflection and is the ground truth for what the server serves — prefer
it over grepping for route attributes, which misses `[Route]`-only actions such
as the CDN `unity_assets` handler and mis-handles primary-constructor
controllers.

When adding an endpoint, verify all three of: the route, the verb ordinal, and
whether the response is a bare scalar, an array, or a paged
`{Results, TotalResults}` container. Those three account for nearly every defect
catalogued above.
