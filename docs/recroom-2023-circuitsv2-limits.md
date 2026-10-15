# 2023 client CircuitsV2 limits (`UGC.CircuitsV2.Limit.*`)

How the March-2023 client budgets CircuitsV2, and how DorkNet lifts the
network budget so circuits stop dropping sends. Reverse-engineered from
`RecRoom.CircuitsV2.Config.LimitConfig` in the 2023-03-21 ISIL dump
(`IsilDump/Assembly-CSharp/RecRoom/CircuitsV2/Config/LimitConfig.txt`)
and the limiter chain under `IsilDump/Circuits.V2/`.

## Why it matters

Retail budgets a circuit **5 RPCs per frame** and **1024 bytes per 60
frames**. A circuit that exceeds either budget does not queue or slow
down — the send is **dropped on the floor** and an execution error of
kind 2 (bytes) or 3 (RPCs) is raised, and the room save gets flagged
`cv2_rpc_limited` / `cv2_net_limited`. This is what makes busy CV2 rooms
feel like they're desyncing at random.

DorkNet serves both budgets at 1,000,000,000, which is more than a
circuit can spend in a frame. It also raises the room-size limits
(compute, chips, cloud variables and culling) to about 10x retail.
[The scalar limits](#the-scalar-limits) covers what each one does and
why its value stops where it does.

## The keys

The client reads these through its gameconfig provider, i.e. the array
served by `GET api/gameconfigs/v1/all`. Any key the server omits — or
sends unparseably — silently falls back to a value baked into the
client, with no error logged.

| Key | Type | Client default | DorkNet default |
| --- | --- | --- | --- |
| `UGC.CircuitsV2.Limit.Network` | JSON array | one entry, below | budgets lifted |
| `UGC.CircuitsV2.Limit.Compute` | int | 50000 | 500000 |
| `UGC.CircuitsV2.Limit.Chips` | int | 3000 | 30000 |
| `UGC.CircuitsV2.Limit.CloudValues` | int | 100 | 1000 |
| `UGC.CircuitsV2.Limit.CloudValuesAutosaveSeconds` | float | 300 | 300 |
| `UGC.CircuitsV2.Limit.UnculledChips` | int | 500 | 3000 |
| `UGC.CircuitsV2.Limit.VisibleChips` | int | 500 | 3000 |
| `UGC.CircuitsV2.Limit.InkPerChip` | int | 15 | 0 |

The client defaults are the values `LimitConfig..ctor` bakes in (the
`mov dword ptr [rbx+28h..40h]` run in `LimitConfig.txt`). For comparison,
the 2023-06-21 build raised its `COMPUTE_DEFAULT` to 100000 and left the
others unchanged.

Scalars are parsed with `int.TryParse` / `float.TryParse` under
`NumberStyles.Integer` / `NumberStyles.Float` and
`CultureInfo.InvariantCulture` — so no thousands separators, and a dot
decimal separator only.

## The network array

`UGC.CircuitsV2.Limit.Network` is a **top-level JSON array**, parsed by
Newtonsoft (`JsonConvert.DeserializeObject`), not Unity's `JsonUtility`.
Newtonsoft binds public fields and falls back to case-insensitive
matching, but DorkNet emits the client's own field names verbatim:

```json
[{"NumPlayersForActivation":0,"Bytes":1000000000,"BytesTimeWindowFrame60s":60,"Rpcs":1000000000,"RpcTimeWindowFrame60s":1}]
```

The retail value is the same shape with `Bytes` 1024, `Rpcs` 5.

**Entry selection.** The client sorts entries ascending by
`NumPlayersForActivation` and activates the highest one whose threshold
is at or below the room's **max player capacity** (not the live player
count). One entry at threshold 0 therefore applies everywhere.

**Validation.** The client rejects the whole array — falling back to the
retail budget — if it is not valid JSON, is empty, or has no entry with
`NumPlayersForActivation` of 0. `ServerSettingsService` normalizes
around all three: it sorts, de-duplicates thresholds, and rewrites the
lowest entry's threshold to 0.

## The scalar limits

Every getter is consumed in exactly the places listed here. A sweep of
the whole ISIL dump for `LimitConfig.get_` finds only these files.

### Compute: per-frame CPU budget

