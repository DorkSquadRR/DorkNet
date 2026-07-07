# Dumping Rec Room avatar items + color variants (UnityPy)

> **⚠️ Model correction (2023 builds).** The color-variant sections below key
> variants on `(item, swatch)`. That is **wrong for 2023**: one swatch material
> is shared across many colorways (Angler hat's swatch renders Blue/Gray/Tan),
> so a `{item},{swatch},{mask}` descriptor collapses distinct colors. The real
> identity is the **combination GUID**, and the client equips the descriptor
> `{itemGuid},{combinationGuid…}` **verbatim** as an opaque `combinationLookup`
> key. `color_variants.json` (schema with per-entry `combination_id` +
> `descriptor`) and `StoreService` are migrated to this model; the runtime
> `AvatarCatalogDumpMelon` already emits the combination id per variant. The
> UnityPy `_Combination` bundle scan below is now only used to recover the
> human **color label** (the asset stem), not the variant identity.



How to statically extract the avatar catalog (outfits) and color variants
from a Rec Room client build and seed them into the DorkNet store. This is
the runbook behind `DorkNet.Server/Data/avatar_item_lookup.json` (outfits)
and `color_variants.json` (variants).

Everything here is **offline / static** — no running game, no MelonLoader.
It was done for the **2023.03.21** build; the same steps work for any 2023
build (paths and a couple of `path_id`s change).

---

## 0. Why it's fiddly (read this first)

- The 2023 client is **IL2CPP with Unity type trees stripped** in
  `resources.assets` (`enable_type_tree=False`). UnityPy therefore **cannot
  read MonoBehaviour fields by name** there — you parse raw serialized bytes.
- `TypeTreeGeneratorAPI` (from the IL2CPP metadata) can rebuild type trees
  for **leaf** types (e.g. `AvatarItemData`, 66 nodes) but **fails on the
  container** `AvatarItemWardrobeRuntimeConfig` (its nested
  `SerializedDictionary<>` generic → `failed to dump nodes raw`). So you
  don't use it for the actual extraction — raw parsing is simpler and works.
- The **StreamingAssets/aa bundles DO carry type trees**, but their custom
  MonoBehaviours still won't `read()` cleanly (a consistent ~44-byte
  mismatch). Again: parse the raw header instead.
- **Build matters.** Item GUIDs are stable, but the *set* differs per build.
  The 2023.06.21 catalog has 926 items; **2023.03.21 has 882**. Never seed
  June-only items onto a March server — the March client can't render them
  (blank store tiles). Always dump from the build your server targets.

---

## 1. Prerequisites

```bash
pip install UnityPy            # 1.25 used here
pip install TypeTreeGeneratorAPI   # only needed if you want leaf type trees
```

Get the build. If it's a restic snapshot, restore it; the game files land
under a nested path, e.g.:

```
C:\tmp\recroom-2023-03-21-restic\Z\Rec Room old versions\staging\7490748483298966814\RecRoom_Data
```

Key inputs inside `RecRoom_Data`:
- `resources.assets` — holds the wardrobe config MonoBehaviour (outfits + combo keys)
- `StreamingAssets\aa\StandaloneWindows64\*.bundle` — 6365 hash-named bundles (the `_Combination` variant assets)
- `il2cpp_data\Metadata\global-metadata.dat` + `..\GameAssembly.dll` — only if using TypeTreeGeneratorAPI

Set `DATA` to the `RecRoom_Data` path in the scripts below.

---

## 2. Dump the outfits (from `resources.assets`)

The whole catalog is one MonoBehaviour: `AvatarItemWardrobeRuntimeConfig`
(namespace `RecRoom.Avatars.Data.Runtime`, assembly `Assembly-CSharp`). Its
`avatarItemDataLookup` dictionary maps GUID → `AvatarItemData`, serialized as
repeated records:

```
[ keyGuid string ][ Name string ][ AvatarItemGuid string ][ OutfitType int32 ] ...
```

`Name` is **paren-wrapped** (`(Wrist_VampireHunter)`) — that's why grepping
for a plain name misses it. `OutfitType` is the enum:
`Hat=0, Hair=2, Ear=3, Eye=10, Beard=20, Shoulder=100, Shirt=101, Waist=102,
Neck=103, TeamJersey=104, Wrist=200, TeamWrist=203, Legs=300, Feet=301`.

First find the config MonoBehaviour: it's the one with by far the most GUIDs.

