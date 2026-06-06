import { useEffect, useMemo, useState } from 'react';
import { get } from '../lib/api';
import type { Player, PlayerLogEntry } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { absoluteTime, clip, relativeTime } from '../lib/format';
import { RefreshCw } from '../components/Icons';

export function PlayerLogs({ embedded }: { embedded?: boolean } = {}) {
  const { data: players } = useApi<Player[]>('/players?take=500');
  const [pid, setPid] = useState<number | null>(null);
  const [filter, setFilter] = useState('');
  const [take, setTake] = useState(200);
  const [auto, setAuto] = useState(false);
  const [rows, setRows] = useState<PlayerLogEntry[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useMemo(() => async () => {
    if (pid === null) return;
    setLoading(true);
    setErr(null);
    try {
      const data = await get<PlayerLogEntry[]>(`/players/${pid}/logs?take=${take}`);
      setRows(data);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [pid, take]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    if (!auto) return;
    const id = setInterval(load, 5000);
    return () => clearInterval(id);
  }, [auto, load]);

  const filtered = (rows ?? []).filter(r => {
    if (!filter.trim()) return true;
    const f = filter.toLowerCase();
    return r.path.toLowerCase().includes(f)
        || String(r.status).includes(f)
        || r.method.toLowerCase().includes(f)
        || r.host.toLowerCase().includes(f);
  });

  return (
    <div>
      {!embedded && (
        <PageHeader
          title="Player request logs"
          blurb="Last few hundred HTTP calls per player, pulled from the Redis ring buffer. Useful when a player reports something weird."
        />
      )}

      <div className="card !p-4 mb-4 grid grid-cols-1 md:grid-cols-[2fr,1fr,120px,auto,auto] gap-2 items-end">
        <label className="flex flex-col gap-1">
          <span className="label">Player</span>
          <select value={pid ?? ''} onChange={e => setPid(e.target.value ? parseInt(e.target.value) : null)} className="input">
            <option value="">— pick a player —</option>
            {(players ?? []).map(p => (
              <option key={p.id} value={p.id}>
                {p.displayName || p.username} · @{p.username} · #{p.id}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Filter</span>
          <input value={filter} onChange={e => setFilter(e.target.value)} placeholder="path, host, status…" className="input" />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Take</span>
          <input type="number" min={10} max={500} value={take} onChange={e => setTake(parseInt(e.target.value || '200'))} className="input" />
        </label>
        <label className="flex items-center gap-2 text-xs text-ink-300 pb-1">
          <input type="checkbox" checked={auto} onChange={e => setAuto(e.target.checked)} className="size-4 accent-brand-500" />
          auto 5s
        </label>
        <button onClick={load} className="btn-secondary text-xs h-9" disabled={loading || pid === null}>
          <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
        </button>
      </div>

      {err && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>}

      <div className="card overflow-hidden">
        {pid === null && <Empty title="Pick a player" blurb="Logs load once you select someone above." />}
        {pid !== null && rows && filtered.length === 0 && (
          <Empty title={filter ? 'No log lines match the filter' : 'No log entries for this player yet'} />
        )}
        {filtered.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[720px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800 sticky top-0">
              <tr>
                <th className="text-left font-medium px-3 py-2">When</th>
                <th className="text-left font-medium px-3 py-2">Status</th>
                <th className="text-left font-medium px-3 py-2">Method</th>
                <th className="text-left font-medium px-3 py-2">Host</th>
                <th className="text-left font-medium px-3 py-2">Path</th>
                <th className="text-right font-medium px-3 py-2">ms</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800 font-mono text-xs">
              {filtered.map((r, i) => (
                <tr key={i} className="table-row-hover">
                  <td className="px-3 py-1.5 text-ink-400 whitespace-nowrap" title={absoluteTime(r.timestamp)}>{relativeTime(r.timestamp)}</td>
                  <td className={`px-3 py-1.5 tabular-nums ${r.status >= 500 ? 'text-danger' : r.status >= 400 ? 'text-warn' : 'text-success'}`}>{r.status}</td>
                  <td className="px-3 py-1.5 text-ink-200">{r.method}</td>
                  <td className="px-3 py-1.5 text-ink-300">{r.host}</td>
                  <td className="px-3 py-1.5 text-ink-100" title={r.path + (r.query ? '?' + r.query : '')}>
                    {clip(r.path + (r.query ? '?' + r.query : ''), 100)}
                  </td>
                  <td className="px-3 py-1.5 text-right text-ink-300 tabular-nums">{r.elapsedMs}</td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
      </div>
    </div>
  );
}