`CircuitsV2Manager`'s per-frame tick (`OCGDEIJJBAG(float)`, ISIL 240 at
`CircuitsV2Manager.txt:21241`) builds a fresh compute context every
frame through `DIILFAPPLOI` (ISIL 018 at `:12637`), which wraps the value
in `Circuits.V2/BENFNKBKHEA`. That object keeps four int counters. Each
charge adds to one of them and checks the sum against the budget with
`<=`. The default costs come from the `MPKGOFPJCOP` interface: 100, 100,
50 or 500 depending on the kind of operation. When the budget runs out,
circuit work stops for the rest of the frame and the save is flagged
`cv2_cpu_limited`.

Nothing is allocated from this value. **It is the only guard against
runaway loops**, though. Raising it lets a broken loop burn more frame
time before it is stopped. A budget near `int.MaxValue` would let the
summed counters wrap negative so the check never fires. DorkNet serves
500,000, which is 10x retail and 5x the June 2023 build. That allows
1,000 to 10,000 operations per frame, depending on their kind.

### Chips: room chip cap

`CreationManager.OnCircuitsV2LimitsRefreshed` (`CreationManager.txt:3179`)
rebuilds the room's max sandbox resources as
`CostInfo(ink 30000, lights 64, ai 101, numChips = Chips, numCloudVariables = CloudValues)`
(ISIL 050-068). Adding a chip checks `CanAffordResourceCost(OneChip)`
against it. A failed check shows the "Too many chips." toast
(`CircuitsV2Manager_NestedType_AMEJFMPHNEO.txt` ISIL 164-187, 592).

Hazard: the chip-culling manager's constructor preallocates **four**
`List<Chip>(Chips)` (`PAFDMBHNMBP.txt` `.ctor` ISIL 053-108). That costs
32 bytes per unit of the limit whether or not the room uses it, and a
value of 0 or less throws. The clamp is 1..1,000,000, which is 32MB at
the top. DorkNet serves 30,000, which preallocates under 1MB.

### CloudValues: room cloud-variable cap

This is the `numCloudVariables` field of the same max-resources
`CostInfo`. `CreationManager.CalculateActiveResourceCost` also reads it
(ISIL 389-415 at `CreationManager.txt:6136-6162`):

```
inkCost += InkPerChip * Math.Min(numChips, CloudValues)
```

That is an unchecked int multiply. It also means that raising
CloudValues while InkPerChip stays at retail's 15 makes chips use
**more** room ink. Serving InkPerChip 0 removes the term. The clamp was
lowered from 100,000 to 0..10,000 so the product stays at or below 3e8
even with InkPerChip at its own ceiling. DorkNet serves 1,000.

### InkPerChip: ink cost per chip (lower is more)

This is a **cost**, not an allowance. It feeds the
`CalculateActiveResourceCost` term above. The add-chip path also charges
it directly through `CanAffordResourceCost` ("Not enough ink.",
`AMEJFMPHNEO` ISIL 247-265). That direct charge only runs when
`ShowGadgetsToEveryone` is set and `Chips < VisibleChips` (ISIL 221-234),
so with the defaults below it never runs.

The room's ink cap is a **hard-coded 30000** (`0x7530`,
`CreationManager.txt:3374`), not a gameconfig key. Lowering this cost is
therefore the only way to leave circuits more room under that cap. No
client code divides by it. DorkNet serves **0**, so chips use no room
ink. The clamp is 0..30,000, because past 30000 no single chip can be
afforded.

### UnculledChips and VisibleChips: chip culling

The chip-culling manager reads both together (`PAFDMBHNMBP.DJLEFJCOLCF`,
ISIL 079-087 at `PAFDMBHNMBP.txt:2520-2525`). It subtracts
`UnculledChips` from the live chip count and culls that many chips.
`VisibleChips` is used in the same arithmetic further down. Both values
only appear in counts and subtractions: they allocate nothing and are
never divisors. Raising them costs rendering time, not stability.
DorkNet serves 3,000 for both, so every chip in a retail-sized room stays
drawn. Both stay at or below Chips, which keeps the `Chips < VisibleChips`
ink charge above switched off.

### CloudValuesAutosaveSeconds: save cadence

A `CircuitsV2Manager` coroutine reads this value
(`CircuitsV2Manager_NestedType_CDPKCHNIHFG.txt` ISIL 059 at `:193`),
waits that many seconds, then flushes the CircuitsV2 player save-data
manager. It sets how often saves happen, not how much a room can hold. A
shorter interval sends more save traffic to a shared server. A longer
one loses more progress if the client crashes. DorkNet leaves it at
retail's 300 seconds. The clamp stays at 1..3600 and rejects non-finite
values.

