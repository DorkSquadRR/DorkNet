import { useEffect, useMemo, useState } from 'react';
import { api, get } from '../lib/api';
import { useApi } from '../lib/useApi';
import { useToast } from '../components/Toast';
import { Confirm } from '../components/Confirm';
import { RefreshCw, Search, Trash } from '../components/Icons';
import { relativeTime } from '../lib/format';

// Avatar-item slot ids from the watch enum (RecRoom.Avatar.Data.OutfitType
// in the decompiled client).
const SLOT_NAMES: Record<number, string> = {
  0: 'Head', 1: 'Face', 2: 'Hair', 3: 'Torso',
  4: 'Legs', 5: 'Feet', 6: 'Accessory', 7: 'Hand item',
};

// GiftRarity: RecNet.Avatars+GiftRarity. The 2020 watch tints the
// gift-box particle effects + post-open VFX from this value, so it's
// worth letting admins pick.
const RARITY_OPTIONS: Array<{ value: number; label: string }> = [
  { value: 0,  label: 'Common' },
  { value: 10, label: 'Uncommon' },
  { value: 20, label: 'Rare' },
  { value: 30, label: 'Epic' },
  { value: 50, label: 'Legendary' },
];

interface AvatarItem {
  guid: string;
  slot: number;
  friendlyName: string;
  tooltip: string;
  rarity: number;
  safe: boolean;
}

