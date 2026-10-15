import { useRef, useState } from 'react';
import { openXapk, type Xapk } from '../lib/xapk';
import { connectHeadset, installXapk, webUsbSupported, type Headset, type Progress } from '../lib/questInstall';

const fmtSize = (n: number) =>
  n >= 1e9 ? `${(n / 1e9).toFixed(2)} GB` : n >= 1e6 ? `${(n / 1e6).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1e3))} KB`;

function Step({ n, title, done, children }: { n: number; title: string; done?: boolean; children: React.ReactNode }) {
  return (
    <section className="card !p-5 space-y-3">
      <h2 className="flex items-center gap-3 text-base font-semibold text-ink-50">
        <span className={`grid size-7 place-items-center rounded-full text-sm ${done ? 'bg-brand-500 text-white' : 'bg-ink-800 text-ink-200'}`}>
          {done ? '✓' : n}
        </span>
        {title}
      </h2>
      {children}
    </section>
  );
}

function Bar({ p }: { p: Progress }) {
  const pct = p.total > 0 ? Math.min(100, Math.round((p.done / p.total) * 100)) : 0;
  return (
    <div className="space-y-1">
      <div className="flex justify-between text-xs text-ink-300">
        <span>{p.label}</span>
        <span>{p.phase === 'launch' ? '' : `${fmtSize(p.done)} / ${fmtSize(p.total)} · ${pct}%`}</span>
      </div>
      <div className="h-2 rounded-full bg-ink-800 overflow-hidden">
        <div className="h-full bg-brand-500 transition-[width] duration-200" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

export function Quest() {
  const supported = webUsbSupported();
  const [xapk, setXapk] = useState<Xapk | null>(null);
  const [headset, setHeadset] = useState<Headset | null>(null);
  const [progress, setProgress] = useState<Record<string, Progress>>({});
  const [busy, setBusy] = useState<'file' | 'connect' | 'install' | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const pickFile = async (f: File | undefined) => {
    if (!f) return;
    setErr(null); setDone(false); setProgress({}); setBusy('file');
    try { setXapk(await openXapk(f)); }
    catch (e) { setXapk(null); setErr((e as Error).message); }
    finally { setBusy(null); }
  };

  const connect = async () => {
    setErr(null); setBusy('connect');
    try { setHeadset(await connectHeadset()); }
    catch (e) {
      const m = (e as Error).message;
      // Closing the browser's device picker isn't an error worth showing.
      if (!/No device selected|No headset selected/i.test(m)) setErr(m);
    }
    finally { setBusy(null); }
  };

  const install = async () => {
    if (!xapk || !headset) return;
    setErr(null); setDone(false); setProgress({}); setBusy('install');
    try {
      await installXapk(
        headset,
        xapk,
        (p) => setProgress((prev) => ({ ...prev, [p.phase]: p })),
        async (pkg) => window.confirm(
          `An older copy of ${pkg} is installed with a different signing key and has to be removed first.\n\n` +
          'Your account is on the server, but local settings on the headset will be lost. Continue?'),
      );
      setDone(true);
    } catch (e) {
      const m = String((e as Error)?.message ?? e);
      setErr(/disconnected|NetworkError|transfer/i.test(m)
        ? 'The headset disconnected. Keep it plugged in and awake (wear it or cover the sensor), reconnect and try again.'
        : m);
      if (/disconnected|NetworkError/i.test(m)) setHeadset(null);
    } finally {
      setBusy(null);
    }
  };

  const name = xapk?.manifest.name || 'DorkNet';
  const totalSize = xapk ? xapk.apk.size + xapk.expansions.reduce((s, x) => s + x.entry.size, 0) : 0;

  return (
    <div className="max-w-2xl mx-auto space-y-4">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Install on Meta Quest</h1>
        <p className="text-sm text-ink-400">
          Installs the DorkNet Quest build straight from your browser: no SideQuest or command line.
          Your headset needs <span className="text-ink-200">developer mode</span> on (Meta Horizon phone app → your
          headset → Developer mode).
        </p>
      </div>

      {!supported && (
        <div className="rounded-lg border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">
          This browser can't talk to USB devices. Open this page in <b>Chrome</b> or <b>Edge</b> on a computer
          (Windows, macOS, Linux or ChromeOS).
        </div>
      )}

      <Step n={1} title="Choose the .xapk file" done={!!xapk}>
        <p className="text-sm text-ink-400">Pick the <code className="text-ink-200">DorkNet-….xapk</code> you were given. It stays on your computer; nothing is uploaded.</p>
        <input ref={fileRef} type="file" accept=".xapk,.zip" className="hidden" onChange={(e) => pickFile(e.target.files?.[0])} />
        <div className="flex flex-wrap items-center gap-3">
          <button className="btn-secondary" disabled={busy !== null} onClick={() => fileRef.current?.click()}>
            {busy === 'file' ? 'Reading…' : xapk ? 'Choose a different file' : 'Choose file'}
          </button>
          {xapk && (
            <span className="text-sm text-ink-300">
              <span className="text-ink-100 font-medium">{name}</span> {xapk.manifest.version_name} · {fmtSize(totalSize)}
            </span>
          )}
        </div>
      </Step>

      <Step n={2} title="Connect your headset" done={!!headset}>
        <ol className="list-decimal pl-5 text-sm text-ink-400 space-y-1">
          <li>Plug the Quest into this computer with a USB cable.</li>
          <li>Click <b className="text-ink-200">Connect</b> and pick the Quest in the browser's list.</li>
          <li>Put the headset on and tap <b className="text-ink-200">Allow</b> on "Allow USB debugging" (tick "Always allow").</li>
        </ol>
        <p className="text-xs text-ink-500">If it says the device is in use, close SideQuest / Meta Quest Developer Hub, or run <code>adb kill-server</code>.</p>
        <div className="flex flex-wrap items-center gap-3">
          <button className="btn-secondary" disabled={!supported || busy !== null} onClick={connect}>
            {busy === 'connect' ? 'Waiting for the headset…' : headset ? 'Reconnect' : 'Connect'}
          </button>
          {headset && <span className="text-sm text-ink-300">Connected: <span className="text-ink-100 font-medium">{headset.model}</span></span>}
        </div>
      </Step>

      <Step n={3} title="Install" done={done}>
        <p className="text-sm text-ink-400">
          Installs the app and copies the game data. The game data is large, so this takes a few minutes. Keep the
          cable in and the headset awake (wear it or cover the sensor above the nose).
        </p>
        <button className="btn-primary w-full" disabled={!xapk || !headset || busy !== null} onClick={install}>
          {busy === 'install' ? 'Installing…' : done ? 'Install again' : 'Install'}
        </button>
        {(['app', 'data', 'launch'] as const).map((k) => progress[k] && <Bar key={k} p={progress[k]} />)}
        {done && (
          <div className="rounded-lg border border-brand-500/30 bg-brand-500/10 px-4 py-3 text-sm text-ink-100">
            Installed! The game is starting in the headset. Next time, find it under
            <b> Library → Unknown Sources → {name}</b>.
          </div>
        )}
      </Step>

      {err && <div className="rounded-lg border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>}

      <p className="text-xs text-ink-500">
        The app installs alongside the Meta Store version of Rec Room and doesn't replace it. You'll need a DorkNet
        account to sign in.
      </p>
    </div>
  );
}
