# Rec Room 2023.03.21 — shop wire contracts (watch Shop tab + room consumables)

How the March-2023 client's in-room shop works
(`RecRoom.Systems.RoomConsumablesManager`) and the exact wire shapes
DorkNet serves for it. Everything here was verified against the
2023.03.21 ISIL dump (`RecNet.Runtime` Utf8Json formatters) and the
Cpp2IL dummy DLLs — key names, field types, and enum values are read
from the binary, not guessed.

Server implementation: `DorkNet.Server/Controllers/API/RoomConsumables/RoomConsumablesController.cs`
backed by the `RoomConsumables` + `RoomConsumableOwnership` tables.
Integration coverage: `DorkNet.Server.Tests/RoomConsumablesShopTests.cs`.

## Why this exists (the crash)

On every room join the client fetches
`GET api/roomconsumables/v1/roomConsumable/room/{roomId}/me` and feeds
the rows to `RoomConsumablesManager`. The row-processing method
(`ODAIDLGODIK.FBDIGFOANBG`) dereferences the row's `Consumable` desc
**unconditionally**. If the server's JSON keys don't match, Utf8Json
leaves the field null and the client throws
`NullReferenceException` → unobserved task exception → "Uploading Crash
Report" in `Player.log`, and the shop is dead for the session. The old
DorkNet endpoint sent `Id`/`ConsumableItemDesc`/`CreatedAt` (keys
borrowed from the *gift* DTO, which really does use
`ConsumableItemDesc`) — the 2023 room-consumable DTO does not.

## Endpoints (client literals)

Base `{0}` = `api/roomconsumables`. Route literals are case-mixed on
the wire (`roomConsumable` vs `roomconsumable`); ASP.NET matches both.

| Verb | Path | Purpose |
|---|---|---|
| GET | `{0}/v1/roomConsumable/room/{roomId}` | room catalog (shop shelf) |
| GET | `{0}/v1/roomConsumable/room/{roomId}/me` | caller's inventory, fetched on room join |
| GET | `{0}/v1/roomConsumable/{id}` | single desc |
| GET | `{0}/v1/roomConsumable/{id}/isOwned` | "do non-creators hold stock" (bare bool) |
| POST | `{0}/v1/roomConsumable` | create (no id in body) / update (id in body); body = desc DTO |
| DELETE | `{0}/v1/roomConsumable/{id}` | soft delete; response = bare status int |
| POST | `{0}/v1/roomconsumable/{id}/purchase/tokens` | buy one with tokens |
| POST | `{0}/v1/roomconsumable/{id}/purchase/currency` | buy one with a room currency |
| POST | `{0}/v1/roomConsumable/{id}/consume` | spend one unit |

## DTOs

Consumable desc (`MOONCMIECPL`, formatter `HJPEABLEEKL`):

```json
{
  "RoomConsumableId": "guid",
  "RoomId": 116,
  "Name": "Cola",
  "Description": "…",
  "ImageName": "…",
  "PriceAndCurrency": { "Price": 10, "CurrencyId": null }
}
```

`CurrencyId` null = token-priced; otherwise a room-currency PublicId.
The desc also has a client-side `CreatedAt` field but it is **not** a
wire key.

Inventory row (`HOMJKAOHGDG`, formatter `CICEHLNDLDE`):

```json
{
  "RoomConsumableId": "guid",
  "AccountId": 4,
  "Count": 1,
  "ConcurrencyCode": "guid",
  "ModifiedAt": "2026-07-07T12:00:00Z",
  "Consumable": { …desc… }
}
```

`AccountId` is int32 on the wire. `Consumable` must never be null (see
crash above).

## Concurrency model

Each (player, consumable) stack carries an opaque `ConcurrencyCode`
Guid. Purchase and consume requests send
`{ CurrentConcurrencyCode, NewConcurrencyCode }` — the **client
generates the new code** and adopts it locally on success (the purchase
response carries no inventory row), so the server must store
`NewConcurrencyCode` verbatim or the next consume fails with
`ConcurrencyCodeMismatch` (32). On a mismatch the server returns the
current row so the client can resync.

Purchase body (`ILNJDMFNOCD`, formatter `JEBCHNDADKK`):

```json
{
  "ConcurrencyCodes": { "CurrentConcurrencyCode": null, "NewConcurrencyCode": "guid" },
  "ExpectedPriceAndCurrency": { "Price": 10, "CurrencyId": null }
}
```

Consume body is the bare code pair (`CFOBCEPBBHA`, formatter
`JKJHOAKNOCF`): `{ "CurrentConcurrencyCode": "guid", "NewConcurrencyCode": "guid" }`.

## Responses

Create/update (`GFKDAADENEE`): `{ "Status": 0, "Consumable": {…desc…} }`.
Delete: bare status int (`0`).
Consume (`BGHBAILNNJJ`, formatter `LPFAFIGAIOE`):
`{ "Status": 0, "InventoryItem": {…inventory row…} }`.

