import { useState } from 'react';
import type { AuditEntry } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { absoluteTime, clip, relativeTime } from '../lib/format';
import { RefreshCw } from '../components/Icons';

export function Audit() {
  const [take, setTake] = useState(200);
  const { data, loading, error, refresh } = useApi<AuditEntry[]>(`/audit?take=${take}`);

  return (
    <div>
      <PageHeader
        title="Audit log"
        blurb="Every admin action with actor, target, and reason. Newest first."
        actions={<>
          <select value={take} onChange={e => setTake(parseInt(e.target.value))} className="input w-24 text-xs !py-1.5">
            <option value={100}>last 100</option>
            <option value={200}>last 200</option>
            <option value={500}>last 500</option>
          </select>
          <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        </>}
      />

      {error && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{error}</div>}

      <div className="card overflow-hidden">
        {data && data.length === 0 && <Empty title="No audit entries yet" />}
        {data && data.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[640px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800 sticky top-0">
              <tr>
                <th className="text-left font-medium px-4 py-2.5">When</th>
                <th className="text-left font-medium px-4 py-2.5">Admin</th>
                <th className="text-left font-medium px-4 py-2.5">Action</th>
                <th className="text-left font-medium px-4 py-2.5">Target</th>
                <th className="text-left font-medium px-4 py-2.5">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {data.map(e => (
                <tr key={e.id} className="table-row-hover">
                  <td className="px-4 py-2 text-ink-300 text-xs whitespace-nowrap" title={absoluteTime(e.timestamp)}>
                    {relativeTime(e.timestamp)}
                  </td>
                  <td className="px-4 py-2 text-ink-200">#{e.adminPlayerId}</td>
                  <td className="px-4 py-2">
                    <span className="font-mono text-xs text-brand-200 bg-brand-500/10 px-1.5 py-0.5 rounded">{e.action}</span>
                  </td>
                  <td className="px-4 py-2 text-ink-200">
                    <span className="text-ink-400">{e.targetType}</span>
                    {e.targetId > 0 && <> · #{e.targetId}</>}
                  </td>
                  <td className="px-4 py-2 text-ink-300 text-xs" title={e.reason}>{clip(e.reason, 100)}</td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
      </div>
    </div>
  );
}
