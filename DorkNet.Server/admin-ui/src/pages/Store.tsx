import { useMemo, useState } from 'react';
import { api } from '../lib/api';
import type { StoreItem, StorefrontDefinition } from '../lib/types';
import { profileImageUrl } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { Modal } from '../components/Modal';
import { useToast } from '../components/Toast';
import { Confirm } from '../components/Confirm';
import { currencyName, num, relativeTime } from '../lib/format';
import { Plus, RefreshCw, Search, Trash } from '../components/Icons';

// Storefronts the SPA can target when /admin/v1/storefronts hasn't
// responded yet. Mirrors StoreService.GetStorefrontDefinitions on the
// server side — replaced by the live response as soon as it arrives.
const FALLBACK_STOREFRONTS: StorefrontDefinition[] = [
  { key: 'main', storefrontType: null, displayName: 'Main watch catalog', scope: 'watch' },
  { key: 'watch', storefrontType: 3, displayName: 'Watch gift-drop shelf', scope: 'watch' },
  { key: 'all', storefrontType: null, displayName: 'All storefront shelves', scope: 'shared' },
  { key: 'rro', storefrontType: null, displayName: 'All RRO and Rec Center shelves', scope: 'shared' },
  { key: 'giftdrop:1', storefrontType: 1, displayName: 'Laser Tag', scope: 'room' },
  { key: 'giftdrop:2', storefrontType: 2, displayName: 'Rec Center', scope: 'room' },
  { key: 'giftdrop:100', storefrontType: 100, displayName: 'Quest - Lost Skulls', scope: 'room' },
  { key: 'giftdrop:101', storefrontType: 101, displayName: 'Quest - Dracula', scope: 'room' },
  { key: 'giftdrop:102', storefrontType: 102, displayName: 'Quest - Golden Trophy', scope: 'room' },
  { key: 'giftdrop:103', storefrontType: 103, displayName: 'Quest - Crimson Cauldron', scope: 'room' },
  { key: 'giftdrop:200', storefrontType: 200, displayName: 'Rec Royale', scope: 'room' },
  { key: 'giftdrop:300', storefrontType: 300, displayName: 'Cafe', scope: 'room' },
  { key: 'giftdrop:400', storefrontType: 400, displayName: 'Paintball', scope: 'room' },
  { key: 'giftdrop:401', storefrontType: 401, displayName: 'Paintball - River', scope: 'room' },
  { key: 'giftdrop:402', storefrontType: 402, displayName: 'Paintball - Homestead', scope: 'room' },
  { key: 'giftdrop:403', storefrontType: 403, displayName: 'Paintball - Quarry', scope: 'room' },
  { key: 'giftdrop:404', storefrontType: 404, displayName: 'Paintball - Clear Cut', scope: 'room' },
  { key: 'giftdrop:405', storefrontType: 405, displayName: 'Paintball - Spillway', scope: 'room' },
  { key: 'giftdrop:406', storefrontType: 406, displayName: 'Paintball - Sunset Drive-In', scope: 'room' },
  { key: 'giftdrop:500', storefrontType: 500, displayName: 'Bowling', scope: 'room' },
  { key: 'giftdrop:600', storefrontType: 600, displayName: 'Stunt Runner', scope: 'room' },
  { key: 'giftdrop:700', storefrontType: 700, displayName: 'Dorm Mirror', scope: 'room' },
  { key: 'season:1', storefrontType: null, displayName: 'Season 1', scope: 'season' },
];
const CATEGORIES = ['head', 'torso', 'legs', 'feet', 'accessory', 'hair', 'face', 'consumable', 'roomtemplate'];