// Gifting fires the in-game gift-box popup by inserting a GiftPackageEntity
// row + pushing GiftPackageReceived. The watch's DowloadGiftPackages loop
// picks it up on the next poll, the player sees the popup, taps "open",
// and the consume endpoint writes the avatar item into their inventory.
// This is the only flow that triggers the popup — directly poking
// InventoryJson skips it entirely.
// Per-player gift composer, rendered as the "Gift" tab of the player
// detail modal. The recipient is fixed to the open player, so there's
// no picker and no page chrome — just the reward builder, presentation
// options, and that player's pending-gift inbox.
export function GiftPanel({ playerId }: { playerId: number }) {
  const [includeItem, setIncludeItem] = useState(true);
  const [includeCurrency, setIncludeCurrency] = useState(false);
  const [includeXp, setIncludeXp] = useState(false);

  const [item, setItem] = useState<AvatarItem | null>(null);
  const [manualGuid, setManualGuid] = useState('');
  const [avatarItemType, setAvatarItemType] = useState(0); // 0 Outfit, 1 HairDye

  const [currencyType, setCurrencyType] = useState(2);
  const [currencyAmount, setCurrencyAmount] = useState(1000);

  const [xp, setXp] = useState(500);

  const [rarity, setRarity] = useState(0);
  const [message, setMessage] = useState('A gift from the admins.');

  const [busy, setBusy] = useState(false);
  const [pendingRefreshKey, setPendingRefreshKey] = useState(0);
  const toast = useToast();

  const send = async () => {
    if (!includeItem && !includeCurrency && !includeXp) {
      return toast.push('Pick at least one reward (item / currency / xp)', 'error');
    }
    const guid = includeItem ? (manualGuid.trim() || item?.guid) : null;
    if (includeItem && !guid) {
      return toast.push('Pick an avatar item from the list, or paste a GUID', 'error');
    }
    setBusy(true);
    try {
      const res = await api<{ id: number }>(`/players/${playerId}/gift`, {
        method: 'POST',
        body: {
          AvatarItemGuid: includeItem ? guid : null,
          AvatarItemType: includeItem ? avatarItemType : null,
          CurrencyType: includeCurrency ? currencyType : null,
          Currency: includeCurrency ? currencyAmount : null,
          Xp: includeXp ? xp : null,
          Message: message.trim() || null,
          Rarity: rarity,
        },
      });
      toast.push(`Gift #${res.id} sent — the player will see the popup in-game`, 'success');
      setManualGuid('');
      setItem(null);
      setPendingRefreshKey(k => k + 1);
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <p className="text-xs text-ink-400 mb-4">
        Drops a wrapped gift box on this player's HUD — they tap to open it and the rewards land in their inventory / wallet.
      </p>

      <PendingGifts playerId={playerId} refreshKey={pendingRefreshKey} />

      <div className="grid grid-cols-1 lg:grid-cols-[1fr,360px] gap-4">
        <div className="card !p-5 space-y-4">
          <div className="space-y-2">
            <label className="flex items-center gap-2 text-sm text-ink-100 font-medium">
              <input type="checkbox" checked={includeItem} onChange={e => setIncludeItem(e.target.checked)} className="size-4 accent-brand-500" />
              Include avatar item
            </label>
            {includeItem && (
              <div className="pl-6 space-y-2">
                <div className="flex gap-2 items-center">
                  <span className="label">Type</span>
                  <select value={avatarItemType} onChange={e => setAvatarItemType(parseInt(e.target.value))} className="input max-w-[160px]">
                    <option value={0}>Outfit item</option>
                    <option value={1}>Hair dye</option>
                  </select>
                </div>
                <AvatarItemPicker selected={item} onSelect={(i) => { setItem(i); setManualGuid(''); }} />
                <label className="flex flex-col gap-1">
                  <span className="label">Or paste a raw GUID</span>
                  <input
                    value={manualGuid}
                    onChange={e => { setManualGuid(e.target.value); setItem(null); }}
                    placeholder="00000000-0000-0000-0000-000000000000"
                    className="input font-mono text-xs"
                  />
                </label>
              </div>
            )}
          </div>

          <div className="space-y-2">
            <label className="flex items-center gap-2 text-sm text-ink-100 font-medium">
              <input type="checkbox" checked={includeCurrency} onChange={e => setIncludeCurrency(e.target.checked)} className="size-4 accent-brand-500" />
              Include currency
            </label>
            {includeCurrency && (
              <div className="pl-6 grid grid-cols-2 gap-2">
                <label className="flex flex-col gap-1">
                  <span className="label">Currency</span>
                  <select value={currencyType} onChange={e => setCurrencyType(parseInt(e.target.value))} className="input">
                    <option value={1}>Tokens</option>
                    <option value={2}>Coins</option>
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span className="label">Amount</span>
                  <input type="number" min={1} value={currencyAmount} onChange={e => setCurrencyAmount(parseInt(e.target.value || '0'))} className="input" />
                </label>
              </div>
            )}
          </div>

          <div className="space-y-2">
            <label className="flex items-center gap-2 text-sm text-ink-100 font-medium">
              <input type="checkbox" checked={includeXp} onChange={e => setIncludeXp(e.target.checked)} className="size-4 accent-brand-500" />
              Include XP
            </label>
            {includeXp && (
              <div className="pl-6">
                <label className="flex flex-col gap-1">
                  <span className="label">XP amount</span>
                  <input type="number" min={1} value={xp} onChange={e => setXp(parseInt(e.target.value || '0'))} className="input w-32" />
                </label>
              </div>
            )}
          </div>
        </div>

        {/* Sidebar: presentation + send button. The watch tints the gift box
            and applies VFX based on rarity, so it's a real cosmetic choice. */}
        <div className="card !p-5 space-y-4 h-fit">
          <h2 className="text-sm font-semibold text-ink-50">Presentation</h2>
          <label className="flex flex-col gap-1">
            <span className="label">Rarity (gift-box tint)</span>
            <select value={rarity} onChange={e => setRarity(parseInt(e.target.value))} className="input">
              {RARITY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
          <label className="flex flex-col gap-1">
            <span className="label">Message (shown on the open card)</span>
            <textarea value={message} onChange={e => setMessage(e.target.value)} rows={3} className="input" />
          </label>
          <button onClick={send} disabled={busy} className="btn-primary w-full text-xs">
            {busy ? 'Sending…' : 'Send gift'}
          </button>
          <div className="text-[11px] text-ink-500">
            The recipient sees the gift box in their next HUD update (SignalR push) or when they next poll <code className="font-mono">/api/avatar/v2/gifts</code>.
          </div>
        </div>
      </div>
    </div>
  );
}

function AvatarItemPicker({ selected, onSelect }: { selected: AvatarItem | null; onSelect: (i: AvatarItem) => void }) {
  const { data: catalog, loading } = useApi<AvatarItem[]>('/avatar-items');
  const [search, setSearch] = useState('');
  const [slotFilter, setSlotFilter] = useState<number | 'all'>('all');

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (catalog ?? []).filter(it => {
      if (slotFilter !== 'all' && it.slot !== slotFilter) return false;
      if (term && !it.friendlyName.toLowerCase().includes(term) && !it.guid.toLowerCase().includes(term)) return false;
      return true;
    });
  }, [catalog, search, slotFilter]);

  const slots = useMemo(() => Array.from(new Set((catalog ?? []).map(c => c.slot))).sort((a, b) => a - b), [catalog]);

  return (
    <div>
      <div className="flex flex-wrap gap-2 items-end mb-2">
        <div className="relative flex-1 min-w-[180px]">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search by name or GUID…" className="input pl-8" />
        </div>
        <select value={slotFilter} onChange={e => setSlotFilter(e.target.value === 'all' ? 'all' : parseInt(e.target.value))} className="input max-w-[140px]">
          <option value="all">All slots</option>
          {slots.map(s => <option key={s} value={s}>{SLOT_NAMES[s] ?? `Slot #${s}`}</option>)}
        </select>
      </div>
      <div className="rounded-lg border border-ink-800 max-h-64 overflow-y-auto">
        {loading && <div className="p-3 text-xs text-ink-400">Loading catalog…</div>}
        {catalog && filtered.length === 0 && <div className="p-3 text-xs text-ink-400">No items match.</div>}
        {filtered.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[560px]">
            <tbody className="divide-y divide-ink-800">
              {filtered.slice(0, 200).map(it => (
                <tr
                  key={it.guid}
                  onClick={() => onSelect(it)}
                  className={`cursor-pointer table-row-hover ${selected?.guid === it.guid ? 'bg-brand-500/10' : ''}`}
                >
                  <td className="px-3 py-1.5">
                    <div className="text-ink-100 text-sm">{it.friendlyName || <span className="text-ink-500">unnamed</span>}</div>
                    <div className="font-mono text-[10px] text-ink-500">{it.guid}</div>
                  </td>
                  <td className="px-3 py-1.5 text-xs text-ink-300 whitespace-nowrap">{SLOT_NAMES[it.slot] ?? `slot ${it.slot}`}</td>
                </tr>
              ))}
              {filtered.length > 200 && (
                <tr><td colSpan={2} className="p-3 text-center text-xs text-ink-500">+ {filtered.length - 200} more — refine the search.</td></tr>
              )}
            </tbody>
          </table></div>
        )}
      </div>
    </div>
  );
}

