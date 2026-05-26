# Rec Room 2020 Client OpenAPI Context

This companion explains `docs/recroom-2020-client-openapi.json`, an inferred OpenAPI 3.1 description of the request surface observed in the March and December 2020 Rec Room client decompiles.

## Files

- `docs/recroom-2020-client-openapi.json`: OpenAPI 3.1 artifact.
- `docs/recroom-2020-client-request-catalog.csv`: December ISIL call-site evidence.
- `docs/recroom-2020-03-request-literals.csv`: March string-literal evidence.
- `docs/recroom-2020-client-request-expectations.md`: human-readable request/result table.
- `docs/recroom-2020-client-response-contracts.md`: endpoint-by-endpoint
  expected response body notes, including DTO members and recovered JSON parser
  keys.
- `docs/recroom-2020-client-response-contracts.json`: machine-readable version
  of the response contracts.

## How To Read It

- `x-recroom-client-route-literal` is the exact client string or fragment before OpenAPI normalization.
- `x-recroom-evidence` points back to decompiled type, method signature, file, and line.
- `x-recroom-builds` says whether the route was seen in December 2020, March 2020, or both.
- `x-recroom-inferred-http-method` is best effort. The dump often hides the high-level request builder body, so mutations are modeled as `post`/`delete` by route semantics.
- Response schemas are permissive and family-specific, but operations now carry
  `x-client-response-contracts` from the response-contract pass. For exact body
  shape, prefer the Markdown/JSON response contract files and the
  `Client parser JSON keys` entries recovered from `PPGFHEDFBEA`.

## Coverage Summary

- December raw call-site rows: 367
- March request-like literals: 87
- OpenAPI paths: 231
- OpenAPI operations: 245

| Family | Count |
| --- | ---: |
| account | 13 |
| activities | 4 |
| announcements | 1 |
| avatar | 3 |
| bootstrap | 2 |
| bug-reporting | 1 |
| clubs | 37 |
| community-board | 1 |
| config-settings | 5 |
| economy | 6 |
| elo | 1 |
| equipment | 1 |
| groups | 1 |
| images | 1 |
| inventions | 5 |
| matchmaking | 10 |
| messages | 7 |
| misc | 25 |
| objectives | 1 |
| player-events | 10 |
| players | 6 |
| playlists | 21 |
| quickplay | 1 |
| relationships | 1 |
| reporting | 5 |
| room-keys | 4 |
| rooms | 63 |
| sanitize | 2 |
| storage-cdn | 3 |
| store | 2 |
| test-case-management | 1 |
| versioncheck | 1 |

## Service Mapping

- `account`: account profile APIs, usually `api.rec.net` or account service; password routes may belong to auth/account host.
- `matchmaking`: `match.rec.net` style `/goto/*` and room instance discovery.
- `rooms` and `playlists`: room/playlist JSON APIs. Room blob bytes are not JSON and must remain CDN/storage binary responses.
- `inventions`: invention metadata/save/version/publish/download APIs; March and December differ on save/publish versions.
- `clubs`: club and member APIs; may use the name-server `Clubs` service base.
- `images`, `storage-cdn`: image metadata/upload plus later binary fetches.
- `messages`, `player-events`, `avatar`, `economy`, `reporting`, `config-settings`: primary API host unless the name server routes them elsewhere.

## Important Limitations

This is a compatibility map, not a guarantee that every method/schema is exact. It is intentionally broad so DorkNet can see every route family the client may touch. Before implementing a missing endpoint, inspect the evidence method and nearby DTO declarations, then verify with client logs.

The highest-risk exact-shape areas are matchmaking/goto, room data descriptors, CDN/storage blobs, auth/account login, avatar gifts/items, and invention save/download. Those should be validated against runtime client behavior because wrong primitive-vs-object-vs-array shapes can crash or stall the 2020 client.
