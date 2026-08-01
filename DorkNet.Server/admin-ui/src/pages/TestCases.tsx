import { useState } from 'react';
import { api } from '../lib/api';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { useToast } from '../components/Toast';
import { RefreshCw, Trash } from '../components/Icons';

// QA test cases and the GitHub issues filed against them.
//
// Status values match the client's TestCaseStatus enum. Only Failed<->Passed
// are the reconciler's to move (a closed issue means fixed), so a Claimed case
// is never rewritten by a sync — see docs/admin.md.
const STATUS: Record<number, { label: string; className: string }> = {
  0: { label: 'Not yet tested', className: 'badge-neutral' },
  1: { label: 'Claimed', className: 'badge-neutral' },
  2: { label: 'Failed', className: 'badge-banned' },
  3: { label: 'Passed', className: 'badge-online' },
};

interface TestCaseRow {
  id: string;
  key: string;
  title: string;
  roomName: string;
  status: number;
  testPassId: number | null;
  issueUrl: string;
  issueNumber: number | null;
  updatedAt: string;
}

interface TestCaseList {
  githubConfigured: boolean;
  repository: string;
  cases: TestCaseRow[];
}

export function TestCases() {
  const toast = useToast();
  const [status, setStatus] = useState<number | ''>('');
  const query = status === '' ? '' : `?status=${status}`;
  const { data, loading, error, refresh } = useApi<TestCaseList>(`/testcases${query}`);
  const [busy, setBusy] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);

  const fileIssue = async (row: TestCaseRow) => {
    setBusy(row.id);
    try {
      const res = await api<{ issue: { number: number; url: string }; created: boolean }>(
        `/testcases/${row.id}/issue`, { method: 'POST' },
      );
      toast.push(
        res.created
          ? `Filed #${res.issue.number} for ${row.key}`
          : `${row.key} is already linked to #${res.issue.number}`,
        'success',
      );
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const unlink = async (row: TestCaseRow) => {
    setBusy(row.id);
    try {
      // Unlink only — the issue itself is deliberately left open.
      await api(`/testcases/${row.id}/issue`, { method: 'DELETE' });
      toast.push(`Unlinked ${row.key}`, 'success');
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const sync = async () => {
    setSyncing(true);
    try {
      const res = await api<{ reconciled: number }>('/testcases/issues/sync', { method: 'POST' });
      toast.push(
        res.reconciled === 0 ? 'Everything already in sync' : `Updated ${res.reconciled} case(s)`,
        'success',
      );
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setSyncing(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Test cases"
        actions={<>
          <select
            value={status}
            onChange={e => setStatus(e.target.value === '' ? '' : Number(e.target.value))}
            className="input text-xs"
          >
            <option value="">All statuses</option>
            {Object.entries(STATUS).map(([value, s]) => (
              <option key={value} value={value}>{s.label}</option>
            ))}
          </select>
          <button
            onClick={sync}
            className="btn-secondary text-xs"
            disabled={syncing || !data?.githubConfigured}
            title={data?.githubConfigured ? 'Reconcile linked cases against their issues' : 'GitHub is not configured'}
          >
            {syncing ? 'Syncing…' : 'Sync issues'}
          </button>
          <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        </>}
      />

      {/* A server with no GitHub token is an ordinary deployment, so this is a
          note rather than an error. */}
      {data && !data.githubConfigured && (
        <div className="card px-4 py-3 text-sm text-ink-300 mb-4">
          GitHub issue linking is off. Set <code className="font-mono">GitHub:Token</code> and{' '}
          <code className="font-mono">GitHub:Repository</code> to enable it.
        </div>
      )}
      {error && <div className="card px-4 py-3 text-sm text-danger mb-4">{error}</div>}

      <div className="card overflow-hidden">
        {data && data.cases.length === 0 && <Empty title="No test cases" />}
        {data && data.cases.length > 0 && (
          <ul className="divide-y divide-ink-800">
            {data.cases.map(row => {
              const badge = STATUS[row.status] ?? STATUS[0];
              return (
                <li key={row.id} className="px-4 py-3 flex flex-wrap items-center justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-medium text-ink-50 truncate">{row.title}</span>
                      <span className="badge-neutral font-mono">{row.key}</span>
                      <span className={badge.className}>{badge.label}</span>
                      {row.testPassId !== null && (
                        <span className="text-xs text-ink-500">pass {row.testPassId}</span>
                      )}
                    </div>
                    {row.roomName && <div className="text-xs text-ink-500 mt-0.5">{row.roomName}</div>}
                  </div>
                  <div className="flex items-center gap-2">
                    {row.issueNumber !== null ? (
                      <>
                        <a
                          href={row.issueUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="btn-secondary text-xs"
                        >
                          #{row.issueNumber}
                        </a>
                        <button
                          onClick={() => unlink(row)}
                          disabled={busy === row.id}
                          className="btn-ghost text-xs text-danger"
                        >
                          <Trash /> Unlink
                        </button>
                      </>
                    ) : (
                      <button
                        onClick={() => fileIssue(row)}
                        disabled={busy === row.id || !data.githubConfigured}
                        className="btn-primary text-xs"
                        title={data.githubConfigured ? 'File a GitHub issue' : 'GitHub is not configured'}
                      >
                        {busy === row.id ? 'Filing…' : 'File issue'}
                      </button>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