// ── Pending gift inbox for the selected recipient ───────────────────
// Surfaces any unconsumed gifts on the recipient's account so we can
// see what's actually queued and delete broken / abandoned entries
// from earlier test sends. IsGifted-flagged or otherwise malformed
// rows might still be in the inbox refusing to render — clear them
// here.

interface PendingGift {
  id: number;
  fromPlayerId: number | null;
  avatarItemType: number;
  avatarItemDescOrHairDyeDesc: string;
  currencyType: number;
  currency: number;
  xp: number;
  level: number;
  giftContext: number;
  giftRarity: number;
  message: string;
  consumed: boolean;
  isValid: boolean;
  isGifted: boolean;
  createdAt: string;
}

function PendingGifts({ playerId, refreshKey }: { playerId: number; refreshKey: number }) {
  const [rows, setRows] = useState<PendingGift[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<PendingGift | null>(null);
  const toast = useToast();

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      const data = await get<PendingGift[]>(`/players/${playerId}/gifts`);
      setRows(data);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [playerId, refreshKey]);

  const delOne = async () => {
    if (!pendingDelete) return;
    try {
      await api(`/gifts/${pendingDelete.id}`, { method: 'DELETE' });
      toast.push(`Deleted gift #${pendingDelete.id}`, 'success');
      load();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const clearAll = async () => {
    try {
      const res = await api<{ removed: number }>(`/players/${playerId}/gifts/clear`, { method: 'POST' });
      toast.push(`Removed ${res.removed} pending gift${res.removed === 1 ? '' : 's'}`, 'success');
      load();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  // Most of the broken test-gifts will have IsGifted=true or empty
  // payloads — flag those so the user can spot stuck rows at a glance.
  const looksBroken = (g: PendingGift) =>
    g.isGifted
    || !g.isValid
    || (g.avatarItemType === 0
        && !g.avatarItemDescOrHairDyeDesc
        && g.currency === 0
        && g.xp === 0
        && g.level === 0);

  return (
    <div className="card !p-4 mb-4 max-w-5xl">
      <div className="flex items-center justify-between mb-3">
        <div>
          <h2 className="text-sm font-semibold text-ink-50">Pending gifts on this player</h2>
          <p className="text-xs text-ink-400">Unconsumed gift packages currently in the recipient's inbox. Delete anything stuck.</p>
        </div>
        <div className="flex items-center gap-2">
          <button onClick={load} className="btn-ghost text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
          {rows && rows.length > 0 && (
            <button onClick={() => setConfirmClear(true)} className="btn-danger text-xs">
              Clear all ({rows.length})
            </button>
          )}
        </div>
      </div>

      {err && <div className="text-xs text-danger">{err}</div>}
      {!err && rows && rows.length === 0 && (
        <div className="text-xs text-ink-500 py-2">Inbox is empty.</div>
      )}
      {rows && rows.length > 0 && (
        <div className="table-scroll"><table className="w-full text-sm min-w-[560px]">
          <thead className="text-[11px] uppercase tracking-wider text-ink-400 border-b border-ink-800">
            <tr>
              <th className="text-left font-medium pb-2">#</th>
              <th className="text-left font-medium pb-2">Contents</th>
              <th className="text-left font-medium pb-2">From</th>
              <th className="text-left font-medium pb-2">Created</th>
              <th />
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-800">
            {rows.map(g => (
              <tr key={g.id} className={`table-row-hover ${looksBroken(g) ? 'bg-danger/5' : ''}`}>
                <td className="py-2 text-ink-400 tabular-nums">{g.id}</td>
                <td className="py-2 text-ink-100">
                  <div className="flex flex-wrap gap-1">
                    {g.avatarItemDescOrHairDyeDesc && (
                      <span className="badge-neutral font-mono">item</span>
                    )}
                    {g.currency > 0 && (
                      <span className="badge-neutral">{g.currency.toLocaleString()} {g.currencyType === 1 ? 'tokens' : 'coins'}</span>
                    )}
                    {g.xp > 0 && <span className="badge-neutral">{g.xp} xp</span>}
                    {g.isGifted && <span className="badge-banned">IsGifted=true (broken)</span>}
                    {!g.isValid && <span className="badge-banned">IsValid=false</span>}
                  </div>
                  {g.message && <div className="text-xs text-ink-400 mt-0.5">{g.message}</div>}
                </td>
                <td className="py-2 text-xs text-ink-300">{g.fromPlayerId ? `#${g.fromPlayerId}` : <span className="text-ink-500">system</span>}</td>
                <td className="py-2 text-xs text-ink-300">{relativeTime(g.createdAt)}</td>
                <td className="py-2 text-right">
                  <button onClick={() => setPendingDelete(g)} className="btn-ghost text-xs text-danger">
                    <Trash />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table></div>
      )}

      <Confirm
        open={confirmClear}
        onClose={() => setConfirmClear(false)}
        title="Clear all pending gifts"
        body={<>Remove every unconsumed gift currently in this player's inbox? Consumed gifts (already opened) are left untouched.</>}
        confirmLabel="Clear all"
        destructive
        onConfirm={clearAll}
      />
      <Confirm
        open={pendingDelete !== null}
        onClose={() => setPendingDelete(null)}
        title={`Delete gift #${pendingDelete?.id}`}
        body={<>This removes the gift permanently. If the player had already opened it, the inventory grant from that consume is unaffected.</>}
        confirmLabel="Delete"
        destructive
        onConfirm={delOne}
      />
    </div>
  );
}
