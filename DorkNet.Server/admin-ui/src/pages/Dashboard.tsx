import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router';
import { get } from '../lib/api';
import type { Stats } from '../lib/types';
import { absoluteTime, relativeTime } from '../lib/format';
import { ArrowLeft, RefreshCw } from '../components/Icons';
import { PlayerPanel } from './Players';

type Online = Stats['players']['online'][number];

interface RoomGroup {
  key: string;
  room: Online['currentRoom'];
  players: Online[];
}

// Live: who is online right now, grouped by the room instance they're in,
// with the selected player's account and moderation tools beside the list.
// The selection lives in ?player= so a phone can show the list and the
// player as two screens with working back navigation.
export function Dashboard() {
  const [stats, setStats] = useState<Stats | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const selected = Number(params.get('player')) || null;

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setStats(await get<Stats>('/stats'));
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    const id = window.setInterval(load, 10_000);
    return () => window.clearInterval(id);
  }, [load]);

  // Opening a player from the list adds one history entry (so a phone's
  // back button returns to the list); switching between players replaces it.
  const select = (id: number | null) => {
    const next = new URLSearchParams(params);
    if (id === null) next.delete('player');
    else next.set('player', String(id));
    setParams(next, { replace: selected !== null, state: id !== null && selected === null ? { fromList: true } : undefined });
  };
  const back = () => {
    if ((location.state as { fromList?: boolean } | null)?.fromList) navigate(-1);
    else select(null);
  };

  const online = stats?.players.online ?? [];
  const groups = useMemo(() => groupByRoom(online), [online]);
  const inRooms = groups.filter(g => g.room).length;
  const selectedOnline = online.find(p => p.id === selected);

  return (
    <div className="grid h-full grid-rows-[minmax(0,1fr)] md:grid-cols-[340px_minmax(0,1fr)] lg:grid-cols-[360px_minmax(0,1fr)]">
      {/* Player list */}
      <section className={`min-h-0 flex-col overflow-y-auto border-ink-800 md:flex md:border-r ${selected ? 'hidden' : 'flex'}`} aria-label="Online players">
        <div className="border-b border-ink-800 px-4 pb-3 pt-4">
          <div className="flex items-baseline gap-2">
            <h1 className="text-[15px] font-semibold text-ink-50">{stats ? `${stats.players.onlineNow} online` : 'Online'}</h1>
            {stats && <span className="text-[13px] text-ink-400">in {inRooms} {inRooms === 1 ? 'room' : 'rooms'}</span>}
            <button
              onClick={load}
              disabled={loading}
              className="ml-auto rounded-[5px] p-1 text-ink-400 hover:bg-ink-850 hover:text-ink-100"
              aria-label="Refresh"
              title={stats ? `Updated ${new Date(stats.serverTime).toLocaleTimeString()}` : 'Refresh'}
            >
              <RefreshCw className={loading ? 'animate-spin' : ''} />
            </button>
          </div>
          {stats && (
            <div className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[13px] text-ink-400">
              <span>{stats.players.total.toLocaleString()} accounts</span>
              <span>{stats.players.newToday} new today</span>
              {stats.moderation.openReports > 0 && (
                <Link to="/players?tab=reports" className="text-warn hover:underline">
                  {stats.moderation.openReports} open {stats.moderation.openReports === 1 ? 'report' : 'reports'}
                </Link>
              )}
              {stats.players.bannedNow > 0 && (
                <Link to="/players?tab=bans" className="hover:text-ink-100 hover:underline">{stats.players.bannedNow} banned</Link>
              )}
            </div>
          )}
        </div>

        {err && <div className="mx-4 mt-3 rounded-md border border-danger/40 px-3 py-2 text-[13px] text-danger">{err}</div>}
        {!stats && !err && <div className="px-4 py-8 text-[13px] text-ink-400">Loading…</div>}
        {stats && online.length === 0 && (
          <div className="px-4 py-8 text-[13px] text-ink-400">Nobody is online right now.</div>
        )}

        <div className="pb-4">
          {groups.map(g => (
            <div key={g.key}>
              <div className="flex items-baseline gap-2 px-4 pb-1 pt-3 text-xs text-ink-400">
                {g.room ? (
                  <>
                    <Link to={`/rooms/${g.room.roomId}`} className="truncate text-[13px] font-semibold text-ink-50 hover:underline">{g.room.name}</Link>
                    <span className="shrink-0">{[g.room.photonRegionId, g.room.isPrivate && 'private'].filter(Boolean).join(', ')}</span>
                    <span className={`ml-auto shrink-0 tabular-nums ${g.room.maxCapacity > 0 && g.players.length >= g.room.maxCapacity ? 'text-warn' : ''}`}>
                      {g.players.length}{g.room.maxCapacity > 0 ? `/${g.room.maxCapacity}` : ''}
                    </span>
                  </>
                ) : (
                  <>
                    <span className="text-[13px] font-semibold text-ink-50">Not in a room</span>
                    <span className="ml-auto tabular-nums">{g.players.length}</span>
                  </>
                )}
              </div>
              {g.players.map(p => {
                const on = p.id === selected;
                return (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => select(p.id)}
                    aria-current={on ? 'true' : undefined}
                    className={`flex w-full items-center gap-2.5 px-4 py-2.5 text-left text-[14px] md:py-1.5 md:text-[13.5px] ${
                      on ? 'bg-sel shadow-[inset_2px_0_0_rgb(var(--brand-500))]' : 'hover:bg-ink-850'
                    }`}
                  >
                    <span className="min-w-0 flex-1 truncate text-ink-50">
                      {p.displayName || p.username}
                      {p.isAdmin && <span className="ml-2 text-xs text-brand-300">admin</span>}
                      {p.isJunior && <span className="ml-2 text-xs text-warn">junior</span>}
                    </span>
                    <span className="text-xs tabular-nums text-ink-400" title="Level">{p.level}</span>
                    <span className="w-16 text-right text-xs text-ink-400" title={absoluteTime(p.lastSeenAt)}>{shortAgo(p.lastSeenAt)}</span>
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      </section>

      {/* Selected player */}
      <section className={`min-h-0 overflow-y-auto md:block ${selected ? 'block' : 'hidden'}`} aria-label="Player">
        {selected ? (
          <div className="mx-auto max-w-3xl px-4 py-4 sm:px-7 sm:py-6">
            <button onClick={back} className="mb-3 inline-flex items-center gap-1.5 text-[14px] text-brand-300 md:hidden">
              <ArrowLeft /> Live
            </button>
            <PlayerPanel
              id={selected}
              whereabouts={selectedOnline?.currentRoom ? (
                <>in <Link to={`/rooms/${selectedOnline.currentRoom.roomId}`} className="text-brand-300 hover:underline">{selectedOnline.currentRoom.name}</Link></>
              ) : undefined}
              onChanged={load}
              onDeleted={() => { select(null); load(); }}
            />
          </div>
        ) : (
          <div className="flex h-full items-center justify-center px-6 text-center text-[13.5px] text-ink-400">
            Pick a player to see their account and moderation tools.
          </div>
        )}
      </section>
    </div>
  );
}

function groupByRoom(online: Online[]): RoomGroup[] {
  const map = new Map<string, RoomGroup>();
  for (const p of online) {
    const r = p.currentRoom;
    const key = r ? `${r.roomId}:${r.subRoomId}:${r.roomInstanceId}` : 'none';
    let g = map.get(key);
    if (!g) { g = { key, room: r, players: [] }; map.set(key, g); }
    g.players.push(p);
  }
  const groups = [...map.values()];
  for (const g of groups) {
    g.players.sort((a, b) => (a.displayName || a.username).localeCompare(b.displayName || b.username));
  }
  // Busiest rooms first; players outside a room last.
  return groups.sort((a, b) => {
    if (!a.room !== !b.room) return a.room ? -1 : 1;
    return b.players.length - a.players.length;
  });
}

// "4 min ago" is too wide for the list column; "4m" is enough.
function shortAgo(iso: string | null | undefined): string {
  const r = relativeTime(iso);
  if (r === 'just now') return 'now';
  return r.replace(/ ago$/, '').replace(/ (sec|min|hr|day|wk|mo|yr)$/, (_, u: string) => ({ sec: 's', min: 'm', hr: 'h', day: 'd', wk: 'w', mo: 'mo', yr: 'y' } as Record<string, string>)[u]);
}
