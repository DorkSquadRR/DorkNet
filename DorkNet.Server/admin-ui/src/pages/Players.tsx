import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, get } from '../lib/api';
import type { Player, PlayerDetail } from '../lib/types';
import { absoluteTime, currencyName, num, relativeTime } from '../lib/format';
import { PageHeader } from '../components/PageHeader';
import { Modal } from '../components/Modal';
import { Confirm } from '../components/Confirm';
import { useToast } from '../components/Toast';
import { Empty } from '../components/Empty';
import { TabPanel, Tabs } from '../components/Tabs';
import { RefreshCw, Search } from '../components/Icons';
import { GiftPanel } from './Gift';
import { PasswordResetCard } from './Passwords';

export function Players({ embedded }: { embedded?: boolean } = {}) {
  const [params] = useSearchParams();
  const [query, setQuery] = useState(params.get('query') ?? '');
  const [rows, setRows] = useState<Player[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  // Debounced search — the watch admin sat on /api/admin/v1/players for
  // every keypress, blowing through the SQLite write lock pool. 200ms
  // is enough to feel responsive without thrashing the DB.
  const search = useMemo(() => {
    const handler = (q: string) => {
      setLoading(true);
      setErr(null);
      get<Player[]>(`/players?take=200${q ? `&query=${encodeURIComponent(q)}` : ''}`)
        .then(setRows)
        .catch(e => setErr((e as Error).message))
        .finally(() => setLoading(false));
    };
    let t: number | undefined;
    return (q: string) => {
      window.clearTimeout(t);
      t = window.setTimeout(() => handler(q), 200);
    };
  }, []);

  useEffect(() => { search(query); }, [query, search]);

  // The sidebar's "Find a player" box navigates here with ?query=.
  const urlQuery = params.get('query') ?? '';
  useEffect(() => { setQuery(urlQuery); }, [urlQuery]);

  const refreshBtn = (
    <button onClick={() => search(query)} className="btn-secondary" disabled={loading}>
      <RefreshCw className={loading ? 'animate-spin' : ''} />
      Refresh
    </button>
  );

  return (
    <div>
      {!embedded && <PageHeader title="Players" actions={refreshBtn} />}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
          <input
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Search by name or username"
            aria-label="Search players"
            className="input pl-8"
          />
        </div>
        <span className="text-[13px] text-ink-400">
          {rows ? `${rows.length}${rows.length === 200 ? '+' : ''} ${rows.length === 1 ? 'match' : 'matches'}` : ''}
        </span>
        {embedded && <div className="ml-auto">{refreshBtn}</div>}
      </div>

      {err && <div className="mb-3 rounded-md border border-danger/40 px-3 py-2 text-[13px] text-danger">{err}</div>}

      <div className="table-scroll">
        <table className="w-full min-w-[640px] text-[13.5px]">
          <thead className="border-b border-ink-800 text-xs text-ink-400">
            <tr>
              <th className="px-3 py-2 text-left font-medium">Name</th>
              <th className="px-3 py-2 text-left font-medium">Status</th>
              <th className="px-3 py-2 text-right font-medium">Level</th>
              <th className="px-3 py-2 text-left font-medium">Last seen</th>
              <th className="px-3 py-2 text-left font-medium">Joined</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-800">
            {rows?.map(p => (
              <tr
                key={p.id}
                className="table-row-hover cursor-pointer"
                onClick={() => setSelectedId(p.id)}
              >
                <td className="px-3 py-2">
                  <button
                    type="button"
                    onClick={e => { e.stopPropagation(); setSelectedId(p.id); }}
                    className="text-left font-medium text-brand-300 hover:underline"
                  >
                    {p.displayName || p.username}
                  </button>
                  <span className="ml-2 text-xs text-ink-400">@{p.username}</span>
                </td>
                <td className="px-3 py-2"><PlayerStatus p={p} /></td>
                <td className="px-3 py-2 text-right tabular-nums text-ink-200">{p.level}</td>
                <td className="whitespace-nowrap px-3 py-2 text-ink-300" title={absoluteTime(p.lastSeenAt)}>{p.online ? 'now' : relativeTime(p.lastSeenAt)}</td>
                <td className="whitespace-nowrap px-3 py-2 text-ink-300" title={absoluteTime(p.createdAt)}>{shortDate(p.createdAt)}</td>
              </tr>
            ))}
            {rows && rows.length === 0 && (
              <tr><td colSpan={5}><Empty title="No players match" blurb="Try part of the username or display name." /></td></tr>
            )}
            {!rows && !err && (
              <tr><td colSpan={5} className="py-10 text-center text-[13px] text-ink-400">Loading players…</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {selectedId !== null && (
        <Modal title="Player" open onClose={() => setSelectedId(null)} size="xl">
          <PlayerPanel
            id={selectedId}
            onChanged={() => search(query)}
            onDeleted={() => { setSelectedId(null); search(query); }}
          />
        </Modal>
      )}
    </div>
  );
}

function isBanned(p: Pick<Player, 'bannedUntil'>): boolean {
  return !!p.bannedUntil && new Date(p.bannedUntil) > new Date();
}

function shortDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isFinite(d.getTime()) ? d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : '—';
}

// Status as coloured text: the state that matters most first, then roles.
function PlayerStatus({ p }: { p: Player }) {
  const parts: React.ReactNode[] = [];
  if (isBanned(p)) parts.push(<span key="ban" className="text-danger" title={absoluteTime(p.bannedUntil)}>Banned</span>);
  else if (p.online) parts.push(<span key="on" className="text-success">Online</span>);
  else parts.push(<span key="off" className="text-ink-400">Offline</span>);
  if (p.isAdmin) parts.push(<span key="admin" className="text-brand-300">Admin</span>);
  if (p.isDeveloper) parts.push(<span key="dev" className="text-ink-300">Developer</span>);
  if (p.isCommunityTeam) parts.push(<span key="ct" className="text-ink-300">Community team</span>);
  if (p.isJunior) parts.push(<span key="jr" className="text-warn">Junior</span>);
  return <span className="inline-flex flex-wrap gap-x-2.5">{parts}</span>;
}

// ── Player panel ──────────────────────────────────────────────────────
// Everything about one account: identity, status, quick actions and the
// moderation / grants / gift / profile tools. Rendered inline on the Live
// page and inside a dialog on the Players directory.

type PanelTab = 'overview' | 'mod' | 'grants' | 'gift' | 'profile';

export function PlayerPanel({ id, whereabouts, onChanged, onDeleted }: {
  id: number;
  // Where the player is right now, if the caller knows (the Live page does).
  whereabouts?: React.ReactNode;
  onChanged?: () => void;
  onDeleted?: () => void;
}) {
  const toast = useToast();
  const [data, setData] = useState<PlayerDetail | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [tab, setTab] = useState<PanelTab>('overview');
  const [kickOpen, setKickOpen] = useState(false);
  const idBase = useId();

  // Responses are only applied while their player is still the one shown,
  // so a slow answer for a previous selection can never land under (and
  // aim the panel's actions at) the current one.
  const currentId = useRef(id);
  currentId.current = id;

  const reload = () => {
    const forId = id;
    setErr(null);
    get<PlayerDetail>(`/players/${forId}`)
      .then(d => { if (currentId.current === forId) setData(d); })
      .catch(e => { if (currentId.current === forId) setErr((e as Error).message); });
  };
  // A new player resets the panel: overview tab, no open confirmation.
  useEffect(() => {
    setData(null);
    setTab('overview');
    setKickOpen(false);
    reload();
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const changed = () => { reload(); onChanged?.(); };

  if (err) return <div className="rounded-md border border-danger/40 px-3 py-2 text-[13px] text-danger">{err}</div>;
  if (!data) return <div className="py-10 text-center text-[13px] text-ink-400">Loading…</div>;

  const banned = isBanned(data);
  const roles = [
    data.isAdmin && 'Admin',
    data.isDeveloper && 'Developer',
    data.isCommunityTeam && 'Community team',
    data.isVerified && 'Verified',
    data.isJunior && 'Junior',
  ].filter(Boolean) as string[];

  return (
    <div>
      <h1 className="text-[22px] font-semibold tracking-tight text-ink-50">{data.displayName || data.username}</h1>
      <div className="mt-0.5 flex flex-wrap gap-x-4 gap-y-0.5 text-[13px] text-ink-400">
        <span>@{data.username}</span>
        <span>#{data.id}</span>
        <span>Level {data.level}</span>
        <span>Joined {shortDate(data.createdAt)}</span>
        {roles.length > 0 && <span className="text-ink-300">{roles.join(', ')}</span>}
      </div>
      <div className="mt-2 text-[13.5px]">
        {banned ? (
          <><span className="text-danger">Banned</span> <span className="text-ink-300">until {absoluteTime(data.bannedUntil)}</span></>
        ) : data.online ? (
          <><span className="text-success">Online</span>{whereabouts && <span className="text-ink-300"> {whereabouts}</span>}</>
        ) : (
          <><span className="text-ink-400">Offline</span><span className="text-ink-300">, last seen {relativeTime(data.lastSeenAt)}</span></>
        )}
      </div>

      <div className="mt-3 grid grid-cols-2 gap-1.5 sm:flex sm:flex-wrap">
        <button onClick={() => setKickOpen(true)} className="btn-secondary py-2 sm:py-1.5" disabled={!data.online}>Kick</button>
        <button onClick={() => setTab('mod')} className="btn-secondary py-2 text-danger sm:py-1.5">{banned ? 'Unban' : 'Ban'}</button>
        <button onClick={() => setTab('gift')} className="btn-secondary py-2 sm:py-1.5">Send gift</button>
        <button onClick={() => setTab('grants')} className="btn-secondary py-2 sm:py-1.5">Grant</button>
      </div>

      <Tabs
        className="mt-5"
        tabs={[
          { key: 'overview', label: 'Overview' },
          { key: 'mod', label: 'Moderation' },
          { key: 'grants', label: 'Grants' },
          { key: 'gift', label: 'Gift' },
          { key: 'profile', label: 'Profile' },
        ]}
        active={tab}
        onChange={setTab}
        idBase={idBase}
      />

      <TabPanel idBase={idBase} active={tab}>
        {tab === 'overview' && <OverviewTab data={data} />}
        {tab === 'mod'      && <ModerationTab data={data} onChanged={changed} />}
        {tab === 'grants'   && <GrantsTab    data={data} onChanged={changed} />}
        {tab === 'gift'     && <GiftPanel    playerId={data.id} />}
        {tab === 'profile'  && <ProfileTab   data={data} onChanged={changed} onDeleted={() => onDeleted?.()} />}
      </TabPanel>

      <Confirm
        open={kickOpen}
        onClose={() => setKickOpen(false)}
        title="Kick player"
        body={`Boot @${data.username} from their current session? They can reconnect straight away.`}
        confirmLabel="Kick"
        onConfirm={async () => {
          try {
            await api(`/players/${data.id}/kick`, { method: 'POST', body: { Reason: '' } });
            toast.push(`Kicked @${data.username}`, 'success');
            changed();
          } catch (e) {
            toast.push((e as Error).message, 'error');
          }
        }}
      />
    </div>
  );
}

function OverviewTab({ data }: { data: PlayerDetail }) {
  return (
    <dl className="grid grid-cols-[110px_minmax(0,1fr)] gap-x-4 gap-y-2 text-[13.5px] sm:grid-cols-[130px_minmax(0,1fr)]">
      <Field label="Display name" value={data.displayName} />
      <Field label="Username" value={`@${data.username}`} />
      <Field label="Email" value={data.email ?? <span className="text-ink-500">none</span>} />
      <Field label="Joined" value={absoluteTime(data.createdAt)} />
      <Field label="Last seen" value={absoluteTime(data.lastSeenAt)} />
      <Field label="XP" value={num(data.xp)} />
      <Field label="Last IP" value={data.lastIp ?? '—'} mono />
      <Field label="Wallet" value={
        data.balances.length === 0
          ? <span className="text-ink-500">no balances</span>
          : <span className="flex flex-wrap gap-x-4">{data.balances.map(b => (
              <span key={b.currencyType}><span className="tabular-nums">{num(b.balance)}</span> <span className="text-ink-400">{currencyName(b.currencyType)}</span></span>
            ))}</span>
      } />
      <Field label="Bio" value={data.bio || <span className="text-ink-500">no bio</span>} />
    </dl>
  );
}

function Field({ label, value, mono }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <>
      <dt className="text-ink-400">{label}</dt>
      <dd className={`min-w-0 break-words text-ink-100 ${mono ? 'font-mono text-xs leading-5' : ''}`}>{value}</dd>
    </>
  );
}

// ── Moderation actions: ban, unban, kick, promote, demote ────────────

function ModerationTab({ data, onChanged }: { data: PlayerDetail; onChanged: () => void }) {
  const toast = useToast();
  const [banDays, setBanDays] = useState(7);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmKey, setConfirmKey] = useState<null | 'ban' | 'unban' | 'kick' | 'promote' | 'demote'>(null);

  const run = async (label: string, fn: () => Promise<unknown>) => {
    setBusy(label);
    try {
      await fn();
      toast.push(`${label} succeeded`, 'success');
      onChanged();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const banned = data.bannedUntil && new Date(data.bannedUntil) > new Date();

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Ban / unban</h3>
        <p className="text-xs text-ink-400 mb-3">
          {banned ? `Currently banned until ${absoluteTime(data.bannedUntil)}.` : 'Temporary ban with optional reason.'}
        </p>
        <div className="flex items-center gap-2 mb-2">
          <input type="number" min={1} max={3650} value={banDays} onChange={e => setBanDays(parseInt(e.target.value || '1'))} className="input w-24" />
          <span className="text-xs text-ink-400">days</span>
        </div>
        <input value={reason} onChange={e => setReason(e.target.value)} placeholder="Reason (optional, logged)" className="input mb-2 text-xs" />
        <div className="flex gap-2">
          <button onClick={() => setConfirmKey('ban')} className="btn-danger text-xs" disabled={!!busy || data.isAdmin}>
            {busy === 'Ban' ? 'Banning…' : 'Ban player'}
          </button>
          {banned && (
            <button onClick={() => setConfirmKey('unban')} className="btn-secondary text-xs" disabled={!!busy}>
              Unban
            </button>
          )}
        </div>
        {data.isAdmin && <p className="text-xs text-warn mt-2">Demote this admin first.</p>}
      </div>

      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Kick</h3>
        <p className="text-xs text-ink-400 mb-3">Sends a ModerationKick push so the watch boots out of its current session. The player can reconnect immediately.</p>
        <input value={reason} onChange={e => setReason(e.target.value)} placeholder="Reason (optional)" className="input mb-2 text-xs" />
        <button onClick={() => setConfirmKey('kick')} className="btn-secondary text-xs" disabled={!!busy}>Kick player</button>
      </div>

      <div className="card !p-4 md:col-span-2">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Admin role</h3>
        <p className="text-xs text-ink-400 mb-3">
          Admins bypass the ban check and can use this console. Don't demote yourself — there's a server-side guard but it's safer to have another admin do it.
        </p>
        <div className="flex gap-2">
          {!data.isAdmin && (
            <button onClick={() => setConfirmKey('promote')} className="btn-primary text-xs" disabled={!!busy}>Promote to admin</button>
          )}
          {data.isAdmin && (
            <button onClick={() => setConfirmKey('demote')} className="btn-danger text-xs" disabled={!!busy}>Revoke admin</button>
          )}
        </div>
      </div>

      <Confirm
        open={confirmKey === 'ban'}
        onClose={() => setConfirmKey(null)}
        title="Ban player"
        body={`Ban @${data.username} for ${banDays} day${banDays === 1 ? '' : 's'}?`}
        confirmLabel="Ban"
        destructive
        onConfirm={() => run('Ban', () => api(`/players/${data.id}/ban`, { method: 'POST', body: { DurationDays: banDays, Reason: reason } }))}
      />
      <Confirm
        open={confirmKey === 'unban'}
        onClose={() => setConfirmKey(null)}
        title="Unban player"
        body={`Lift the ban on @${data.username}?`}
        confirmLabel="Unban"
        onConfirm={() => run('Unban', () => api(`/players/${data.id}/unban`, { method: 'POST', body: { Reason: reason } }))}
      />
      <Confirm
        open={confirmKey === 'kick'}
        onClose={() => setConfirmKey(null)}
        title="Kick player"
        body={`Boot @${data.username} from their current session?`}
        confirmLabel="Kick"
        onConfirm={() => run('Kick', () => api(`/players/${data.id}/kick`, { method: 'POST', body: { Reason: reason } }))}
      />
      <Confirm
        open={confirmKey === 'promote'}
        onClose={() => setConfirmKey(null)}
        title="Promote to admin"
        body={`Grant admin privileges to @${data.username}? They'll be able to use this console.`}
        confirmLabel="Promote"
        onConfirm={() => run('Promote', () => api(`/players/${data.id}/promote`, { method: 'POST' }))}
      />
      <Confirm
        open={confirmKey === 'demote'}
        onClose={() => setConfirmKey(null)}
        title="Revoke admin"
        body={`Strip admin privileges from @${data.username}?`}
        confirmLabel="Demote"
        destructive
        onConfirm={() => run('Demote', () => api(`/players/${data.id}/demote`, { method: 'POST' }))}
      />
    </div>
  );
}

// ── Grants: currency, XP, item, displayName, flags ───────────────────

function GrantsTab({ data, onChanged }: { data: PlayerDetail; onChanged: () => void }) {
  const toast = useToast();
  const run = async (label: string, fn: () => Promise<unknown>) => {
    try {
      await fn();
      toast.push(`${label} applied`, 'success');
      onChanged();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const [currType, setCurrType] = useState(2);
  const [currAmount, setCurrAmount] = useState(1000);
  const [xp, setXp] = useState(1000);
  const [targetLevel, setTargetLevel] = useState(data.level);
  const [itemId, setItemId] = useState('');
  const [itemQty, setItemQty] = useState(1);

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Adjust currency</h3>
        <p className="text-xs text-ink-400 mb-3">Positive grants, negative deducts (clamped to zero).</p>
        <div className="flex gap-2 mb-2">
          <select value={currType} onChange={e => setCurrType(parseInt(e.target.value))} className="input w-32">
            <option value={1}>Tokens</option>
            <option value={2}>Coins</option>
          </select>
          <input type="number" value={currAmount} onChange={e => setCurrAmount(parseInt(e.target.value || '0'))} className="input flex-1" />
        </div>
        <button
          onClick={() => run('Currency', () => api(`/players/${data.id}/currency`, { method: 'POST', body: { CurrencyType: currType, Amount: currAmount, Reason: 'admin_grant' } }))}
          className="btn-primary text-xs"
        >Apply</button>
      </div>

      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Grant XP</h3>
        <p className="text-xs text-ink-400 mb-3">May trigger level-up rewards.</p>
        <div className="flex gap-2 mb-2">
          <input type="number" value={xp} onChange={e => setXp(parseInt(e.target.value || '0'))} className="input" />
        </div>
        <button
          onClick={() => run('XP', () => api(`/players/${data.id}/xp`, { method: 'POST', body: { Amount: xp, Reason: 'admin_grant' } }))}
          className="btn-primary text-xs"
        >Grant XP</button>
      </div>

      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Set level</h3>
        <p className="text-xs text-ink-400 mb-3">Moves XP to the floor for that level.</p>
        <div className="flex gap-2 mb-2">
          <input
            type="number"
            min={1}
            max={100}
            value={targetLevel}
            onChange={e => setTargetLevel(parseInt(e.target.value || '1'))}
            className="input"
          />
        </div>
        <button
          onClick={() => run('Level', () => api(`/players/${data.id}/level`, { method: 'POST', body: { Level: targetLevel } }))}
          disabled={!Number.isFinite(targetLevel) || targetLevel < 1 || targetLevel === data.level}
          className="btn-primary text-xs"
        >Set level</button>
      </div>

      <div className="card !p-4 md:col-span-2">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Unlock cheer badges</h3>
        <p className="text-xs text-ink-400 mb-3">
          Drops one cheer of each category (General, Helpful, Great Host, Sportsman, Creative, Credit) into the player's received-cheer list so they can pin any badge from the watch's Selected Cheer picker. Idempotent — re-running only fills in categories they don't already have. No XP is awarded.
        </p>
        <button
          onClick={() => run('Cheers', () => api(`/players/${data.id}/cheers/unlock`, { method: 'POST' }))}
          className="btn-primary text-xs"
        >Unlock all categories</button>
      </div>

      <div className="card !p-4 md:col-span-2">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Unlock all items</h3>
        <p className="text-xs text-ink-400 mb-3">
          Grants the player the entire catalog: every avatar cosmetic, every permanent hair dye, and every store equipment/consumable. Idempotent — only adds what's missing. The watch's wardrobe only displays up to ~120 owned cosmetics at once (hard client cap), but all are granted and store items show as owned.
        </p>
        <button
          onClick={() => run('Unlock all', () => api(`/players/${data.id}/inventory/unlock-all`, { method: 'POST' }))}
          className="btn-primary text-xs"
        >Unlock everything</button>
      </div>

      <div className="card !p-4 md:col-span-2">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Grant inventory item</h3>
        <p className="text-xs text-ink-400 mb-3">Appends an item GUID to the player's inventory JSON. Use the Store catalog to find slugs / GUIDs.</p>
        <div className="grid grid-cols-1 sm:grid-cols-[1fr,120px,auto] gap-2">
          <input value={itemId} onChange={e => setItemId(e.target.value)} placeholder="item GUID" className="input font-mono text-xs" />
          <input type="number" min={1} max={1000} value={itemQty} onChange={e => setItemQty(parseInt(e.target.value || '1'))} className="input" />
          <button
            onClick={() => run('Item', () => api(`/players/${data.id}/inventory/grant`, { method: 'POST', body: { ItemId: itemId.trim(), Quantity: itemQty } }))}
            disabled={!itemId.trim()}
            className="btn-primary text-xs"
          >Grant</button>
        </div>
      </div>
    </div>
  );
}

// ── Profile: display name + role flags ───────────────────────────────

function ProfileTab({ data, onChanged, onDeleted }: { data: PlayerDetail; onChanged: () => void; onDeleted: () => void }) {
  const toast = useToast();
  const [displayName, setDisplayName] = useState(data.displayName);
  const [username, setUsername] = useState(data.username);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [flags, setFlags] = useState({
    isVerified: data.isVerified,
    isDeveloper: data.isDeveloper,
    isCommunityTeam: data.isCommunityTeam,
    isJunior: data.isJunior,
  });

  const save = async (label: string, fn: () => Promise<unknown>) => {
    try {
      await fn();
      toast.push(`${label} saved`, 'success');
      onChanged();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Username</h3>
        <p className="text-xs text-ink-400 mb-3">The unique @handle. 2–24 chars: letters, numbers, _ or -. Changing it doesn't touch the display name.</p>
        <div className="flex gap-2">
          <input value={username} onChange={e => setUsername(e.target.value)} className="input flex-1 font-mono text-xs" />
          <button
            onClick={() => save('Username', () => api(`/players/${data.id}/username`, { method: 'POST', body: { Username: username.trim() } }))}
            disabled={!username.trim() || username.trim() === data.username}
            className="btn-primary text-xs"
          >Save</button>
        </div>
      </div>

      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Display name</h3>
        <p className="text-xs text-ink-400 mb-3">Force-override the player's display name.</p>
        <div className="flex gap-2">
          <input value={displayName} onChange={e => setDisplayName(e.target.value)} className="input flex-1" />
          <button
            onClick={() => save('Display name', () => api(`/players/${data.id}/displayName`, { method: 'POST', body: { DisplayName: displayName } }))}
            disabled={!displayName.trim() || displayName === data.displayName}
            className="btn-primary text-xs"
          >Save</button>
        </div>
      </div>

      <div className="card !p-4">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Profile flags</h3>
        <p className="text-xs text-ink-400 mb-3">
          Verified = blue check on profile. Developer or Community Team unlocks the watch's overhead-badge slider — once unlocked, the player picks "Developer" or "Community Team" from their in-watch settings and the label renders above their head for everyone in the room.
        </p>
        <div className="flex flex-col gap-2 mb-3">
          {([
            ['isVerified', 'Verified'],
            ['isDeveloper', 'Developer'],
            ['isCommunityTeam', 'Community Team'],
            ['isJunior', 'Junior'],
          ] as const).map(([k, label]) => (
            <label key={k} className="flex items-center gap-2 text-sm text-ink-200">
              <input
                type="checkbox"
                checked={flags[k]}
                onChange={e => setFlags(f => ({ ...f, [k]: e.target.checked }))}
                className="size-4 accent-brand-500"
              />
              {label}
            </label>
          ))}
        </div>
        <button
          onClick={() => save('Flags', () => api(`/players/${data.id}/flags`, { method: 'POST', body: { IsVerified: flags.isVerified, IsDeveloper: flags.isDeveloper, IsCommunityTeam: flags.isCommunityTeam, IsJunior: flags.isJunior } }))}
          className="btn-primary text-xs"
        >Save flags</button>
      </div>

      <div className="card !p-4 md:col-span-2">
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Reset avatar</h3>
        <p className="text-xs text-ink-400 mb-3">
          Wipes the player's equipped outfit and inventory back to the 2020 starter set.
          Use this when the watch crashes loading the avatar — usually means an item
          GUID in their inventory has no matching <code className="text-xs">outfits_assets_*.bundle</code> on disk
          (the build can't load it and Unity's Addressables tears down the local player).
        </p>
        <button
          onClick={() => {
            if (!confirm(`Reset @${data.username}'s avatar to the starter outfit and inventory? Existing wardrobe items will be removed.`)) return;
            save('Avatar reset', () => api(`/players/${data.id}/avatar/reset`, { method: 'POST' }));
          }}
          className="btn-danger text-xs"
        >Reset to starter</button>
      </div>

      <PasswordResetCard playerId={data.id} username={data.username} />

      <div className="card !p-4 md:col-span-2 border-danger/40 bg-danger/5">
        <h3 className="text-sm font-semibold text-danger mb-1">Remove account</h3>
        <p className="text-xs text-ink-300 mb-3">
          Deletes the account and personal rows. Rooms, inventions, clubs, and other durable authored content are reassigned to the system account.
        </p>
        <button
          onClick={() => setDeleteOpen(true)}
          className="btn-danger text-xs"
          disabled={data.isAdmin || data.id === 1}
        >Remove account</button>
        {data.isAdmin && <p className="text-xs text-warn mt-2">Demote this admin before removing the account.</p>}
        {data.id === 1 && <p className="text-xs text-warn mt-2">The system account cannot be removed.</p>}
      </div>

      <DeleteAccountModal
        open={deleteOpen}
        data={data}
        onClose={() => setDeleteOpen(false)}
        onDeleted={onDeleted}
      />
    </div>
  );
}

function DeleteAccountModal({ open, data, onClose, onDeleted }: {
  open: boolean;
  data: PlayerDetail;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const toast = useToast();
  const [confirmUsername, setConfirmUsername] = useState('');
  const [confirmPhrase, setConfirmPhrase] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const expectedPhrase = `DELETE ${data.id}`;
  const valid = confirmUsername.trim() === data.username && confirmPhrase.trim() === expectedPhrase;

  const remove = async () => {
    if (!valid) return;
    setBusy(true);
    try {
      await api(`/players/${data.id}`, {
        method: 'DELETE',
        body: {
          ConfirmUsername: confirmUsername.trim(),
          ConfirmPhrase: confirmPhrase.trim(),
          Reason: reason.trim() || null,
        },
      });
      toast.push(`@${data.username} removed`, 'success');
      onClose();
      onDeleted();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title="Remove account"
      open={open}
      onClose={onClose}
      size="sm"
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={remove} className="btn-danger text-xs" disabled={busy || !valid}>
          {busy ? 'Removing…' : 'Remove account'}
        </button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <p className="text-ink-200">
          This removes <span className="font-mono">@{data.username}</span> and cannot be undone from the admin UI.
        </p>
        <label className="block">
          <span className="block text-xs text-ink-400 mb-1">Type username</span>
          <input
            value={confirmUsername}
            onChange={e => setConfirmUsername(e.target.value)}
            placeholder={data.username}
            className="input font-mono text-xs"
          />
        </label>
        <label className="block">
          <span className="block text-xs text-ink-400 mb-1">Type confirmation phrase</span>
          <input
            value={confirmPhrase}
            onChange={e => setConfirmPhrase(e.target.value)}
            placeholder={expectedPhrase}
            className="input font-mono text-xs"
          />
        </label>
        <label className="block">
          <span className="block text-xs text-ink-400 mb-1">Reason</span>
          <input
            value={reason}
            onChange={e => setReason(e.target.value)}
            placeholder="Optional audit note"
            className="input text-xs"
          />
        </label>
      </div>
    </Modal>
  );
}
