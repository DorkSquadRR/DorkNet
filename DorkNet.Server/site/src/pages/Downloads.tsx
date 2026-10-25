import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { fileUrl, getDownloads, type DownloadFile, type DownloadRelease, type DownloadsManifest } from '../lib/downloads';

const fmtSize = (n: number) =>
  n >= 2 ** 30 ? `${(n / 2 ** 30).toFixed(2)} GB` : n >= 2 ** 20 ? `${(n / 2 ** 20).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 2 ** 10))} KB`;
const fmtDate = (iso?: string) => {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(+d) ? '' : d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
};

const PLATFORMS = [
  { id: 'pc', title: 'PC', hint: 'Windows, with or without a VR headset.' },
  { id: 'quest', title: 'Meta Quest', hint: 'Needs developer mode on your headset and a USB cable.' },
];

// What each file is for, in players' terms. Keyed by the ids publish.py writes.
function describe(f: DownloadFile, rel: DownloadRelease): { title: string; primary?: boolean; body: React.ReactNode } {
  switch (f.id) {
    case 'pc-full':
      return {
        title: 'Full game', primary: true,
        body: <>Rec Room {rel.title} with DorkNet already set up. Unzip it anywhere and
          run {rel.pc_exe ? <code className="text-ink-200">{rel.pc_exe}</code> : 'the game'}.</>,
      };
    case 'pc-patch':
      return {
        title: 'Patch only',
        body: <>Just the DorkNet part (MelonLoader and the DorkNet mod), for a copy of the game you already have. The
          DorkNet launcher installs this for you; skip it if you got the full game.</>,
      };
    case 'quest':
      return {
        title: 'Quest app', primary: true,
        body: <>Install it straight from this site: plug in your headset and use the <Link className="text-brand-300 hover:text-brand-200" to="/quest">Quest
          page</Link> (Chrome or Edge). Or download it for SideQuest.</>,
      };
    case 'quest-installer':
      return {
        title: 'Install scripts',
        body: <>Prefer the command line? Unzip these next to the Quest app and run <code className="text-ink-200">install.cmd</code> (Windows)
          or <code className="text-ink-200">install.sh</code> (macOS / Linux). Needs adb.</>,
      };
    default:
      return { title: f.name, body: null };
  }
}

function FileCard({ f, rel }: { f: DownloadFile; rel: DownloadRelease }) {
  const d = describe(f, rel);
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(f.sha256);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch { /* clipboard blocked: the full hash is in the tooltip */ }
  };
  return (
    <article className={`card !p-5 flex flex-col gap-3 ${d.primary ? 'border-brand-500/50 ring-2 ring-brand-500/15' : ''}`}>
      <div className="flex items-center gap-2">
        <h3 className="font-semibold text-ink-50">{d.title}</h3>
        {d.primary && <span className="badge bg-brand-500/15 text-brand-200">Start here</span>}
      </div>
      <p className="text-sm text-ink-300">{d.body}</p>
      <div className="font-mono text-xs text-ink-400 break-all">{f.name}</div>
      <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-ink-400">
        <span>{fmtSize(f.size)}</span>
        {f.built_at && <span>Updated {fmtDate(f.built_at)}</span>}
        {f.build && <span>Build {f.build}</span>}
      </div>
      <div className="mt-auto flex flex-wrap items-center gap-2 pt-1">
        {f.id === 'quest' ? (
          <>
            <Link className="btn-primary" to={`/quest?file=${encodeURIComponent(f.path)}`}>Install on Quest</Link>
            <a className="btn-secondary" href={fileUrl(f)} download={f.name}>Download</a>
          </>
        ) : (
          <a className={d.primary ? 'btn-primary' : 'btn-secondary'} href={fileUrl(f)} download={f.name}>Download</a>
        )}
        <button
          type="button"
          onClick={copy}
          title={`Copy SHA-256: ${f.sha256}`}
          className={`rounded-md border border-dashed px-2 py-1 font-mono text-xs transition-colors ${
            copied ? 'border-success text-success' : 'border-ink-700 text-ink-400 hover:text-ink-100'}`}
        >
          {copied ? 'Copied' : `SHA-256 ${f.sha256.slice(0, 12)}…`}
        </button>
      </div>
    </article>
  );
}

export function Downloads() {
  const [m, setM] = useState<DownloadsManifest | null | undefined>(undefined);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    getDownloads(true).then(setM).catch((e) => setErr((e as Error).message));
  }, []);

  const releases = (m?.releases ?? []).filter((r) => r.files.length);

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Downloads</h1>
        <p className="text-sm text-ink-400">
          Rec Room for DorkNet, built from the DorkNet source by the build server. The SHA-256 next to each file
          lets you check your download.
        </p>
      </div>

      {err && <div className="rounded-lg border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">Couldn't load the builds: {err}</div>}
      {m === undefined && !err && <p className="text-sm text-ink-400">Loading builds…</p>}
      {m === null && <p className="text-sm text-ink-400">This server doesn't offer downloads.</p>}
      {m && !releases.length && <p className="text-sm text-ink-400">No builds have been published yet.</p>}

      {releases.map((rel) => (
        <section key={rel.id} className="space-y-5">
          <div className="flex flex-wrap items-baseline gap-x-3">
            <h2 className="text-lg font-semibold text-ink-50">Rec Room {rel.title}</h2>
            {rel.game_version && <span className="text-xs text-ink-400">Game build of {fmtDate(`${rel.game_version}T12:00:00Z`)}</span>}
          </div>
          {PLATFORMS.map((p) => {
            const files = rel.files.filter((f) => f.platform === p.id);
            if (!files.length) return null;
            return (
              <div key={p.id} className="space-y-2">
                <div>
                  <h3 className="label">{p.title}</h3>
                  <p className="text-xs text-ink-400">{p.hint}</p>
                </div>
                <div className="grid gap-3 sm:grid-cols-2">
                  {files.map((f) => <FileCard key={f.id} f={f} rel={rel} />)}
                </div>
              </div>
            );
          })}
        </section>
      ))}

      {releases.length > 0 && (
        <p className="border-t border-ink-800 pt-4 text-xs text-ink-400">
          These builds contain Rec Room's own game files. They're for this community only: please don't re-share them.
        </p>
      )}
    </div>
  );
}