```python
import UnityPy, os, re, struct, json
DATA = r"...\RecRoom_Data"
env = UnityPy.load(os.path.join(DATA, "resources.assets"))
GUID = re.compile(rb'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
best = (0, None)
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    try: raw = o.get_raw_data()
    except Exception: continue
    c = len(GUID.findall(raw))
    if c > best[0]: best = (c, o.path_id)
print("config path_id:", best[1], "guids:", best[0])   # 2023.03.21 -> 235957
```

Then parse the AvatarItemData records. A record is a `Name` (printable, not a
GUID/CSV) immediately followed by a GUID string, then an int in enum range:

```python
raw = next(o.get_raw_data() for o in env.objects
           if o.type.name == "MonoBehaviour" and o.path_id == best[1])
n = len(raw)
G = re.compile(r'^(?:[0-9a-fA-F-]{36}|[A-Za-z0-9_\-]{22})$')

def rstr(i):
    if i+4 > n: return None
    L = struct.unpack_from("<i", raw, i)[0]
    if L < 0 or L > 128 or i+4+L > n: return None
    b = raw[i+4:i+4+L]
    if not all(32 <= c < 127 for c in b): return None
    return b.decode(), (i+4+L+3) & ~3      # strings are 4-byte aligned

items = {}; i = 0
while i < n-8:
    r = rstr(i)
    if not r: i += 1; continue
    name, e = r
    if G.match(name) or ',' in name or not re.search(r'[A-Za-z]', name):
        i = e; continue                     # not a name
    g = rstr(e)
    if not g or not G.match(g[0]): i = e; continue
    guid, ge = g
    ot = struct.unpack_from("<i", raw, ge)[0]
    if ot < -1 or ot > 5000: i = e; continue
    items.setdefault(guid, {"guid": guid, "name": name, "outfit_type": ot})
    i = ge + 4
print("outfits:", len(items))              # 2023.03.21 -> 882
```

To seed the store, each item needs the full `avatar_item_lookup.json` shape
(`guid, name, outfit_type, outfit_set, drawer_index, hides_*, uses_*,
is_team_item`). Defaults (`0` / `false`) are fine for new items. The store
auto-creates a `wardrobe-{guid}` tile per catalog item on restart
(`StoreService.SeedClothingFromGameAsync`).

> The March catalog already matches the shipped 882, so there's usually
> nothing to add — this step is mainly for verifying / re-dumping a new build.

---

## 3. Dump the color variants (from the `_Combination` bundles)

Variants are **`*_Combination` MonoBehaviours** in the aa bundles, named
`Item_Slot_Colour_Combination` (e.g. `Jersey_Shirt_Outline_Yellow_Combination`).
Each carries a GUID CSV string `{combinationId},{swatchGuid},{mask},` —
**the swatch GUID is the 2nd element** (verified: the Jersey example's 2nd
GUID `d2a692e6…` equals the shipped `color_variants.json` swatch_guid).

The MonoBehaviour header layout (offsets):
`m_GameObject PPtr (12) + m_Enabled+pad (4) + m_Script PPtr (12) = 28`, then
`m_Name` (length-prefixed, 4-byte aligned) at **offset 28**, then fields.

Match each combination to a base item by **exact `(slot, name)`** — catalog
names are `(Slot_Name)`, combination names are `Name_Slot_Color`. This is far
more reliable than the old name-prefix matcher.

