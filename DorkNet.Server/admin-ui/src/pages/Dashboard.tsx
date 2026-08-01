import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, get } from '../lib/api';
import type { Stats } from '../lib/types';
import { absoluteTime, num, relativeTime } from '../lib/format';
import { PlayerAvatar } from '../components/PlayerAvatar';
import { useToast } from '../components/Toast';
import { Activity, Ban, Building2, Gift, Megaphone, RefreshCw, Users } from '../components/Icons';

type ActionState = { playerId: number; username: string; kind: 'kick' | 'ban' } | null;

export function Dashboard() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [action, setAction] = useState<ActionState>(null);
  const toast = useToast();

  const load = async () => {
    setLoading(true);
    setErr(null);
    try {
      setStats(await get<Stats>('/stats'));
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    const id = setInterval(load, 10_000);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const runAction = async (reason: string) => {
    if (!action) return;
    try {
      if (action.kind === 'kick') {
        await api(`/players/${action.playerId}/kick`, { method: 'POST', body: { Reason: reason } });
        toast.push(`Kicked @${action.username}`, 'success');
      } else {
        await api(`/players/${action.playerId}/ban`, { method: 'POST', body: { DurationDays: 1, Reason: reason || 'Quick ban from live dashboard' } });
        toast.push(`Banned @${action.username} for 1 day`, 'success');
      }
      setAction(null);
      await load();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const onlinePlayers = stats?.players.online ?? [];

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-ink-50">Live Ops</h1>
          <p className="text-sm text-ink-400">Online players, active rooms, and server health.</p>
        </div>
        <div className="flex gap-2">
          <Link to="/broadcast" className="btn-secondary text-xs"><Megaphone /> Broadcast</Link>
          <button onClick={load} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} />
            Refresh
          </button>
        </div>
      </div>

      {err && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>}

      <section className="grid grid-cols-2 xl:grid-cols-6 gap-3">
        <Stat icon={<Users />} label="Online" value={stats?.players.onlineNow} sub={`${stats?.players.total ?? 0} accounts`} tone="success" />
        <Stat icon={<Activity />} label="In game" value={stats?.rooms.inGamePlayerCount} sub={`${stats?.rooms.activeSessionCount ?? 0} sessions`} tone="brand" />
        <Stat icon={<Building2 />} label="Rooms" value={stats?.rooms.total} sub={`${num(stats?.rooms.totalVisits ?? 0)} visits`} tone="neutral" />
        <Stat icon={<Gift />} label="Inventions" value={stats?.inventions} sub={`${num(stats?.rooms.totalCheers ?? 0)} room cheers`} tone="neutral" />
        <Stat icon={<Users />} label="New today" value={stats?.players.newToday} sub={`${stats?.photos.today ?? 0} photos`} tone="brand" />
        <Stat icon={<Ban />} label="Moderation" value={stats?.moderation.openReports} sub={`${stats?.players.bannedNow ?? 0} banned`} tone="warn" />
      </section>

      <section className="grid grid-cols-1 2xl:grid-cols-[minmax(0,1fr)_380px] gap-4">
        <div className="card overflow-hidden">
          <div className="border-b border-ink-800 px-4 py-3 flex flex-wrap items-center gap-2">
            <div>
              <h2 className="text-sm font-semibold text-ink-50">Online players</h2>
              <p className="text-xs text-ink-400">{onlinePlayers.length} connected right now</p>
            </div>
            <Link to="/players" className="btn-ghost text-xs ml-auto">All players</Link>
          </div>
          <div className="table-scroll">
            <table className="w-full text-sm min-w-[900px]">
              <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/60 border-b border-ink-800">
                <tr>
                  <th className="text-left font-medium px-4 py-2.5">Player</th>
                  <th className="text-left font-medium px-4 py-2.5">Room</th>
                  <th className="text-left font-medium px-4 py-2.5">Instance</th>
                  <th className="text-right font-medium px-4 py-2.5">Level</th>
                  <th className="text-left font-medium px-4 py-2.5">Seen</th>
                  <th className="text-right font-medium px-4 py-2.5">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-800">
                {onlinePlayers.map(p => (
                  <tr key={p.id} className="table-row-hover">
                    <td className="px-4 py-2.5">
                      <div className="flex items-center gap-2.5 min-w-0">
                        <PlayerAvatar name={p.profileImageName} displayName={p.displayName || p.username} size={34} />
                        <div className="min-w-0">
                          <div className="font-medium text-ink-50 truncate">{p.displayName || p.username}</div>
                          <div className="text-xs text-ink-400 truncate">@{p.username} <span className="text-ink-600">·</span> #{p.id}</div>
                        </div>
                        <div className="flex gap-1 ml-1">
                          {p.isAdmin && <span className="badge-admin">Admin</span>}
                          {p.isJunior && <span className="badge-junior">Junior</span>}
                          {p.isVerified && <span className="badge-neutral">Verified</span>}
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-2.5">
                      {p.currentRoom ? (
                        <Link to={`/rooms/${p.currentRoom.roomId}`} className="text-ink-100 hover:text-brand-200">
                          {p.currentRoom.name}
                        </Link>
                      ) : (
                        <span className="text-ink-500">Unknown</span>
                      )}
                      {p.currentRoom?.isPrivate && <span className="badge-neutral ml-2">Private</span>}
                    </td>
                    <td className="px-4 py-2.5 font-mono text-xs text-ink-300">
                      {p.currentRoom ? (
                        <span title={p.currentRoom.photonRoomId}>
                          #{p.currentRoom.roomInstanceId} · {p.currentRoom.photonRegionId}
                        </span>
                      ) : '—'}
                    </td>
                    <td className="px-4 py-2.5 text-right tabular-nums text-ink-200">{p.level}</td>
                    <td className="px-4 py-2.5 text-xs text-ink-300" title={absoluteTime(p.lastSeenAt)}>
                      {relativeTime(p.lastSeenAt)}
                    </td>
                    <td className="px-4 py-2.5">
                      <div className="flex justify-end gap-1.5">
                        <Link to={`/players?query=${encodeURIComponent(p.username)}`} className="btn-ghost text-xs">Open</Link>
                        <Link to={`/gift?player=${p.id}`} className="btn-ghost text-xs">Gift</Link>
                        <button onClick={() => setAction({ playerId: p.id, username: p.username, kind: 'kick' })} className="btn-secondary text-xs">Kick</button>
                        {!p.isAdmin && <button onClick={() => setAction({ playerId: p.id, username: p.username, kind: 'ban' })} className="btn-danger text-xs">Ban 1d</button>}
                      </div>
                    </td>
                  </tr>
                ))}
                {stats && onlinePlayers.length === 0 && (
                  <tr><td colSpan={6} className="py-10 text-center text-xs text-ink-400">No online players.</td></tr>
                )}
                {!stats && !err && (
                  <tr><td colSpan={6} className="py-10 text-center text-xs text-ink-400">Loading live players...</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>

        <div className="space-y-4">
          <Panel title="Active sessions" subtitle={`${stats?.rooms.activeSessionCount ?? 0} open`}>
            <div className="space-y-2">
              {stats?.rooms.activeSessions.map(s => (
                <div key={s.id} className="rounded-lg border border-ink-800 bg-ink-950/40 p-3">
                  <div className="flex items-center justify-between gap-2">
                    <div className="font-mono text-xs text-ink-200 truncate">{s.photonRoomName || `session-${s.id}`}</div>
                    <span className="badge-online">{s.playerCount}/{s.maxCapacity}</span>
                  </div>
                  <div className="mt-1 text-[11px] text-ink-500">
                    room {s.roomId || 'unknown'} · {s.region} · {relativeTime(s.createdAt)}
                  </div>
                </div>
              ))}
              {stats && stats.rooms.activeSessions.length === 0 && <div className="text-xs text-ink-500 py-4">No active sessions.</div>}
            </div>
          </Panel>

          <Panel title="Top rooms" subtitle="By visits">
            <div className="space-y-2">
              {stats?.rooms.topByVisits.slice(0, 6).map((r, idx) => (
                <Link key={r.id} to={`/rooms/${r.id}`} className="block rounded-lg border border-ink-800 bg-ink-950/40 px-3 py-2 hover:bg-ink-800/60">
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-ink-500 w-5 tabular-nums">{idx + 1}</span>
                    <span className="text-sm text-ink-100 truncate flex-1">{r.name}</span>
                    <span className="text-xs text-ink-300 tabular-nums">{num(r.visitCount)}</span>
                  </div>
                </Link>
              ))}
            </div>
          </Panel>
        </div>
      </section>

      <p className="text-[11px] text-ink-500">Server time: {stats ? new Date(stats.serverTime).toLocaleString() : '-'}</p>

      {action && (
        <QuickAction
          action={action}
          onCancel={() => setAction(null)}
          onConfirm={runAction}
        />
      )}
    </div>
  );
}

function Stat({ label, value, sub, icon, tone }: { label: string; value: number | undefined; sub?: string; icon: React.ReactNode; tone: 'success' | 'warn' | 'brand' | 'neutral' }) {
  const toneClass = {
    success: 'text-success bg-success/10 border-success/25',
    warn: 'text-warn bg-warn/10 border-warn/25',
    brand: 'text-brand-200 bg-brand-500/10 border-brand-500/25',
    neutral: 'text-ink-200 bg-ink-800/60 border-ink-700',
  }[tone];
  return (
    <div className="card !rounded-lg p-4">
      <div className="flex items-start justify-between gap-2">
        <div className={`rounded-md border p-2 ${toneClass}`}>{icon}</div>
        <div className="text-right">
          <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
          <div className="mt-1 text-2xl font-semibold tabular-nums text-ink-50">{value?.toLocaleString() ?? '-'}</div>
        </div>
      </div>
      {sub && <div className="mt-2 text-xs text-ink-400 truncate">{sub}</div>}
    </div>
  );
}

function Panel({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <div className="card !rounded-lg overflow-hidden">
      <div className="border-b border-ink-800 px-4 py-3">
        <h2 className="text-sm font-semibold text-ink-50">{title}</h2>
        <p className="text-xs text-ink-400">{subtitle}</p>
      </div>
      <div className="p-3">{children}</div>
    </div>
  );
}

function QuickAction({ action, onCancel, onConfirm }: { action: Exclude<ActionState, null>; onCancel: () => void; onConfirm: (reason: string) => void }) {
  const [reason, setReason] = useState(action.kind === 'kick' ? 'Quick kick from live dashboard' : 'Quick ban from live dashboard');
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-ink-950/70 px-4">
      <div className="card !rounded-lg w-full max-w-md p-4">
        <h2 className="text-sm font-semibold text-ink-50">
          {action.kind === 'kick' ? 'Kick player' : 'Ban player for 1 day'}
        </h2>
        <p className="mt-1 text-xs text-ink-400">@{action.username}</p>
        <label className="label block mt-4">Reason</label>
        <textarea value={reason} onChange={e => setReason(e.target.value)} rows={3} className="input mt-1.5" />
        <div className="mt-4 flex justify-end gap-2">
          <button onClick={onCancel} className="btn-ghost text-xs">Cancel</button>
          <button onClick={() => onConfirm(reason)} className={action.kind === 'kick' ? 'btn-secondary text-xs' : 'btn-danger text-xs'}>
            {action.kind === 'kick' ? 'Kick' : 'Ban 1d'}
          </button>
        </div>
      </div>
    </div>
  );
}
