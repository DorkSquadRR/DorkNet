import { useEffect, useMemo, useState } from 'react';
import { api, get } from '../lib/api';
import type { Player, PlayerDetail } from '../lib/types';
import { absoluteTime, currencyName, num, relativeTime } from '../lib/format';
import { PageHeader } from '../components/PageHeader';
import { Modal } from '../components/Modal';
import { Confirm } from '../components/Confirm';
import { useToast } from '../components/Toast';
import { Empty } from '../components/Empty';
import { PlayerAvatar } from '../components/PlayerAvatar';
import { RefreshCw, Search } from '../components/Icons';

export function Players() {
  const [query, setQuery] = useState('');
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

  return (
    <div>
      <PageHeader
        title="Players"
        blurb="Search and moderate every account in the database."
        actions={
          <button onClick={() => search(query)} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} />
            Refresh
          </button>
        }
      />

      <div className="card overflow-hidden">
        <div className="border-b border-ink-800 p-3 flex items-center gap-2">
          <div className="relative flex-1 max-w-xs">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
            <input
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Search username or display name…"
              className="input pl-8"
            />
          </div>
          <div className="text-xs text-ink-400 ml-auto">
            {rows ? `${rows.length} match${rows.length === 1 ? '' : 'es'}` : ''}
          </div>
        </div>

        {err && <div className="px-4 py-3 text-sm text-danger border-b border-danger/30 bg-danger/5">{err}</div>}

        <div className="table-scroll">
        <table className="w-full text-sm min-w-[640px]">
          <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
            <tr>
              <th className="text-left font-medium px-4 py-2.5">Account</th>
              <th className="text-left font-medium px-4 py-2.5">Status</th>
              <th className="text-right font-medium px-4 py-2.5">Level</th>
              <th className="text-left font-medium px-4 py-2.5">Last seen</th>
              <th className="text-left font-medium px-4 py-2.5">Joined</th>
              <th />
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-800">
            {rows?.map(p => (
              <tr
                key={p.id}
                className="table-row-hover cursor-pointer"
                onClick={() => setSelectedId(p.id)}
              >
                <td className="px-4 py-2.5">
                  <div className="flex items-center gap-2.5">
                    <PlayerAvatar name={p.profileImageName} displayName={p.displayName || p.username} size={32} />
                    <div className="min-w-0">
                      <div className="font-medium text-ink-50 truncate">{p.displayName || p.username}</div>
                      <div className="text-xs text-ink-400 truncate">@{p.username} <span className="text-ink-600">·</span> #{p.id}</div>
                    </div>
                  </div>
                </td>
                <td className="px-4 py-2.5">
                  <div className="flex flex-wrap gap-1">
                    {p.online && <span className="badge-online">Online</span>}
                    {p.bannedUntil && new Date(p.bannedUntil) > new Date() && <span className="badge-banned">Banned</span>}
                    {p.isAdmin && <span className="badge-admin">Admin</span>}
                    {p.isDeveloper && <span className="badge-neutral">Dev</span>}
                    {p.isCommunityTeam && <span className="badge-neutral">Community</span>}
                    {p.isJunior && <span className="badge-junior">Junior</span>}
                    {p.isVerified && <span className="badge-neutral">Verified</span>}
                    {!p.online && !p.bannedUntil && !p.isAdmin && !p.isDeveloper && !p.isCommunityTeam && !p.isJunior && !p.isVerified && (
                      <span className="text-xs text-ink-500">—</span>
                    )}
                  </div>
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums text-ink-200">{p.level}</td>
                <td className="px-4 py-2.5 text-ink-300 text-xs" title={absoluteTime(p.lastSeenAt)}>{relativeTime(p.lastSeenAt)}</td>
                <td className="px-4 py-2.5 text-ink-300 text-xs" title={absoluteTime(p.createdAt)}>{relativeTime(p.createdAt)}</td>
                <td className="px-4 py-2.5 text-right">
                  <button className="btn-ghost text-xs">Open →</button>
                </td>
              </tr>
            ))}
            {rows && rows.length === 0 && (
              <tr><td colSpan={6}><Empty title="No matches" blurb="Try a different name or clear the filter." /></td></tr>
            )}
            {!rows && !err && (
              <tr><td colSpan={6} className="py-10 text-center text-xs text-ink-400">Loading players…</td></tr>
            )}
          </tbody>
        </table>
        </div>
      </div>

      {selectedId !== null && (
        <PlayerDetail
          id={selectedId}
          onClose={() => setSelectedId(null)}
          onChanged={() => { setSelectedId(null); search(query); }}
        />
      )}
    </div>
  );
}

// ── Detail / edit modal ───────────────────────────────────────────────