### Limits these keys don't raise

- **Room ink 30000, lights 64 and AI controllers 101** are literals in
  the max-resources `CostInfo` (`CreationManager.txt:3374`, and again in
  the static constructor at `:22761`).
- **Event recursion depth is fixed at 16.**
  `Circuits.V2/BCNIMLFCMMD.MENMKOOAJPF` (ISIL 041,
  `Compare [rbx+240], 16`) raises "Max recursion depth reached. Do you
  have events in a loop?"
- **Room save uploads are capped at 100,000,000 bytes on the server.**
  `StorageController.Upload` sets `[RequestSizeLimit]` and
  `MultipartBodyLengthLimit` to 100 MB, up from Kestrel's 30 MB default
  that a room with tens of thousands of chips could reach. 100 MB is
  also Cloudflare's per-request ceiling, so raising it further does
  nothing behind the tunnel.

## How the budget is enforced

Each budget is a sliding window with **one bucket per rendered frame**.
`RpcTimeWindowFrame60s` is that window's length in frames, so 1 means
per frame and 60 means roughly per second at 60fps. On each send the
client adds the cost to the current bucket and compares the window's
running sum against the budget.

Two consequences worth keeping in mind:

- **Widening a window makes the limit stricter, not looser** — the same
  budget now has to cover more frames. Raise the budget instead.
- The **byte budget is checked before the RPC budget**, so lifting
  `Rpcs` alone leaves circuits blocked on `Bytes`. Both have to move.

Enforcement is entirely client-local: each client reads its own
gameconfig and polices its own outgoing sends, and nothing validates
budgets on receive. Photon's own server-side message-rate and bandwidth
caps still apply underneath this and are not affected by these keys.

## Clamps, and why they exist

`NormalizeCircuitsV2Limits` clamps admin input. Most of the clamps are
crash guards read straight off the disassembly:

- **`Rpcs` is floored at 1.** The CircuitsV2 debug subscreen renders
  `RpcRecentMax / Rpcs` with a hardware integer divide, so a budget of 0
  is a divide-by-zero the moment that panel opens.
- **Time windows are capped at 3600 frames.** A window allocates
  `new int[N]` — one bucket per frame — when circuits start, so an
  unbounded window is an out-of-memory kill rather than a bigger budget.
- **`Chips` is capped at 1,000,000 and floored at 1**, because it sets
  the size of four list preallocations in the chip-culling manager.
- **`InkPerChip` is capped at 30,000 and `CloudValues` at 10,000**,
  because the ink calculation multiplies them as unchecked ints.
- **`Compute` is capped at 10,000,000.** It allocates nothing, but it is
  the runaway-loop guard, and a value near `int.MaxValue` lets its
  counters wrap past the check.

Budgets themselves are capped at 1,000,000,000: the client only ever
compares a counter against them, and this leaves 2x headroom under
`int.MaxValue`.

## Operational notes

- The client builds its rate limiter **once, when circuits start**.
  Changing these settings does not affect anyone already in a room —
  players have to rejoin.
- Raising `Rpcs` also changes the Maker Pen top bar's network readout,
  which renders `<RpcRecentMax> / <Rpcs>`; the ratio collapses toward 0
  and the bar effectively tracks byte usage only.
- The keys are additive to the gameconfig array, so older clients that
  know nothing about CircuitsV2 just ignore them.
- Defaults are applied when the settings are read. While the
  `CircuitsV2LimitsJson` column is empty (no admin has saved the panel),
  the server always serves the current defaults. Saving the panel
  stores the full record, so every value sticks from then on, including
  the ones the admin didn't touch. **Reset** in the panel restores the
  current defaults.

## Where this lives

| Concern | File |
| --- | --- |
| Settings record, defaults, clamps, key emission | `DorkNet.Server/Services/ServerSettingsService.cs` |
| Persisted column | `ServerSettingsEntity.CircuitsV2LimitsJson` |
| Admin API | `api/admin/v1/settings/circuits-limits` (GET/POST) |
| Admin SPA | `admin-ui/src/pages/Settings.tsx` → CircuitsV2 limits panel |
| Served to the client | `api/gameconfigs/v1/all` |
| Tests | `DorkNet.Server.Tests/CircuitsV2LimitsTests.cs` |
