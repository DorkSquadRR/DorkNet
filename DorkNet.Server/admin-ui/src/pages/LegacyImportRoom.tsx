import { useMemo, useRef, useState } from 'react';
import { api, ApiError } from '../lib/api';
import type { Player, Room } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';

// Extracts the file's parent folder name from a webkitRelativePath
// like "YarrHarrHeist/Lobby/abc.room" → "Lobby". Mirrors the C# helper
// in AdminController.ImportRoom so the preview matches what the server
// will actually use.
function extractScene(relPath: string): string {
  const parts = relPath.replace(/\\/g, '/').split('/').filter(Boolean);
  return parts.length >= 2 ? parts[parts.length - 2] : '';
}

interface FilePick {
  file: File;
  scene: string;
}

export function LegacyImportRoom() {
  const { data: players } = useApi<Player[]>('/players?take=500');
  const { data: rooms } = useApi<Room[]>('/rooms');

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [entryScene, setEntryScene] = useState('');
  const [ownerId, setOwnerId] = useState<number | null>(null);
  const [picks, setPicks] = useState<FilePick[]>([]);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const folderRef = useRef<HTMLInputElement>(null);
  const filesRef = useRef<HTMLInputElement>(null);
  const toast = useToast();

  const distinctScenes = useMemo(() => Array.from(new Set(picks.map(p => p.scene))).filter(Boolean), [picks]);

  const onFolder = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    setPicks(files
      .filter(f => f.name.endsWith('.room'))
      .map(f => {
        // webkitRelativePath is in lib.dom.d.ts as a non-empty string when
        // the file was picked via <input webkitdirectory>; otherwise "".
        const rel = f.webkitRelativePath ?? '';
        return { file: f, scene: extractScene(rel) || extractScene(f.name) };
      })
    );
  };

  const onFiles = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    setPicks(prev => [
      ...prev,
      ...files.filter(f => f.name.endsWith('.room'))
            .map(f => ({ file: f, scene: '' })),
    ]);
  };

  const updateScene = (idx: number, scene: string) => {
    setPicks(prev => prev.map((p, i) => i === idx ? { ...p, scene } : p));
  };

  const removeFile = (idx: number) => {
    setPicks(prev => prev.filter((_, i) => i !== idx));
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !ownerId || picks.length === 0) return;
    if (picks.some(p => !p.scene.trim())) {
      toast.push('Every file needs a scene folder name', 'error');
      return;
    }
    setBusy(true);
    setResult(null);
    const fd = new FormData();
    fd.append('name', name.trim());
    fd.append('description', description.trim());
    fd.append('entryScene', entryScene.trim());
    fd.append('creatorPlayerId', String(ownerId));
    for (const p of picks) {
      fd.append('files', p.file, p.file.name);
      fd.append('scenePaths', p.scene);
    }
    try {
      const res = await api<{
        roomId: number; roomName: string; sceneCount: number; entryScene: string;
        normalizedSceneCount: number; htrMirrorStarted: boolean;
      }>('/rooms/import', { method: 'POST', formData: fd, timeoutMs: 600_000 });
      setResult(`Imported room #${res.roomId} "${res.roomName}" — ${res.sceneCount} scene(s), entry: ${res.entryScene}. .htr mirror running in background.`);
      toast.push('Room imported', 'success');
      setName(''); setDescription(''); setEntryScene(''); setPicks([]);
      if (folderRef.current) folderRef.current.value = '';
      if (filesRef.current) filesRef.current.value = '';
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : (e as Error).message;
      setResult(`Failed: ${msg}`);
      toast.push(msg, 'error');
    } finally {
      setBusy(false);
    }
  };

  // ── .htr re-mirror ────────────────────────────────────────────────
  const [mirrorRoomId, setMirrorRoomId] = useState<number | ''>('');
  const [mirrorResult, setMirrorResult] = useState<string | null>(null);
  const [mirrorBusy, setMirrorBusy] = useState(false);

  const runMirror = async () => {
    if (!mirrorRoomId) return;
    setMirrorBusy(true);
    setMirrorResult(null);
    try {
      const res = await api<{ uniqueRefs: number; downloaded: number; alreadyMirrored: number }>(
        `/rooms/${mirrorRoomId}/mirror-htr`, { method: 'POST', timeoutMs: 600_000 }
      );
      setMirrorResult(`Scanned ${res.uniqueRefs} refs · ${res.downloaded} downloaded · ${res.alreadyMirrored} already mirrored.`);
      toast.push('Mirror complete', 'success');
    } catch (e) {
      const msg = (e as Error).message;
      setMirrorResult(msg);
      toast.push(msg, 'error');
    } finally {
      setMirrorBusy(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Import room (legacy)"
        blurb="Original folder-based importer. The new zip-based importer at /import-room handles the same workflow with less manual fiddling; this page stays around for tooling that already targets the legacy multi-file endpoint."
      />

      <form onSubmit={submit} className="card !p-5 space-y-4">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <Field label="Room name">
            <input value={name} onChange={e => setName(e.target.value)} required className="input" placeholder="YarrHarrHeist" />
          </Field>
          <Field label="Entry scene (optional, defaults to Lobby or alphabetical first)">
            <input value={entryScene} onChange={e => setEntryScene(e.target.value)} className="input" placeholder="Lobby" />
          </Field>
        </div>
        <Field label="Description">
          <input value={description} onChange={e => setDescription(e.target.value)} className="input" placeholder="Imported from archive" />
        </Field>
        <Field label="Owner (account that becomes the room's creator)">
          <select value={ownerId ?? ''} onChange={e => setOwnerId(e.target.value ? parseInt(e.target.value) : null)} required className="input">
            <option value="">— pick an owner —</option>
            {(players ?? []).map(p => <option key={p.id} value={p.id}>{p.displayName || p.username} · @{p.username} · #{p.id}</option>)}
          </select>
        </Field>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <Field label="Pick a folder (preserves scene structure)">
            <input
              ref={folderRef}
              type="file"
              multiple
              // @ts-expect-error — webkitdirectory is non-standard but supported by Chromium + Firefox
              webkitdirectory=""
              directory=""
              onChange={onFolder}
              className="input file:mr-2 file:rounded-md file:border-0 file:bg-ink-700 file:px-2.5 file:py-1 file:text-xs file:text-ink-100 file:cursor-pointer"
            />
          </Field>
          <Field label="…or pick individual .room files (set scene manually)">
            <input
              ref={filesRef}
              type="file"
              multiple
              accept=".room"
              onChange={onFiles}
              className="input file:mr-2 file:rounded-md file:border-0 file:bg-ink-700 file:px-2.5 file:py-1 file:text-xs file:text-ink-100 file:cursor-pointer"
            />
          </Field>
        </div>

        {picks.length > 0 && (
          <div className="card !p-3">
            <div className="text-xs text-ink-400 mb-2">
              {picks.length} file{picks.length === 1 ? '' : 's'} · {distinctScenes.length} scene{distinctScenes.length === 1 ? '' : 's'} ({distinctScenes.join(', ') || 'none'})
            </div>
            <table className="w-full text-xs">
              <thead className="text-ink-400">
                <tr><th className="text-left pb-1">Scene</th><th className="text-left pb-1">File</th><th className="text-right pb-1">Bytes</th><th /></tr>
              </thead>
              <tbody className="divide-y divide-ink-800">
                {picks.map((p, i) => (
                  <tr key={i}>
                    <td className="py-1.5"><input value={p.scene} onChange={e => updateScene(i, e.target.value)} className="input !py-1 text-xs" /></td>
                    <td className="py-1.5 font-mono text-ink-300">{p.file.name}</td>
                    <td className="py-1.5 text-right text-ink-400 tabular-nums">{p.file.size.toLocaleString()}</td>
                    <td className="py-1.5 text-right"><button type="button" onClick={() => removeFile(i)} className="btn-ghost text-xs">×</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="flex justify-end">
          <button disabled={busy || !name.trim() || !ownerId || picks.length === 0} className="btn-primary text-xs">
            {busy ? 'Uploading…' : `Import (${picks.length} files)`}
          </button>
        </div>

        {result && <div className="card !p-3 text-xs text-ink-200 whitespace-pre-wrap">{result}</div>}
      </form>

      <div className="mt-6">
        <h2 className="text-lg font-semibold text-ink-50 mb-1">Re-mirror .htr assets</h2>
        <p className="text-sm text-ink-400 mb-3">
          Synchronously rescan an existing room's blobs and download any Holotar / AudioSampler <code className="font-mono text-xs">.htr</code> refs not yet in <code className="font-mono text-xs">RoomDataBlobs</code>.
        </p>
        <div className="card !p-4 flex flex-wrap items-end gap-2">
          <Field label="Room">
            <select value={mirrorRoomId} onChange={e => setMirrorRoomId(e.target.value ? parseInt(e.target.value) : '')} className="input min-w-[280px]">
              <option value="">— pick a room —</option>
              {(rooms ?? []).map(r => <option key={r.id} value={r.id}>{r.name} · #{r.id} · {r.blobCount} blobs</option>)}
            </select>
          </Field>
          <button onClick={runMirror} disabled={mirrorBusy || !mirrorRoomId} className="btn-secondary text-xs">
            {mirrorBusy ? 'Mirroring…' : 'Run mirror'}
          </button>
          {mirrorResult && <div className="text-xs text-ink-300 ml-2">{mirrorResult}</div>}
        </div>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="label">{label}</span>
      {children}
    </label>
  );
}