function PlayerDetail({ id, onClose, onChanged }: { id: number; onClose: () => void; onChanged: () => void }) {
  const [data, setData] = useState<PlayerDetail | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [tab, setTab] = useState<'overview' | 'mod' | 'grants' | 'profile'>('overview');

  const reload = () => {
    setErr(null);
    get<PlayerDetail>(`/players/${id}`)
      .then(setData)
      .catch(e => setErr((e as Error).message));
  };
  useEffect(reload, [id]);

  return (
    <Modal title={`Player #${id}`} open onClose={onClose} size="xl">
      {err && <div className="rounded-lg border border-danger/30 bg-danger/10 px-3 py-2 text-xs text-danger mb-3">{err}</div>}
      {!data && !err && <div className="py-10 text-center text-xs text-ink-400">Loading…</div>}
      {data && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex items-start gap-3 min-w-0">
              <PlayerAvatar name={data.profileImageName} displayName={data.displayName || data.username} size={56} />
              <div className="min-w-0">
                <div className="text-lg font-semibold text-ink-50 truncate">{data.displayName || data.username}</div>
                <div className="text-xs text-ink-400 truncate">@{data.username} · #{data.id} · {data.email ?? 'no email'}</div>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {data.online && <span className="badge-online">Online</span>}
                  {data.bannedUntil && new Date(data.bannedUntil) > new Date() && (
                    <span className="badge-banned" title={absoluteTime(data.bannedUntil)}>Banned · expires {relativeTime(data.bannedUntil)}</span>
                  )}
                  {data.isAdmin && <span className="badge-admin">Admin</span>}
                  {data.isDeveloper && <span className="badge-neutral">Developer</span>}
                  {data.isCommunityTeam && <span className="badge-neutral">Community Team</span>}
                  {data.isVerified && <span className="badge-neutral">Verified</span>}
                  {data.isJunior && <span className="badge-junior">Junior</span>}
                </div>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-2 text-xs">
              <Stat label="Level" value={num(data.level)} />
              <Stat label="XP" value={num(data.xp)} />
              <Stat label="Last IP" value={data.lastIp ?? '—'} mono />
            </div>
          </div>

          <div className="flex border-b border-ink-800 text-sm">
            {(['overview', 'mod', 'grants', 'profile'] as const).map(t => (
              <button
                key={t}
                onClick={() => setTab(t)}
                className={`px-3 py-2 -mb-px border-b-2 capitalize ${tab === t ? 'border-brand-500 text-ink-50' : 'border-transparent text-ink-300 hover:text-ink-100'}`}
              >
                {t === 'mod' ? 'Moderation' : t}
              </button>
            ))}
          </div>

          {tab === 'overview' && <OverviewTab data={data} />}
          {tab === 'mod'      && <ModerationTab data={data} onChanged={onChanged} />}
          {tab === 'grants'   && <GrantsTab    data={data} onChanged={reload} />}
          {tab === 'profile'  && <ProfileTab   data={data} onChanged={reload} />}
        </div>
      )}
    </Modal>
  );
}

function Stat({ label, value, mono }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <div className="card !p-2 text-center">
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className={`text-sm font-medium text-ink-50 ${mono ? 'font-mono text-xs' : ''}`}>{value}</div>
    </div>
  );
}

function OverviewTab({ data }: { data: PlayerDetail }) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
      <Field label="Display name" value={data.displayName} />
      <Field label="Username"     value={`@${data.username}`} />
      <Field label="Email"        value={data.email ?? '—'} />
      <Field label="Joined"       value={absoluteTime(data.createdAt)} />
      <Field label="Last seen"    value={absoluteTime(data.lastSeenAt)} />
      <Field label="Last IP"      value={data.lastIp ?? '—'} mono />
      <Field label="Bio" full value={data.bio || <span className="text-ink-500">no bio</span>} />
      <div className="card !p-3 md:col-span-2">
        <div className="text-[11px] uppercase tracking-widest text-ink-400 mb-2">Wallet</div>
        {data.balances.length === 0 ? (
          <div className="text-xs text-ink-500">No currency balances on file.</div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
            {data.balances.map(b => (
              <div key={b.currencyType} className="rounded border border-ink-800 px-3 py-2">
                <div className="text-[10px] uppercase tracking-widest text-ink-400">{currencyName(b.currencyType)}</div>
                <div className="text-sm font-medium text-ink-50 tabular-nums">{num(b.balance)}</div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function Field({ label, value, full, mono }: { label: string; value: React.ReactNode; full?: boolean; mono?: boolean }) {
  return (
    <div className={`card !p-3 ${full ? 'md:col-span-2' : ''}`}>
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className={`mt-0.5 text-sm text-ink-100 ${mono ? 'font-mono text-xs' : ''}`}>{value}</div>
    </div>
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
        {data.isAdmin && <p className="text-[11px] text-warn mt-2">Demote this admin first.</p>}
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

function ProfileTab({ data, onChanged }: { data: PlayerDetail; onChanged: () => void }) {
  const toast = useToast();
  const [displayName, setDisplayName] = useState(data.displayName);
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
          GUID in their inventory has no matching <code className="text-[11px]">outfits_assets_*.bundle</code> on disk
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
    </div>
  );
}
