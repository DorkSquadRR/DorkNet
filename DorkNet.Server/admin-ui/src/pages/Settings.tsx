import { useEffect, useState } from 'react';
import { api, get } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { RefreshCw } from '../components/Icons';

interface ServerSettings {
  signupsDisabled: boolean;
  updatedAt: string;
}

export function Settings() {
  const [settings, setSettings] = useState<ServerSettings | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const load = async () => {
    try {
      const s = await get<ServerSettings>('/settings');
      setSettings(s);
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  useEffect(() => { void load(); }, []);

  const toggleSignups = async () => {
    if (!settings) return;
    const next = !settings.signupsDisabled;
    const verb = next ? 'disable' : 'enable';
    if (!confirm(`Really ${verb} new account creation? Existing players keep their access either way.`)) return;
    setBusy(true);
    try {
      const updated = await api<ServerSettings>('/settings/signups', {
        method: 'POST',
        body: { Disabled: next },
      });
      setSettings(updated);
      toast.push(next ? 'New signups blocked.' : 'New signups allowed.', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Server settings"
        blurb="Runtime toggles applied across every replica without a redeploy."
        actions={
          <button onClick={load} className="btn-secondary text-xs" disabled={busy}>
            <RefreshCw className={busy ? 'animate-spin' : ''} />
            Refresh
          </button>
        }
      />

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{err}</div>
      )}

      {!settings && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading…</div>
      )}

      {settings && (
        <div className="card !p-5 max-w-2xl">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-ink-50">In-game signups</h2>
              <p className="mt-1 text-xs text-ink-400">
                When disabled, the watch's account-creation flow returns an error to the player instead of minting a new account.
                Existing logins keep working — only brand-new signups are blocked.
              </p>
              <p className="mt-2 text-[11px] text-ink-500">
                Last changed {new Date(settings.updatedAt).toLocaleString()}.
              </p>
            </div>
            <button
              onClick={toggleSignups}
              disabled={busy}
              className={(settings.signupsDisabled ? 'btn-primary' : 'btn-danger') + ' text-xs shrink-0'}
            >
              {busy ? 'Working…' : settings.signupsDisabled ? 'Enable signups' : 'Disable signups'}
            </button>
          </div>
          <div className="mt-4 text-xs">
            {settings.signupsDisabled
              ? <span className="badge-banned">Signups disabled</span>
              : <span className="badge-online">Signups allowed</span>}
          </div>
        </div>
      )}
    </div>
  );
}
