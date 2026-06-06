import { useState } from 'react';
import { api } from '../lib/api';
import type { Report } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { Modal } from '../components/Modal';
import { useToast } from '../components/Toast';
import { absoluteTime, relativeTime } from '../lib/format';
import { RefreshCw } from '../components/Icons';

export function Reports({ embedded }: { embedded?: boolean } = {}) {
  const { data, loading, error, refresh } = useApi<Report[]>('/reports?take=100');
  const [resolving, setResolving] = useState<Report | null>(null);

  const refreshBtn = (
    <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
      <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
    </button>
  );

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{refreshBtn}</div>
      ) : (
        <PageHeader
          title="Open reports"
          blurb="Oldest first. Resolving a report only closes the ticket — issue the actual moderation action separately."
          actions={refreshBtn}
        />
      )}

      {error && <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{error}</div>}

      <div className="card overflow-hidden">
        {data && data.length === 0 && <Empty title="No open reports" blurb="When a player reports another, it lands here." />}
        {data && data.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[640px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
              <tr>
                <th className="text-left font-medium px-4 py-2.5">#</th>
                <th className="text-left font-medium px-4 py-2.5">Reporter</th>
                <th className="text-left font-medium px-4 py-2.5">Target</th>
                <th className="text-left font-medium px-4 py-2.5">Reason</th>
                <th className="text-left font-medium px-4 py-2.5">Filed</th>
                <th />
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {data.map(r => (
                <tr key={r.id} className="table-row-hover">
                  <td className="px-4 py-2.5 text-ink-400 tabular-nums">{r.id}</td>
                  <td className="px-4 py-2.5 text-ink-200">#{r.reporterPlayerId}</td>
                  <td className="px-4 py-2.5 text-ink-200">#{r.targetPlayerId}</td>
                  <td className="px-4 py-2.5 text-ink-100 max-w-md">
                    <div className="font-medium">{r.reason || '—'}</div>
                    {r.context && <div className="text-xs text-ink-400 mt-0.5 line-clamp-2">{r.context}</div>}
                  </td>
                  <td className="px-4 py-2.5 text-ink-300 text-xs" title={absoluteTime(r.createdAt)}>{relativeTime(r.createdAt)}</td>
                  <td className="px-4 py-2.5 text-right">
                    <button onClick={() => setResolving(r)} className="btn-primary text-xs">Resolve</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
      </div>

      {resolving && <ResolveModal report={resolving} onClose={() => setResolving(null)} onResolved={() => { setResolving(null); refresh(); }} />}
    </div>
  );
}

function ResolveModal({ report, onClose, onResolved }: { report: Report; onClose: () => void; onResolved: () => void }) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const submit = async () => {
    setBusy(true);
    try {
      await api(`/reports/${report.id}/resolve`, { method: 'POST', body: { Note: note } });
      toast.push('Report resolved', 'success');
      onResolved();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Resolve report #${report.id}`}
      open
      onClose={onClose}
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={submit} className="btn-primary text-xs" disabled={busy}>{busy ? 'Resolving…' : 'Resolve'}</button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <div className="grid grid-cols-2 gap-3 text-xs">
          <div className="card !p-2"><div className="text-ink-400">Reporter</div><div className="text-ink-100">#{report.reporterPlayerId}</div></div>
          <div className="card !p-2"><div className="text-ink-400">Target</div><div className="text-ink-100">#{report.targetPlayerId}</div></div>
        </div>
        <div className="card !p-3">
          <div className="text-[11px] uppercase tracking-widest text-ink-400">Reason</div>
          <div className="text-ink-100 mt-1">{report.reason || '—'}</div>
        </div>
        {report.context && (
          <div className="card !p-3">
            <div className="text-[11px] uppercase tracking-widest text-ink-400">Context</div>
            <div className="text-ink-200 text-xs mt-1 whitespace-pre-wrap">{report.context}</div>
          </div>
        )}
        <label className="block">
          <div className="label mb-1">Resolution note (logged to audit)</div>
          <textarea value={note} onChange={e => setNote(e.target.value)} rows={3} className="input" placeholder="warned · dismissed · banned 7d · etc." />
        </label>
      </div>
    </Modal>
  );
}