// Sort options for the catalog list. `dir` keeps the comparator direction
// out of the sort dropdown labels so "Newest first" reads naturally.
type SortKey = 'name-asc' | 'name-desc' | 'price-asc' | 'price-desc' | 'updated-desc' | 'created-desc';
const SORT_OPTIONS: { key: SortKey; label: string }[] = [
  { key: 'name-asc',     label: 'Name (A → Z)'        },
  { key: 'name-desc',    label: 'Name (Z → A)'        },
  { key: 'price-asc',    label: 'Price (low → high)'  },
  { key: 'price-desc',   label: 'Price (high → low)'  },
  { key: 'updated-desc', label: 'Recently updated'    },
  { key: 'created-desc', label: 'Recently created'    },
];

type ViewMode = 'cards' | 'table';

export function Store() {
  const [storefront, setStorefront] = useState('');
  const [category, setCategory] = useState('');
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<SortKey>('updated-desc');
  const [view, setView] = useState<ViewMode>('cards');
  const [activeFilter, setActiveFilter] = useState<'all' | 'active' | 'inactive' | 'limited' | 'expired'>('all');
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [editing, setEditing] = useState<StoreItem | null | 'new'>(null);
  const [pendingDelete, setPendingDelete] = useState<StoreItem | null>(null);
  const [pendingBulk, setPendingBulk] = useState<null | { action: 'delete' | 'activate' | 'deactivate'; count: number }>(null);
  const [bulkBusy, setBulkBusy] = useState(false);
  const toast = useToast();

  // Server-side pagination is overkill for this dataset (low hundreds
  // of rows typically); pull a generous slice and filter / sort in the
  // browser so the storefront/category dropdowns don't trigger network
  // round-trips on every change.
  const qs = new URLSearchParams();
  qs.set('take', '500');
  const { data, loading, error, refresh } = useApi<StoreItem[]>(`/storeitems?${qs}`);
  const { data: storefrontData } = useApi<StorefrontDefinition[]>('/storefronts');
  const storefronts = storefrontData ?? FALLBACK_STOREFRONTS;

  // Apply every filter (search / storefront / category / activeFilter)
  // then sort. Done in one memo so a single state change re-renders
  // once instead of cascading through multiple useMemos.
  const items = useMemo(() => {
    if (!data) return [] as StoreItem[];
    const now = Date.now();
    const q = search.trim().toLowerCase();
    const filtered = data.filter(it => {
      if (storefront && it.storefront !== storefront) return false;
      if (category && it.category !== category) return false;
      if (q && !it.slug.toLowerCase().includes(q) && !it.displayName.toLowerCase().includes(q)) return false;
      if (activeFilter === 'active' && !it.isActive) return false;
      if (activeFilter === 'inactive' && it.isActive) return false;
      if (activeFilter === 'limited' && !it.isLimitedTime) return false;
      if (activeFilter === 'expired') {
        if (!it.availableUntil) return false;
        if (Date.parse(it.availableUntil) > now) return false;
      }
      return true;
    });
    const cmp = (a: StoreItem, b: StoreItem) => {
      switch (sort) {
        case 'name-asc':     return a.displayName.localeCompare(b.displayName);
        case 'name-desc':    return b.displayName.localeCompare(a.displayName);
        case 'price-asc':    return a.price - b.price;
        case 'price-desc':   return b.price - a.price;
        case 'updated-desc': return Date.parse(b.updatedAt) - Date.parse(a.updatedAt);
        case 'created-desc': return Date.parse(b.createdAt) - Date.parse(a.createdAt);
      }
    };
    return [...filtered].sort(cmp);
  }, [data, storefront, category, search, sort, activeFilter]);

  // Bulk-selection helpers — keep the Set immutable so React picks up
  // the change without us tracking version numbers.
  const toggleSelected = (id: number) => setSelected(prev => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });
  const allVisibleSelected = items.length > 0 && items.every(it => selected.has(it.id));
  const selectAllVisible = () => setSelected(prev => {
    if (allVisibleSelected) {
      const next = new Set(prev);
      for (const it of items) next.delete(it.id);
      return next;
    }
    const next = new Set(prev);
    for (const it of items) next.add(it.id);
    return next;
  });
  const clearSelection = () => setSelected(new Set());

  const del = async () => {
    if (!pendingDelete) return;
    try {
      await api(`/storeitems/${pendingDelete.id}`, { method: 'DELETE' });
      toast.push('Item deleted', 'success');
      setPendingDelete(null);
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  // Bulk action — sequential rather than Promise.all so the server
  // doesn't get hit with 50 simultaneous writes (and so a partial
  // failure tells us how far we got). Selected ids that aren't in the
  // current `data` array (e.g. removed while the user was selecting)
  // are skipped without error.
  const runBulk = async () => {
    if (!pendingBulk || !data) return;
    setBulkBusy(true);
    let ok = 0;
    let failed = 0;
    try {
      const targets = data.filter(it => selected.has(it.id));
      for (const it of targets) {
        try {
          if (pendingBulk.action === 'delete') {
            await api(`/storeitems/${it.id}`, { method: 'DELETE' });
          } else {
            await api(`/storeitems/${it.id}`, {
              method: 'PUT',
              body: { IsActive: pendingBulk.action === 'activate' },
            });
          }
          ok++;
        } catch { failed++; }
      }
      const verb = pendingBulk.action === 'delete' ? 'deleted'
                : pendingBulk.action === 'activate' ? 'activated'
                : 'deactivated';
      toast.push(`${verb} ${ok}/${ok + failed}`, failed > 0 ? 'error' : 'success');
      clearSelection();
      setPendingBulk(null);
      refresh();
    } finally {
      setBulkBusy(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Store catalog"
        blurb="Every row in StoreItemEntity across every storefront. Live items prefer IsActive=false (toggle from the card or bulk bar); hard delete is here for test cleanup."
        actions={<>
          <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
          <button onClick={() => setEditing('new')} className="btn-primary text-xs">
            <Plus /> New item
          </button>
        </>}
      />

      {/* Filter / sort bar — kept on a single card so the controls share
          visual weight and the count + view-toggle anchor the right edge. */}
      <div className="card !p-3 mb-4 space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative flex-1 min-w-[180px] max-w-md">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search slug or display name…"
              className="input pl-8 w-full"
            />
          </div>
          <select value={storefront} onChange={e => setStorefront(e.target.value)} className="input max-w-[200px]">
            <option value="">All storefronts</option>
            {storefronts.map(s => <option key={s.key} value={s.key}>{storefrontLabel(s)}</option>)}
          </select>
          <select value={category} onChange={e => setCategory(e.target.value)} className="input max-w-[160px]">
            <option value="">All categories</option>
            {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
          <select value={sort} onChange={e => setSort(e.target.value as SortKey)} className="input max-w-[180px]">
            {SORT_OPTIONS.map(s => <option key={s.key} value={s.key}>{s.label}</option>)}
          </select>
          <div className="ml-auto inline-flex rounded-md border border-ink-800 bg-ink-900/40 p-0.5 text-[11px]">
            <button
              onClick={() => setView('cards')}
              className={`px-2 py-1 rounded ${view === 'cards' ? 'bg-ink-800 text-ink-50' : 'text-ink-400 hover:text-ink-200'}`}
            >Cards</button>
            <button
              onClick={() => setView('table')}
              className={`px-2 py-1 rounded ${view === 'table' ? 'bg-ink-800 text-ink-50' : 'text-ink-400 hover:text-ink-200'}`}
            >Table</button>
          </div>
        </div>

        {/* Status chips — single-select. Stays on its own row so labels
            wrap predictably on narrow screens. */}
        <div className="flex flex-wrap items-center gap-2 text-[11px]">
          {(['all', 'active', 'inactive', 'limited', 'expired'] as const).map(f => (
            <button
              key={f}
              onClick={() => setActiveFilter(f)}
              className={`rounded-full border px-2.5 py-1 transition-colors ${
                activeFilter === f
                  ? 'border-brand-500/60 bg-brand-500/15 text-brand-100'
                  : 'border-ink-800 bg-ink-900/40 text-ink-400 hover:border-ink-700 hover:text-ink-200'
              }`}
            >
              {f === 'all' ? 'All' :
               f === 'active' ? 'Active only' :
               f === 'inactive' ? 'Inactive only' :
               f === 'limited' ? 'Limited time' :
               'Expired'}
            </button>
          ))}
          <div className="ml-auto text-xs text-ink-400">
            {data ? `${items.length} of ${data.length} items` : ''}
          </div>
        </div>
      </div>

      {/* Bulk action bar — only shows when there's a selection. Floats
          above the grid as a sticky-feeling banner. */}
      {selected.size > 0 && (
        <div className="card !p-2 mb-4 border-brand-500/40 bg-brand-500/5 flex flex-wrap items-center gap-2">
          <span className="text-sm text-ink-100">
            <span className="font-semibold text-brand-100">{selected.size}</span> selected
          </span>
          <button onClick={selectAllVisible} className="btn-ghost text-xs">
            {allVisibleSelected ? 'Deselect visible' : 'Select all visible'}
          </button>
          <div className="ml-auto flex gap-1">
            <button
              onClick={() => setPendingBulk({ action: 'activate', count: selected.size })}
              className="btn-secondary text-xs"
              disabled={bulkBusy}
            >Activate</button>
            <button
              onClick={() => setPendingBulk({ action: 'deactivate', count: selected.size })}
              className="btn-secondary text-xs"
              disabled={bulkBusy}
            >Deactivate</button>
            <button
              onClick={() => setPendingBulk({ action: 'delete', count: selected.size })}
              className="btn-danger text-xs"
              disabled={bulkBusy}
            ><Trash /> Delete</button>
            <button onClick={clearSelection} className="btn-ghost text-xs">Clear</button>
          </div>
        </div>
      )}

      {error && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{error}</div>}
      {!loading && items.length === 0 && (
        <Empty title={data && data.length === 0 ? 'No items in the catalog' : 'No items match the filters'} />
      )}

      {items.length > 0 && view === 'cards' && (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3">
          {items.map(it => (
            <StoreCard
              key={it.id}
              item={it}
              selected={selected.has(it.id)}
              onToggleSelected={() => toggleSelected(it.id)}
              onEdit={() => setEditing(it)}
              onDelete={() => setPendingDelete(it)}
            />
          ))}
        </div>
      )}

      {items.length > 0 && view === 'table' && (
        <div className="card overflow-hidden">
          <div className="table-scroll">
            <table className="w-full text-sm min-w-[820px]">
              <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
                <tr>
                  <th className="w-10 px-3 py-2.5">
                    <input
                      type="checkbox"
                      checked={allVisibleSelected}
                      onChange={selectAllVisible}
                      className="size-4 accent-brand-500"
                      aria-label="Select all visible"
                    />
                  </th>
                  <th className="w-14 px-2 py-2.5" />
                  <th className="text-left font-medium px-2 py-2.5">Item</th>
                  <th className="text-left font-medium px-3 py-2.5">Storefront</th>
                  <th className="text-left font-medium px-3 py-2.5">Category</th>
                  <th className="text-right font-medium px-3 py-2.5">Price</th>
                  <th className="text-left font-medium px-3 py-2.5">State</th>
                  <th className="w-32 px-3 py-2.5" />
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-800">
                {items.map(it => (
                  <tr key={it.id} className={`table-row-hover ${selected.has(it.id) ? 'bg-brand-500/5' : ''}`}>
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        checked={selected.has(it.id)}
                        onChange={() => toggleSelected(it.id)}
                        className="size-4 accent-brand-500"
                      />
                    </td>
                    <td className="px-2 py-2"><Thumb imageName={it.imageName} size={36} /></td>
                    <td className="px-2 py-2">
                      <div className="text-ink-50">{it.displayName}</div>
                      <div className="text-[11px] font-mono text-ink-500">{it.slug}</div>
                    </td>
                    <td className="px-3 py-2 text-ink-200 text-xs">{it.storefront}</td>
                    <td className="px-3 py-2 text-ink-200 text-xs">{it.category}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-ink-100">
                      {num(it.price)} <span className="text-ink-400 text-xs">{currencyName(it.currencyType)}</span>
                    </td>
                    <td className="px-3 py-2">
                      <ItemBadges item={it} />
                    </td>
                    <td className="px-3 py-2 text-right whitespace-nowrap">
                      <button onClick={() => setEditing(it)} className="btn-ghost text-xs">Edit</button>
                      <button onClick={() => setPendingDelete(it)} className="btn-ghost text-xs text-danger">
                        <Trash />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {editing && (
        <StoreItemForm
          item={editing === 'new' ? null : editing}
          storefronts={storefronts}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); refresh(); }}
        />
      )}

      <Confirm
        open={pendingDelete !== null}
        onClose={() => setPendingDelete(null)}
        title="Delete store item"
        body={<>Hard-delete <span className="font-medium text-ink-50">{pendingDelete?.displayName}</span>? Prefer toggling IsActive=false for live items.</>}
        confirmLabel="Delete"
        destructive
        onConfirm={del}
      />

      <Confirm
        open={pendingBulk !== null}
        onClose={() => pendingBulk && !bulkBusy && setPendingBulk(null)}
        title={
          pendingBulk?.action === 'delete' ? `Delete ${pendingBulk.count} items` :
          pendingBulk?.action === 'activate' ? `Activate ${pendingBulk?.count} items` :
          `Deactivate ${pendingBulk?.count} items`
        }
        body={pendingBulk?.action === 'delete'
          ? <>Hard-delete <span className="font-medium text-ink-50">{pendingBulk.count}</span> selected items? This cannot be undone.</>
          : <>Toggle <span className="font-medium text-ink-50">{pendingBulk?.count}</span> items to {pendingBulk?.action === 'activate' ? 'active' : 'inactive'}?</>}
        confirmLabel={bulkBusy ? 'Working…' : pendingBulk?.action === 'delete' ? 'Delete all' : 'Apply'}
        destructive={pendingBulk?.action === 'delete'}
        onConfirm={runBulk}
      />
    </div>
  );
}

// ── Cards / table cells ──────────────────────────────────────────────

function StoreCard({
  item, selected, onToggleSelected, onEdit, onDelete,
}: {
  item: StoreItem;
  selected: boolean;
  onToggleSelected: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <div
      className={`card !p-0 group relative flex flex-col overflow-hidden ${
        selected ? 'ring-2 ring-brand-500/60 border-brand-500/30' : ''
      }`}
    >
      {/* Selection checkbox — sits over the thumbnail. Only fully opaque
          when the item is selected OR the user is hovering the card. */}
      <label className={`absolute left-2 top-2 z-10 flex items-center justify-center rounded bg-ink-950/70 backdrop-blur-sm size-6 cursor-pointer transition-opacity ${selected ? 'opacity-100' : 'opacity-0 group-hover:opacity-100'}`}>
        <input
          type="checkbox"
          checked={selected}
          onChange={onToggleSelected}
          className="size-4 accent-brand-500"
        />
      </label>

      {/* Active/limited/expired badges — top-right of the thumbnail. */}
      <div className="absolute right-2 top-2 z-10 flex flex-col gap-1 items-end">
        <ItemBadges item={item} />
      </div>

      <button
        onClick={onEdit}
        className="block w-full aspect-square bg-ink-950/40 overflow-hidden focus:outline-none"
        title="Edit"
      >
        <Thumb imageName={item.imageName} size={256} fill />
      </button>

      <div className="px-2.5 py-2 flex flex-col gap-1">
        <div className="text-sm text-ink-50 truncate" title={item.displayName}>{item.displayName}</div>
        <div className="text-[10px] font-mono text-ink-500 truncate" title={item.slug}>{item.slug}</div>
        <div className="flex items-center justify-between mt-1">
          <span className="text-xs tabular-nums text-ink-100">
            {num(item.price)} <span className="text-ink-400">{currencyName(item.currencyType)}</span>
          </span>
          <div className="flex items-center gap-1">
            <button onClick={onEdit} className="btn-ghost text-[11px] py-0.5 px-1.5">Edit</button>
            <button onClick={onDelete} className="btn-ghost text-[11px] py-0.5 px-1.5 text-danger" title="Delete">
              <Trash />
            </button>
          </div>
        </div>
        <div className="text-[10px] text-ink-500 truncate">
          {item.storefront} · {item.category}
        </div>
      </div>
    </div>
  );
}

function ItemBadges({ item }: { item: StoreItem }) {
  const expired = item.availableUntil && Date.parse(item.availableUntil) < Date.now();
  return (
    <div className="flex flex-wrap gap-1">
      {item.isActive ? <span className="badge-online">Active</span> : <span className="badge-neutral">Inactive</span>}
      {item.isLimitedTime && <span className="badge-junior">Limited</span>}
      {expired && <span className="badge-banned">Expired</span>}
    </div>
  );
}

// ── Thumbnail ────────────────────────────────────────────────────────
// Pulls the image from img.* with the same signed URL the watch uses
// (sig=p1, optional ?width hint). Falls back to a placeholder tile
// showing the filename when the image is missing or 404s — that's
// surprisingly common with imported rooms whose CDN bytes never made
// the trip.

function Thumb({ imageName, size, fill }: { imageName: string | null; size: number; fill?: boolean }) {
  const [errored, setErrored] = useState(false);
  const url = imageName && !errored ? profileImageUrl(imageName, Math.max(size, 128)) : null;
  if (!url) {
    return (
      <div
        className={`${fill ? 'w-full h-full' : ''} flex items-center justify-center bg-gradient-to-br from-ink-900 to-ink-950 text-[10px] text-ink-500 px-2 text-center`}
        style={fill ? undefined : { width: size, height: size }}
        title={imageName ?? 'no image'}
      >
        <span className="font-mono truncate">{imageName || 'no image'}</span>
      </div>
    );
  }
  return (
    <img
      src={url}
      onError={() => setErrored(true)}
      alt={imageName ?? ''}
      loading="lazy"
      className={`${fill ? 'w-full h-full object-cover' : ''} bg-ink-950`}
      style={fill ? undefined : { width: size, height: size }}
    />
  );
}

// ── Edit / create modal ──────────────────────────────────────────────

function StoreItemForm({
  item, storefronts, onClose, onSaved,
}: {
  item: StoreItem | null;
  storefronts: StorefrontDefinition[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const isNew = item === null;
  const [form, setForm] = useState({
    slug: item?.slug ?? '',
    displayName: item?.displayName ?? '',
    description: item?.description ?? '',
    storefront: item?.storefront ?? 'main',
    category: item?.category ?? 'accessory',
    imageName: item?.imageName ?? '',
    currencyType: item?.currencyType ?? 2,
    price: item?.price ?? 100,
    isActive: item?.isActive ?? true,
    isLimitedTime: item?.isLimitedTime ?? false,
    availableUntil: item?.availableUntil ?? '',
  });
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const save = async () => {
    setBusy(true);
    try {
      const body = {
        Slug: form.slug.trim(),
        DisplayName: form.displayName.trim(),
        Description: form.description.trim(),
        Storefront: form.storefront,
        Category: form.category,
        ImageName: form.imageName.trim(),
        CurrencyType: form.currencyType,
        Price: form.price,
        IsActive: form.isActive,
        IsLimitedTime: form.isLimitedTime,
        AvailableUntil: form.availableUntil || null,
      };
      if (isNew) {
        await api('/storeitems', { method: 'POST', body });
      } else {
        await api(`/storeitems/${item!.id}`, { method: 'PUT', body });
      }
      toast.push(isNew ? 'Item created' : 'Item updated', 'success');
      onSaved();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={isNew ? 'New store item' : `Edit ${item!.slug}`}
      open
      onClose={onClose}
      size="lg"
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={save} disabled={busy || !form.slug.trim() || !form.displayName.trim()} className="btn-primary text-xs">
          {busy ? 'Saving…' : isNew ? 'Create item' : 'Save changes'}
        </button>
      </>}
    >
      <div className="grid grid-cols-1 md:grid-cols-[160px_1fr] gap-4">
        {/* Live preview anchored next to the form — pulls the image
            using the same code path the catalog grid uses, so any
            "no image" or 404 the admin sees in the cards is reproduced
            here while editing. */}
        <div className="space-y-2">
          <div className="label">Preview</div>
          <div className="aspect-square rounded-lg overflow-hidden bg-ink-950/40 border border-ink-800">
            <Thumb imageName={form.imageName.trim() || null} size={256} fill />
          </div>
          <div className="text-[10px] text-ink-500 font-mono break-all">{form.imageName || 'no imageName set'}</div>
        </div>

        <div className="space-y-3">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <Field label="Slug (unique)">
              <input value={form.slug} onChange={e => setForm({ ...form, slug: e.target.value })} disabled={!isNew} className="input font-mono text-xs" />
            </Field>
            <Field label="Display name">
              <input value={form.displayName} onChange={e => setForm({ ...form, displayName: e.target.value })} className="input" />
            </Field>
          </div>
          <Field label="Description">
            <textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={2} className="input" />
          </Field>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <Field label="Storefront">
              <select value={form.storefront} onChange={e => setForm({ ...form, storefront: e.target.value })} className="input">
                {!storefronts.some(s => s.key === form.storefront) && (
                  <option value={form.storefront}>{form.storefront}</option>
                )}
                {storefronts.map(s => <option key={s.key} value={s.key}>{storefrontLabel(s)}</option>)}
              </select>
            </Field>
            <Field label="Category">
              <select value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} className="input">
                {CATEGORIES.map(c => <option key={c}>{c}</option>)}
              </select>
            </Field>
            <Field label="Image name (filename only)">
              <input value={form.imageName} onChange={e => setForm({ ...form, imageName: e.target.value })} className="input font-mono text-xs" />
            </Field>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <Field label="Currency">
              <select value={form.currencyType} onChange={e => setForm({ ...form, currencyType: parseInt(e.target.value) })} className="input">
                <option value={1}>Tokens</option>
                <option value={2}>Coins</option>
              </select>
            </Field>
            <Field label="Price">
              <input type="number" min={0} value={form.price} onChange={e => setForm({ ...form, price: parseInt(e.target.value || '0') })} className="input" />
            </Field>
            <Field label="Available until (ISO)">
              <input type="datetime-local" value={form.availableUntil ? form.availableUntil.slice(0, 16) : ''} onChange={e => setForm({ ...form, availableUntil: e.target.value })} className="input" />
            </Field>
          </div>
          <div className="flex gap-4">
            <label className="flex items-center gap-2 text-sm text-ink-200">
              <input type="checkbox" checked={form.isActive} onChange={e => setForm({ ...form, isActive: e.target.checked })} className="size-4 accent-brand-500" />
              Active
            </label>
            <label className="flex items-center gap-2 text-sm text-ink-200">
              <input type="checkbox" checked={form.isLimitedTime} onChange={e => setForm({ ...form, isLimitedTime: e.target.checked })} className="size-4 accent-brand-500" />
              Limited time
            </label>
          </div>
          {!isNew && (
            <div className="text-xs text-ink-500">
              Created {relativeTime(item!.createdAt)} · Updated {relativeTime(item!.updatedAt)}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}

function storefrontLabel(s: StorefrontDefinition) {
  return `${s.displayName} (${s.key})`;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="label">{label}</span>
      {children}
    </label>
  );
}
