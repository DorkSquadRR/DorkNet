import { useEffect, useRef, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { Confirm } from '../components/Confirm';
import { useToast } from '../components/Toast';
import { HardDrive, RefreshCw } from '../components/Icons';
import { num } from '../lib/format';

// Wire shape returned by StorageBackfillController.BackfillStatus.
interface BackfillStatus {
  running: boolean;
  dryRun: boolean;
  startedAt: string | null;
  finishedAt: string | null;
  total: number;
  uploaded: number;
  alreadyMirrored: number;
  skipped: number;
  failed: number;
  lastBlobName: string | null;
  elapsedMs: number | null;
  error: string | null;
}

export function Storage() {
  const [status, setStatus] = useState<BackfillStatus | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [pendingMode, setPendingMode] = useState<null | 'dry' | 'real'>(null);
  const pollRef = useRef<number | null>(null);
  const toast = useToast();

  // Polling cadence:
  //   * 1 s while a run is active so the progress bar feels live.
  //   * 5 s when idle so the page doesn't hammer the server.
  // Single source of truth — useEffect re-schedules itself based on
  // the latest status.running.
  useEffect(() => {
    const fetchOnce = async () => {
      try {
        const s = await get<BackfillStatus>('/storage/backfill/status');
        setStatus(s);
        setErr(null);
      } catch (e) {
        setErr((e as Error).message);
      }
    };
    void fetchOnce();
    const interval = status?.running ? 1000 : 5000;
    pollRef.current = window.setInterval(fetchOnce, interval);
    return () => { if (pollRef.current) window.clearInterval(pollRef.current); };
  }, [status?.running]);

  const start = async (dryRun: boolean) => {
    setPendingMode(null);
    try {
      await api(`/storage/backfill${dryRun ? '?dryRun=true' : ''}`, { method: 'POST' });
      toast.push(dryRun ? 'Dry-run started' : 'Backfill started', 'success');
      // Force a status refresh so the UI flips to running immediately.
      const s = await get<BackfillStatus>('/storage/backfill/status');
      setStatus(s);
    } catch (e) {
      const msg = (e as Error).message;
      // 409 already-running is informational, not an error.
      if (msg.includes('already_running')) toast.push('A backfill is already in progress', 'error');
      else toast.push(msg, 'error');
    }
  };

  const pct = status && status.total > 0
    ? Math.round(((status.uploaded + status.alreadyMirrored + status.skipped + status.failed) / status.total) * 100)
    : 0;
  const done = status && !status.running && status.startedAt !== null;

  return (
    <div>
      <PageHeader
        title="Storage backfill"
        blurb="Move the legacy RoomDataBlobs.Bytes column out of Postgres and into S3 at the canonical key. Safe to run anytime — each row is HEAD-verified in S3 before the DB copy is dropped, and re-runs are idempotent."
        actions={<>
          <button
            onClick={() => setPendingMode('dry')}
            disabled={status?.running}
            className="btn-secondary text-xs"
          >
            <HardDrive /> Dry-run
          </button>
          <button
            onClick={() => setPendingMode('real')}
            disabled={status?.running}
            className="btn-primary text-xs"
          >
            <HardDrive /> Run backfill
          </button>
        </>}
      />

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">
          {err}
        </div>
      )}

      {status === null && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading status…</div>
      )}

      {status && (
        <div className="space-y-4">
          <div className="card !p-5">
            <div className="flex flex-wrap items-center gap-3 justify-between mb-3">
              <div className="flex items-center gap-2.5">
                {status.running
                  ? <RefreshCw className="animate-spin text-brand-300" />
                  : status.failed > 0
                    ? <span className="badge-banned">Errors</span>
                    : done
                      ? <span className="badge-online">Idle · last run OK</span>
                      : <span className="badge-neutral">Idle</span>
                }
                <div className="text-sm font-medium text-ink-50">
                  {status.running
                    ? (status.dryRun ? 'Dry-run in progress…' : 'Backfill in progress…')
                    : done
                      ? (status.dryRun ? 'Last run: dry-run' : 'Last run: live')
                      : 'No run yet'
                  }
                </div>
              </div>
              <div className="text-xs text-ink-400 tabular-nums">
                {status.running ? `${pct}%` : status.elapsedMs != null ? `${(status.elapsedMs / 1000).toFixed(1)}s` : ''}
              </div>
            </div>

            <div className="h-2 rounded-full bg-ink-800 overflow-hidden">
              <div
                className={`h-full transition-all ${status.failed > 0 ? 'bg-danger' : 'bg-brand-500'}`}
                style={{ width: `${pct}%` }}
              />
            </div>

            <div className="mt-4 grid grid-cols-2 sm:grid-cols-5 gap-2 text-xs">
              <Counter label="Total"    value={num(status.total)} />
              <Counter label="Uploaded" value={num(status.uploaded)}        tone="success" />
              <Counter label="Already"  value={num(status.alreadyMirrored)} />
              <Counter label="Skipped"  value={num(status.skipped)} />
              <Counter label="Failed"   value={num(status.failed)}          tone={status.failed > 0 ? 'danger' : undefined} />
            </div>

            {status.lastBlobName && (
              <div className="mt-3 text-[11px] text-ink-400 font-mono truncate" title={status.lastBlobName}>
                last: {status.lastBlobName}
              </div>
            )}

            {status.error && (
              <div className="mt-3 rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">
                {status.error}
              </div>
            )}
          </div>

          <div className="card !p-4 text-xs text-ink-400 leading-relaxed space-y-2">
            <p className="font-medium text-ink-200">What this does</p>
            <ul className="list-disc list-inside space-y-1 text-ink-400">
              <li>Walks every <code className="text-ink-200">RoomDataBlobs</code> row whose <code className="text-ink-200">Bytes</code> column still holds data.</li>
              <li>Routes each blob through <code className="text-ink-200">BlobRouter.Route(BlobName)</code> to its canonical S3 (bucket, key).</li>
              <li>Uploads to S3, HEAD-verifies the object exists, then clears the row's <code className="text-ink-200">Bytes</code>.</li>
              <li>A row's bytes are only dropped from the DB after S3 confirms the same key — no data loss path.</li>
            </ul>
            <p className="font-medium text-ink-200 pt-2">When to run</p>
            <ul className="list-disc list-inside space-y-1 text-ink-400">
              <li>Once after the S3-only refactor deploy lands, to drain pre-cutover imports.</li>
              <li>Idempotent — re-running picks up only rows whose <code className="text-ink-200">Bytes</code> hasn't been cleared yet.</li>
              <li>Failed rows leave their DB bytes untouched and get retried on the next run.</li>
            </ul>
          </div>
        </div>
      )}

      <Confirm
        open={pendingMode === 'dry'}
        onClose={() => setPendingMode(null)}
        title="Run dry-run backfill"
        body={<>Walks every row, logs the (bucket, key) it WOULD upload to, but doesn't write to S3 or clear the DB. Safe to run anytime — useful to verify the routing before a real run.</>}
        confirmLabel="Start dry-run"
        onConfirm={() => start(true)}
      />
      <Confirm
        open={pendingMode === 'real'}
        onClose={() => setPendingMode(null)}
        title="Run live backfill"
        body={<>Uploads every row's bytes to S3 and clears the DB Bytes column for each row after HEAD-verifies the S3 object. Safe — re-runs are idempotent, and failures leave bytes intact in the DB. Continue?</>}
        confirmLabel="Start backfill"
        onConfirm={() => start(false)}
      />
    </div>
  );
}

function Counter({ label, value, tone }: { label: string; value: string; tone?: 'success' | 'danger' }) {
  const color = tone === 'success' ? 'text-success'
              : tone === 'danger'  ? 'text-danger'
              : 'text-ink-50';
  return (
    <div className="rounded border border-ink-800 bg-ink-900/60 px-3 py-2">
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className={`text-sm font-semibold tabular-nums ${color}`}>{value}</div>
    </div>
  );
}