```python
import glob, UnityPy, struct, json, re
DATA = r"...\RecRoom_Data"
BD = os.path.join(DATA, "StreamingAssets", "aa", "StandaloneWindows64")
outfits = json.load(open("march_outfits.json"))   # from step 2

# catalog index: (slot_lower, name_lower) -> item
idx, slots = {}, set()
for it in outfits:
    inner = it["name"].strip("()")
    if "_" not in inner: continue
    slot, name = inner.split("_", 1)
    idx[(slot.lower(), name.lower())] = it
    slots.add(slot.lower())

def rstr(raw, i):
    if i+4 > len(raw): return None
    L = struct.unpack_from("<i", raw, i)[0]
    if L < 0 or L > 256 or i+4+L > len(raw): return None
    b = raw[i+4:i+4+L]
    if not all(32 <= c < 127 for c in b): return None
    return b.decode(), (i+4+L+3) & ~3

GCSV = re.compile(r'^(?:[0-9a-fA-F-]{36}|[A-Za-z0-9_\-]{22})(?:,(?:[0-9a-fA-F-]{36}|[A-Za-z0-9_\-]{22})?)*,?$')

def match(stem):                       # stem = "Item_Slot_Color"
    toks = stem.split("_")
    for i in range(1, len(toks)):
        if toks[i].lower() in slots and (toks[i].lower(), "_".join(toks[:i]).lower()) in idx:
            return idx[(toks[i].lower(), "_".join(toks[:i]).lower())], "_".join(toks[i+1:]) or "Default"
    return None, None

records = {}
for bp in sorted(glob.glob(os.path.join(BD, "*.bundle"))):
    try: env = UnityPy.load(bp)
    except Exception: continue
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        try: raw = o.get_raw_data()
        except Exception: continue
        r = rstr(raw, 28)                     # m_Name at offset 28
        if not r or not r[0].endswith("_Combination"): continue
        name, e = r
        guidcsv, j = None, e                  # find the GUID CSV among next strings
        for _ in range(8):
            rr = rstr(raw, j)
            if rr is None: j += 1; continue
            if GCSV.match(rr[0]) and ('-' in rr[0] or len(rr[0].split(',')[0]) == 22):
                guidcsv = rr[0]; break
            j = rr[1]
        if not guidcsv: continue
        parts = [p for p in guidcsv.split(',') if p]
        swatch = parts[1] if len(parts) >= 2 else parts[0]
        mask = parts[2] if len(parts) >= 3 else ""
        item, color = match(name[:-len("_Combination")])
        if not item: continue
        records[(item["guid"], swatch)] = {
            "swatch_name": name[:-len("_Combination")] + "_Swatch",
            "swatch_guid": swatch, "color": color or "Default",
            "item_name": item["name"], "item_guid": item["guid"],
            "item_outfit_type": item["outfit_type"], "mask_guid": mask,
        }
print("distinct variants:", len(records))    # 2023.03.21 -> 452
```

The full scan of 6365 bundles takes ~5 minutes. Expect ~1844 combinations →
~836 matched → ~452 distinct.

---

## 4. Merge into `color_variants.json` (additive)

The store keys colored tiles by `wardrobe-colored-{item_guid}-{color}`, so
`item_guid`, `swatch_guid`, and a distinct `color` are all required
(`StoreService.SeedColorVariantsAsync`). Only add records **not already
present** — keep the diff additive so you never regress existing entries.

```python
cv = json.load(open("DorkNet.Server/Data/color_variants.json"))
exist = {(m["item_guid"].lower(), m["swatch_guid"].lower()) for m in cv["matches"]}
for k, r in records.items():
    if (k[0].lower(), k[1].lower()) not in exist:
        cv["matches"].append(r)
json.dump(cv, open("DorkNet.Server/Data/color_variants.json", "w"), indent=2)
```

For the 2023.03.21 scan this added **147 net-new** variants (419 → 566).
Restart the server; `SeedColorVariantsAsync` creates the new
`wardrobe-colored-*` tiles.

---

## 5. Sanity checks

- Every new record: the base item's inner name must appear in the swatch name
  (`(Hat_CatEarsWithEarring)` ↔ `CatEarsWithEarring_Hat_Grey_Swatch`).
- Diff must be **additive** (`git diff --numstat` → `N  0`). Existing entries
  stay byte-identical.
- The bare `wardrobe-{guid}` tile is auto-suppressed for any item that now has
  a color variant (the colored tiles render the correct swatch) — that's
  intended, not a regression.

---

## Gotchas cheat-sheet

| Symptom | Cause / fix |
|---|---|
| `enable_type_tree=False` | 2023 strips type trees in resources.assets — parse raw bytes, don't `read()` |
| Generator: `failed to dump nodes raw` on the config | nested `SerializedDictionary` breaks it — use it only for leaf types, or skip it |
| Item names not found by grep | they're paren-wrapped: `(Shirt_Jersey)` |
| Bundle MB `read()` off by ~44 bytes | type-tree mismatch — parse the raw header (`m_Name` at offset 28) |
| Wrong item count (926 vs 882) | you dumped the wrong build — use the one the server targets |
| Combo keys `{guid},{guid}` don't contain the base item | those are `{swatch},{mask}` in `resources.assets`; the base link is only in the bundle `_Combination` name |
| Variant tiles collide / blank labels | you skipped the color; every colored slug needs a distinct `color` |
