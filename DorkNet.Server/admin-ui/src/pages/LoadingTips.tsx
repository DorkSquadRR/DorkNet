import { useEffect, useRef, useState } from 'react';
import { api, get } from '../lib/api';
import { imageCdnUrl } from '../lib/types';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, Trash } from '../components/Icons';

interface LoadingScreenTip {
  id?: number;
  title: string;
  message: string;
  imageName: string;
  context: number;
  platformMask: number;
  roomNamesCsv: string;
  sortOrder: number;
  isActive: boolean;
}

function blankTip(): LoadingScreenTip {
  return {
    title: '',
    message: '',
    imageName: '',
    context: 0,
    platformMask: -1,
    roomNamesCsv: '',
    sortOrder: 0,
    isActive: true,
  };
}

export function LoadingTips({ embedded }: { embedded?: boolean } = {}) {
  const [tips, setTips] = useState<LoadingScreenTip[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const toast = useToast();

  useEffect(() => {
    get<LoadingScreenTip[]>('/loadingscreentips')
      .then(setTips)
      .catch(e => setErr((e as Error).message));
  }, []);

  const update = (idx: number, next: LoadingScreenTip) => {
    if (!tips) return;
    const copy = tips.slice();
    copy[idx] = next;
    setTips(copy);
  };

  const removeLocal = (idx: number) => {
    if (!tips) return;
    setTips(tips.filter((_, i) => i !== idx));
  };

  const addNew = async () => {
    try {
      const created = await api<LoadingScreenTip>('/loadingscreentips', {
        method: 'POST',
        body: blankTip(),
      });
      setTips([...(tips ?? []), created]);
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const save = async (idx: number) => {
    if (!tips) return;
    const t = tips[idx];
    try {
      if (t.id) {
        const saved = await api<LoadingScreenTip>(`/loadingscreentips/${t.id}`, {
          method: 'PUT',
          body: t,
        });
        update(idx, saved);
      } else {
        const saved = await api<LoadingScreenTip>('/loadingscreentips', {
          method: 'POST',
          body: t,
        });
        update(idx, saved);
      }
      toast.push('Saved', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const remove = async (idx: number) => {
    if (!tips) return;
    const t = tips[idx];
    if (!confirm(`Delete tip "${t.title || '(untitled)'}"?`)) return;
    try {
      if (t.id) await api(`/loadingscreentips/${t.id}`, { method: 'DELETE' });
      removeLocal(idx);
      toast.push('Deleted', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const addBtn = <button onClick={addNew} className="btn-primary text-xs"><Plus /> Add tip</button>;

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{addBtn}</div>
      ) : (
        <PageHeader
          title="Loading screen tips"
          blurb="The list the watch fetches from cdn.localhost/config/LoadingScreenTipData on every dorm load. Edit, upload an image, or scope a tip to specific rooms — saves are live immediately."
          actions={addBtn}
        />
      )}

      {err && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">
          {err}
        </div>
      )}

      {tips === null && !err && (
        <div className="card !p-6 text-center text-xs text-ink-400">Loading tips…</div>
      )}

      {tips && tips.length === 0 && (
        <div className="card !p-6 text-center text-xs text-ink-400">
          No tips yet — add one with the button above.
        </div>
      )}

      {tips && (
        <div className="space-y-3">
          {tips.map((t, i) => (
            <TipEditor
              key={t.id ?? `new-${i}`}
              tip={t}
              onChange={next => update(i, next)}
              onSave={() => save(i)}
              onRemove={() => remove(i)}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function TipEditor({
  tip,
  onChange,
  onSave,
  onRemove,
}: {
  tip: LoadingScreenTip;
  onChange: (next: LoadingScreenTip) => void;
  onSave: () => void;
  onRemove: () => void;
}) {
  const toast = useToast();
  const fileInput = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);

  const handleFile = async (file: File) => {
    if (!file.type.startsWith('image/')) {
      toast.push('Please pick an image file', 'error');
      return;
    }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await api<{ imageName: string; imageUrl: string; bytes: number; reused: boolean }>(
        '/loadingscreentips/upload',
        { method: 'POST', formData: fd, timeoutMs: 60_000 },
      );
      onChange({ ...tip, imageName: res.imageName });
      toast.push(res.reused ? 'Image already on file — reused' : `Uploaded (${(res.bytes / 1024).toFixed(0)} KB)`, 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setUploading(false);
      if (fileInput.current) fileInput.current.value = '';
    }
  };

  const preview = imageCdnUrl(tip.imageName, 'width=160&sig=p1');

  return (
    <div className="card !p-4 space-y-3">
      <div className="flex gap-3">
        <div
          className="shrink-0 size-24 rounded bg-ink-900 border border-ink-800 overflow-hidden flex items-center justify-center cursor-pointer hover:border-brand-500"
          onClick={() => fileInput.current?.click()}
          title="Click to upload"
        >
          {preview ? (
            <img src={preview} className="size-full object-cover" alt="" />
          ) : (
            <span className="text-[10px] text-ink-500 px-1 text-center">{uploading ? 'Uploading…' : 'Pick image'}</span>
          )}
        </div>
        <input
          ref={fileInput}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={e => { const f = e.target.files?.[0]; if (f) handleFile(f); }}
        />
        <div className="flex-1 space-y-2">
          <input
            value={tip.title}
            onChange={e => onChange({ ...tip, title: e.target.value })}
            placeholder="Tip title (shown bold on the splash)"
            className="input text-sm w-full"
            maxLength={128}
          />
          <textarea
            value={tip.message}
            onChange={e => onChange({ ...tip, message: e.target.value })}
            placeholder="Tip body (the actual text the player reads)"
            className="input text-xs w-full"
            rows={2}
            maxLength={512}
          />
          <div className="flex gap-1.5 items-center">
            <button
              type="button"
              onClick={() => fileInput.current?.click()}
              disabled={uploading}
              className="btn-secondary text-xs"
            >
              {uploading ? 'Uploading…' : preview ? 'Replace image' : 'Upload image'}
            </button>
            {tip.imageName && (
              <button
                type="button"
                onClick={() => onChange({ ...tip, imageName: '' })}
                className="btn-ghost text-xs text-danger"
              >
                Clear image
              </button>
            )}
            <span className="font-mono text-[11px] text-ink-400 truncate flex-1" title={tip.imageName}>
              {tip.imageName || <span className="italic text-ink-600">no image</span>}
            </span>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-4 gap-2 text-xs">
        <label className="flex flex-col gap-1">
          <span className="text-ink-400">Room scope (csv, optional)</span>
          <input
            value={tip.roomNamesCsv}
            onChange={e => onChange({ ...tip, roomNamesCsv: e.target.value })}
            placeholder="DormRoom,MakerRoom"
            className="input text-xs"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-ink-400">Context (int)</span>
          <input
            type="number"
            value={tip.context}
            onChange={e => onChange({ ...tip, context: parseInt(e.target.value) || 0 })}
            className="input text-xs"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-ink-400">Platform mask</span>
          <input
            type="number"
            value={tip.platformMask}
            onChange={e => onChange({ ...tip, platformMask: parseInt(e.target.value) || -1 })}
            className="input text-xs"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-ink-400">Sort order</span>
          <input
            type="number"
            value={tip.sortOrder}
            onChange={e => onChange({ ...tip, sortOrder: parseInt(e.target.value) || 0 })}
            className="input text-xs"
          />
        </label>
      </div>

      <div className="flex items-center gap-3">
        <label className="flex items-center gap-1.5 text-xs text-ink-300 cursor-pointer">
          <input
            type="checkbox"
            checked={tip.isActive}
            onChange={e => onChange({ ...tip, isActive: e.target.checked })}
          />
          Active (served to watch)
        </label>
        <div className="flex-1" />
        <button onClick={onSave} className="btn-primary text-xs">Save</button>
        <button onClick={onRemove} className="btn-ghost text-xs text-danger" title="Delete tip">
          <Trash />
        </button>
      </div>
    </div>
  );
}