Token purchase (`MJKFEHPIDME`, formatter `HJOKFJKKEJM`):

```json
{
  "OperationResult": 0,
  "BalanceUpdateResult": 0,
  "TokenBalanceResponse": { "CurrencyType": 2, "Balance": 4990, "Platform": 0 }
}
```

Room-currency purchase (`DEILBLCDNEA`, formatter `IDBFKCFEAHM`) swaps
the last key for
`"CurrencyBalanceResponse": { "AccountId", "CurrencyId", "Balance", "ModifiedAt" }`.

## Enums

`RoomConsumableStatus` (`DLDNGEDFMOJ`) — used by create/update/delete/consume:
`0 Success, 4 RoomConsumableNotFound, 7 PlayerDoesntHavePermission,
13 MaxConsumablesInRoom, 15 RoomIdMissing, 17 PriceOrCurrencyMissing,
18 CurrencyNotFound, 21/22 PriceTooLow/HighTokens, 23/24 NameTooShort/Long,
27 DescriptionTooLong, 32 ConcurrencyCodeMismatch,
33 PlayerDoesNotOwnConsumable, 36 RoomNotFound` (full 0–42 list in the
dummy DLL).

Token-purchase `OperationResult` (`CABBDKFODEC`):
`0 OK, 1 TooManyRequests, 2 NotEnoughCredit, 3 AlreadyOwned,
4 NoItemAvailable, 5 CouponNotApplicable, 6 RequestedPriceDoesNotMatch,
7 RequestedAmountNotAllowed, 8 PlayerNotEligible,
9 RequestCannotBeRefunded, 10 PlayerNotApproved`.

Currency-purchase result (`GACBLALELBP`): `0 Success, 1 NotEnoughCredit`.

## Watch Shop tab (storefronts)

The 2023 Shop tab is fed by two calls, both with shapes that changed
since 2020:

1. `GET api/storefronts/v3/giftdropstore/{id}` → `HOEGLKNEIOF`
   (formatter GNMDEJJAHCJ): `{ StorefrontType, StoreItems, NewUntil,
   NextUpdate, SubscriberDiscountPercent }`. Each StoreItems row
   (JCCCKDCHMLG, formatter POGBAGAHGIA) is
   `{ PurchasableItemId, Type (0=GiftDrop enum MDCCOLGHBMN), Prices:
   [{CurrencyType, Price, StorefrontSaleData}], SubscriberPrices,
   IsFeatured, AvailableAt, AvailableUntil, NewUntil, GiftDrop: {…} }`.
   **`GiftDrop` is SINGULAR** — the 2020 `GiftDrops` array key is
   ignored by the 2023 formatter, and a missing/null `GiftDrop` makes
   `RRUI.Data.StoreItemListModel`'s per-item filter predicate NRE on
   every row (bools/Rarity/type reads on the null desc), so the Shop
   tab renders empty. DorkNet ships both keys.

   The inner `GiftDrop` (EFFIEFEFHHB, formatter EMBCEDNHFLB) keys:
   `GiftDropId (int), FriendlyName, Tooltip, TagList (CSV string),
   ConsumableItemDesc (string), AvatarItemDesc (string descriptor),
   AvatarItemType (int?), EquipmentPrefabName, EquipmentModificationGuid,
   IsQuery, Unique, SubscribersOnly, Rarity (0/10/20/30/50 enum
   BGDEDNBFOCH), AvatarItemId, CurrencyType, Currency (enum EAFDEJBEFJB,
   RecCenterTokens=2), Context (GiftContext enum IFKEEPDDNBC),
   ItemSetId (int?), ItemSetFriendlyName`.

2. `GET api/storefronts/v1/toptoday` → a **bare `List<int>` of
   PurchasableItemIds** (client DCFKEFHJAGC.IDCIMNLBINC), resolved
   against the gift-drop cache with "Could not find purchasable
   giftdrop with purchasableItemId {0}" warnings for unknown ids.
   Serving item objects here (the old `/all` alias) breaks the featured
   fetch entirely.

Coverage: `DorkNet.Server.Tests/WatchShop2023Tests.cs`.

## Related: unprefixed 2023 commerce probes

The 2023 client also calls these commerce paths **without** the `api/`
prefix (same host as `api/catalog/...`); missing routes surface as
"CleanupPendingTransactions failed" + unobserved `HTTP Error 404`
crash reports at startup:

- `POST purchase/v1/cleanuppending` — ack (no pending IAP on DorkNet)
- `GET/POST purchase/v1/hasspentmoney` — bare `false`
- `GET reminder/currentTokenBundles/v2` — `[]` (no real-money offers)
- `POST purchase/v1/{initiatepurchase,processpurchase,completepurchase,cancelpurchase}`
  — aliased onto the existing `api/purchase/v1` handlers

All of these plus `/purchasecampaign` are owned by the Commerce service
in `DorkNetRouteOwnership`.
