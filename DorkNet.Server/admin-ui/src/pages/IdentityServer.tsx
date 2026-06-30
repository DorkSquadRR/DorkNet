import { useEffect, useState } from 'react';
import { get } from '../lib/api';
import { RefreshCw } from '../components/Icons';

interface IdentityServerSettings {
  issuer: string;
  discoveryUrl: string;
  tokenEndpoint: string;
  userInfoEndpoint: string;
  client: {
    clientId: string;
    clientName: string;
    allowedGrantTypes: string[];
    allowedScopes: string[];
    allowOfflineAccess: boolean;
    accessTokenLifetime: number;
    absoluteRefreshTokenLifetime: number;
    slidingRefreshTokenLifetime: number;
    refreshTokenUsage: string;
    refreshTokenExpiration: string;
  };
  signing: {
    certificatePath: string;
    certificateExists: boolean;
    thumbprint: string;
    notBeforeUtc: string;
    notAfterUtc: string;
    algorithm: string;
  };
  license: {
    configured: boolean;
  };
  persistedGrants: {
    store: string;
    warning: string;
  };
  legacyCompatibility: {
    injectsClientId: boolean;
    injectsDefaultScopes: boolean;
    addsLegacyTokenAliases: boolean;
    acceptsLegacyJwtSigningKey: boolean;
  };
}

export function IdentityServer() {
  const [settings, setSettings] = useState<IdentityServerSettings | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = async () => {
    setBusy(true);
    try {
      const data = await get<IdentityServerSettings>('/identityserver');
      setSettings(data);
      setErr(null);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => { void load(); }, []);

  return (
    <div>
      <div className="mb-3 flex justify-end">
        <button onClick={load} className="btn-secondary text-xs" disabled={busy}>
          <RefreshCw className={busy ? 'animate-spin' : ''} />
          Refresh
        </button>
      </div>

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">{err}</div>
      )}

      {!settings && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading...</div>
      )}

      {settings && (
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="space-y-5">
            <div className="card !p-5">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <h2 className="text-sm font-semibold text-ink-50">IdentityServer</h2>
                  <p className="mt-1 break-all font-mono text-xs text-ink-300">{settings.issuer}</p>
                </div>
                <div className="flex flex-wrap gap-2 text-xs">
                  <StatusBadge ok={settings.signing.certificateExists} on="Signing key" off="Missing key" />
                  <StatusBadge ok={settings.license.configured} on="License set" off="No license key" />
                </div>
              </div>

              <div className="mt-5 grid gap-3 md:grid-cols-3">
                <Endpoint label="Discovery" value={settings.discoveryUrl} />
                <Endpoint label="Token" value={settings.tokenEndpoint} />
                <Endpoint label="UserInfo" value={settings.userInfoEndpoint} />
              </div>
            </div>

            <div className="card !p-5">
              <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h2 className="text-sm font-semibold text-ink-50">Client</h2>
                  <p className="mt-1 font-mono text-xs text-ink-400">{settings.client.clientId}</p>
                </div>
                <span className={settings.client.allowOfflineAccess ? 'badge-online' : 'badge-neutral'}>
                  Offline access {settings.client.allowOfflineAccess ? 'on' : 'off'}
                </span>
              </div>

              <div className="grid gap-4 lg:grid-cols-2">
                <ChipList title="Grant types" values={settings.client.allowedGrantTypes} />
                <ChipList title="Scopes" values={settings.client.allowedScopes} />
              </div>

              <div className="mt-5 grid gap-3 sm:grid-cols-3">
                <Metric label="Access token" value={duration(settings.client.accessTokenLifetime)} />
                <Metric label="Refresh token" value={duration(settings.client.absoluteRefreshTokenLifetime)} />
                <Metric label="Refresh mode" value={`${settings.client.refreshTokenUsage} / ${settings.client.refreshTokenExpiration}`} />
              </div>
            </div>

            <div className="card !p-5">
              <h2 className="text-sm font-semibold text-ink-50">Legacy compatibility</h2>
              <div className="mt-4 grid gap-3 md:grid-cols-2">
                <Compatibility label="Inject client_id for old clients" ok={settings.legacyCompatibility.injectsClientId} />
                <Compatibility label="Merge default scopes" ok={settings.legacyCompatibility.injectsDefaultScopes} />
                <Compatibility label="Add legacy token aliases" ok={settings.legacyCompatibility.addsLegacyTokenAliases} />
                <Compatibility label="Accept legacy JWT signing key" ok={settings.legacyCompatibility.acceptsLegacyJwtSigningKey} />
              </div>
            </div>
          </div>

          <div className="space-y-5">
            <div className="card !p-5">
              <h2 className="text-sm font-semibold text-ink-50">Signing certificate</h2>
              <div className="mt-4 space-y-3">
                <Detail label="Path" value={settings.signing.certificatePath} mono />
                <Detail label="Thumbprint" value={settings.signing.thumbprint} mono />
                <Detail label="Algorithm" value={settings.signing.algorithm} mono />
                <Detail label="Valid from" value={formatDate(settings.signing.notBeforeUtc)} />
                <Detail label="Valid until" value={formatDate(settings.signing.notAfterUtc)} />
              </div>
            </div>

            <div className="card !p-5">
              <h2 className="text-sm font-semibold text-ink-50">Operational store</h2>
              <div className="mt-3 flex items-center justify-between gap-3">
                <span className="text-xs text-ink-400">Persisted grants</span>
                <span className="badge-junior">{settings.persistedGrants.store}</span>
              </div>
              <p className="mt-4 text-xs leading-5 text-ink-400">{settings.persistedGrants.warning}</p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function Endpoint({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-ink-800 bg-ink-950/40 p-3">
      <div className="label">{label}</div>
      <div className="mt-2 break-all font-mono text-xs text-ink-100">{value}</div>
    </div>
  );
}

function ChipList({ title, values }: { title: string; values: string[] }) {
  return (
    <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-4">
      <h3 className="text-xs font-semibold uppercase text-ink-400">{title}</h3>
      <div className="mt-3 flex flex-wrap gap-2">
        {values.map((value) => (
          <span key={value} className="badge-neutral font-mono normal-case tracking-normal">{value}</span>
        ))}
      </div>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-ink-800 bg-ink-950/30 p-3">
      <div className="text-[11px] uppercase text-ink-500">{label}</div>
      <div className="mt-1 text-sm font-semibold text-ink-50">{value}</div>
    </div>
  );
}

function Compatibility({ label, ok }: { label: string; ok: boolean }) {
  return (
    <div className="flex min-h-10 items-center justify-between gap-3 rounded-md border border-ink-800 bg-ink-950/30 px-3 py-2">
      <span className="text-xs text-ink-100">{label}</span>
      <StatusBadge ok={ok} on="On" off="Off" />
    </div>
  );
}

function Detail({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="label">{label}</div>
      <div className={(mono ? 'font-mono ' : '') + 'mt-1 break-all text-xs text-ink-100'}>{value}</div>
    </div>
  );
}

function StatusBadge({ ok, on, off }: { ok: boolean; on: string; off: string }) {
  return <span className={ok ? 'badge-online' : 'badge-junior'}>{ok ? on : off}</span>;
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function duration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0s';
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m`;
  return `${seconds}s`;
}
