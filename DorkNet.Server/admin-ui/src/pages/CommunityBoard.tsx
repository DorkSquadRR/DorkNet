import { useEffect, useRef, useState } from 'react';
import { api, get } from '../lib/api';
import type { CommunityBoardState, Player, Room } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Plus, Trash } from '../components/Icons';

export function CommunityBoard({ embedded }: { embedded?: boolean } = {}) {
  const { data: roomList } = useApi<Room[]>('/rooms');
  const { data: players } = useApi<Player[]>('/players?take=500');
  const [state, setState] = useState<CommunityBoardState | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const toast = useToast();

  useEffect(() => {
    get<CommunityBoardState>('/communityboard').then(setState).catch(e => setErr((e as Error).message));
  }, []);

  if (err) return <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">{err}</div>;
  if (!state) return <div className="py-10 text-center text-xs text-ink-400">Loading…</div>;

  const save = async () => {
    setBusy(true);
    try {
      // Server-side `CommunityBoardService.UpdateAsync` replaces every
      // field — null nested objects clear that section. We send the
      // whole state on every save, no PATCH semantics.
      const saved = await api<CommunityBoardState>('/communityboard', { method: 'POST', body: state });
      setState(saved);
      toast.push('Community board saved', 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const saveBtn = <button onClick={save} disabled={busy} className="btn-primary text-xs">{busy ? 'Saving…' : 'Save all'}</button>;

  return (
    <div>
      {embedded ? (
        <div className="flex justify-end mb-3">{saveBtn}</div>
      ) : (
        <PageHeader
          title="Community board"
          blurb="The dorm community panel the watch fetches from /api/communityboard/v1/current. Edits go live instantly."
          actions={saveBtn}
        />
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Announcement state={state} setState={setState} />
        <FeaturedPlayer state={state} setState={setState} players={players ?? []} />
        <FeaturedRoomGroup state={state} setState={setState} rooms={roomList ?? []} />
        <InstagramImages state={state} setState={setState} />
        <Videos state={state} setState={setState} />
      </div>
    </div>
  );
}

// ── Section panels ───────────────────────────────────────────────────

interface SectionProps {
  state: CommunityBoardState;
  setState: (s: CommunityBoardState) => void;
}

function Announcement({ state, setState }: SectionProps) {
  const a = state.currentAnnouncement;
  return (
    <Panel title="Announcement" blurb="Top-of-board banner. Clear to hide.">
      <label className="flex items-center gap-2 text-sm text-ink-200 mb-2">
        <input
          type="checkbox"
          checked={!!a}
          onChange={e => setState({ ...state, currentAnnouncement: e.target.checked ? { message: '', moreInfoUrl: '' } : null })}
          className="size-4 accent-brand-500"
        />
        Show announcement
      </label>
      {a && (
        <div className="space-y-2">
          <Field label="Message">
            <textarea
              value={a.message}
              onChange={e => setState({ ...state, currentAnnouncement: { ...a, message: e.target.value } })}
              rows={2}
              className="input"
            />
          </Field>
          <Field label="More-info URL (optional)">
            <input
              value={a.moreInfoUrl}
              onChange={e => setState({ ...state, currentAnnouncement: { ...a, moreInfoUrl: e.target.value } })}
              className="input"
              placeholder="https://…"
            />
          </Field>
        </div>
      )}
    </Panel>
  );
}

function FeaturedPlayer({ state, setState, players }: SectionProps & { players: Player[] }) {
  const fp = state.featuredPlayer;
  return (
    <Panel title="Featured player" blurb="One spotlit account on the board.">
      <label className="flex items-center gap-2 text-sm text-ink-200 mb-2">
        <input
          type="checkbox"
          checked={!!fp}
          onChange={e => setState({ ...state, featuredPlayer: e.target.checked ? { id: 0, titleOverride: '', urlOverride: '' } : null })}
          className="size-4 accent-brand-500"
        />
        Feature a player
      </label>
      {fp && (
        <div className="space-y-2">
          <Field label="Player">
            <select
              value={fp.id || ''}
              onChange={e => setState({ ...state, featuredPlayer: { ...fp, id: parseInt(e.target.value || '0') } })}
              className="input"
            >
              <option value="">— pick a player —</option>
              {players.map(p => <option key={p.id} value={p.id}>{p.displayName || p.username} · @{p.username} · #{p.id}</option>)}
            </select>
          </Field>
          <Field label="Title override (optional)">
            <input
              value={fp.titleOverride}
              onChange={e => setState({ ...state, featuredPlayer: { ...fp, titleOverride: e.target.value } })}
              className="input"
            />
          </Field>
          <Field label="URL override (optional)">
            <input
              value={fp.urlOverride}
              onChange={e => setState({ ...state, featuredPlayer: { ...fp, urlOverride: e.target.value } })}
              className="input"
              placeholder="https://…"
            />
          </Field>
        </div>
      )}
    </Panel>
  );
}

function FeaturedRoomGroup({ state, setState, rooms }: SectionProps & { rooms: Room[] }) {
  const g = state.featuredRoomGroup;
  return (
    <Panel title="Featured rooms" blurb="Card carousel labeled with the group name.">
      <label className="flex items-center gap-2 text-sm text-ink-200 mb-2">
        <input
          type="checkbox"
          checked={!!g}
          onChange={e => setState({ ...state, featuredRoomGroup: e.target.checked ? { name: '', featuredRooms: [] } : null })}
          className="size-4 accent-brand-500"
        />
        Feature a room group
      </label>
      {g && (
        <div className="space-y-2">
          <Field label="Group name">
            <input value={g.name} onChange={e => setState({ ...state, featuredRoomGroup: { ...g, name: e.target.value } })} className="input" placeholder="Staff picks" />
          </Field>
          <div>
            <div className="label mb-1.5">Rooms ({g.featuredRooms.length})</div>
            <div className="flex flex-col gap-1.5">
              {g.featuredRooms.map((id, i) => (
                <div key={i} className="flex items-center gap-2">
                  <select
                    value={id || ''}
                    onChange={e => {
                      const next = [...g.featuredRooms];
                      next[i] = parseInt(e.target.value || '0');
                      setState({ ...state, featuredRoomGroup: { ...g, featuredRooms: next } });
                    }}
                    className="input flex-1"
                  >
                    <option value="">— pick a room —</option>
                    {rooms.map(r => <option key={r.id} value={r.id}>{r.name} · #{r.id}</option>)}
                  </select>
                  <button
                    type="button"
                    onClick={() => setState({ ...state, featuredRoomGroup: { ...g, featuredRooms: g.featuredRooms.filter((_, j) => j !== i) } })}
                    className="btn-ghost text-xs text-danger"
                  >
                    <Trash />
                  </button>
                </div>
              ))}
              <button
                type="button"
                onClick={() => setState({ ...state, featuredRoomGroup: { ...g, featuredRooms: [...g.featuredRooms, 0] } })}
                className="btn-secondary text-xs self-start"
              >
                <Plus /> Add room
              </button>
            </div>
          </div>
        </div>
      )}
    </Panel>
  );
}

function InstagramImages({ state, setState }: SectionProps) {
  return (
    <Panel title="Instagram images" blurb="Pick an image to upload — the file gets stored as a RoomDataBlob and served via img.localhost. The optional URL field is the click-through target on the board.">
      <div className="flex flex-col gap-2">
        {state.instagramImages.map((img, i) => (
          <InstagramImageRow
            key={i}
            img={img}
            onChange={next => {
              const arr = [...state.instagramImages];
              arr[i] = next;
              setState({ ...state, instagramImages: arr });
            }}
            onRemove={() => setState({ ...state, instagramImages: state.instagramImages.filter((_, j) => j !== i) })}
          />
        ))}
        <button
          type="button"
          onClick={() => setState({ ...state, instagramImages: [...state.instagramImages, { imageName: '', imageUrl: '' }] })}
          className="btn-secondary text-xs self-start"
        >
          <Plus /> Add image
        </button>
      </div>
    </Panel>
  );
}

function InstagramImageRow({
  img,
  onChange,
  onRemove,
}: {
  img: { imageName: string; imageUrl: string };
  onChange: (next: { imageName: string; imageUrl: string }) => void;
  onRemove: () => void;
}) {
  const toast = useToast();
  const fileInput = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);

  const handleFile = async (file: File) => {
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      toast.push('Please pick an image file', 'error');
      return;
    }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await api<{ imageName: string; imageUrl: string; bytes: number; reused: boolean }>(
        '/communityboard/instagram/upload',
        { method: 'POST', formData: fd, timeoutMs: 60_000 },
      );
      onChange({ imageName: res.imageName, imageUrl: img.imageUrl });
      toast.push(res.reused ? 'Image already on file — reused' : `Uploaded (${(res.bytes / 1024).toFixed(0)} KB)`, 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setUploading(false);
      if (fileInput.current) fileInput.current.value = '';
    }
  };

  const apex = typeof window !== 'undefined' && window.location.host.endsWith('localhost') ? 'localhost' : 'rec.net';
  const preview = img.imageName ? `https://img.${apex}/${encodeURIComponent(img.imageName)}?width=128&sig=p1` : null;

  return (
    <div className="rounded border border-ink-800 p-2 flex gap-2 items-start">
      <div
        className="shrink-0 size-16 rounded bg-ink-900 border border-ink-800 overflow-hidden flex items-center justify-center cursor-pointer hover:border-brand-500"
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
      <div className="flex-1 flex flex-col gap-1.5">
        <div className="flex gap-1.5 items-center">
          <button
            type="button"
            onClick={() => fileInput.current?.click()}
            disabled={uploading}
            className="btn-secondary text-xs"
          >
            {uploading ? 'Uploading…' : preview ? 'Replace' : 'Upload'}
          </button>
          <span className="font-mono text-[11px] text-ink-400 truncate flex-1" title={img.imageName}>
            {img.imageName || <span className="italic text-ink-600">no image yet</span>}
          </span>
        </div>
        <input
          value={img.imageUrl}
          onChange={e => onChange({ ...img, imageUrl: e.target.value })}
          placeholder="https://instagram.com/p/… (click-through URL)"
          className="input text-xs"
        />
      </div>
      <button
        type="button"
        onClick={onRemove}
        className="btn-ghost text-xs text-danger self-start"
      >
        <Trash />
      </button>
    </div>
  );
}

function Videos({ state, setState }: SectionProps) {
  return (
    <Panel
      title="Videos"
      blurb="Upload an .mp4/.webm/.mov (≤100 MB) plus a thumbnail image — the files land in RoomDataBlobs and are served from cdn / img. Or skip the upload and paste a SourceUrl pointing at YouTube / etc. for an externally-hosted video."
    >
      <div className="flex flex-col gap-3">
        {state.videos.map((v, i) => (
          <VideoRow
            key={i}
            video={v}
            onChange={next => {
              const arr = [...state.videos];
              arr[i] = next;
              setState({ ...state, videos: arr });
            }}
            onRemove={() => setState({ ...state, videos: state.videos.filter((_, j) => j !== i) })}
          />
        ))}
        <button
          type="button"
          onClick={() => setState({ ...state, videos: [...state.videos, { blobName: '', title: '', description: '', thumbnailBlobName: '', sourceUrl: '' }] })}
          className="btn-secondary text-xs self-start"
        >
          <Plus /> Add video
        </button>
      </div>
    </Panel>
  );
}

// Capture a still frame from a video File and return it as a JPEG Blob.
// Strategy: spin up an off-DOM <video> element, point it at a blob URL
// so the bytes never leave the page, wait for the first decoded frame
// (`loadeddata`), nudge currentTime so most encoders give us a real
// I-frame (some MP4s render black at t=0 because the first sample is
// only a P-frame referencing nothing), draw to a canvas, then export.
//
// Width is capped at 1280 so we don't end up with a 30-MB poster image
// from a 4K source — the watch renders thumbnails at ~256 px anyway.
// Returns null if the browser refuses to decode (Chrome on Linux occas-
// ionally chokes on H.265/HEVC; H.264 + VP9 cover the rest).
async function captureFirstFrame(videoFile: File): Promise<Blob | null> {
  const url = URL.createObjectURL(videoFile);
  try {
    const video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.crossOrigin = 'anonymous';
    video.preload = 'auto';
    video.src = url;
    await new Promise<void>((resolve, reject) => {
      const onReady = () => { cleanup(); resolve(); };
      const onError = () => { cleanup(); reject(new Error('video decode failed')); };
      const cleanup = () => {
        video.removeEventListener('loadeddata', onReady);
        video.removeEventListener('error', onError);
      };
      video.addEventListener('loadeddata', onReady);
      video.addEventListener('error', onError);
    });
    // Seek a tenth of a second in — t=0 frames are often black with
    // certain encoders. Wait for `seeked` so we draw real bytes.
    if (video.duration && Number.isFinite(video.duration) && video.duration > 0.2) {
      await new Promise<void>((resolve) => {
        const onSeeked = () => { video.removeEventListener('seeked', onSeeked); resolve(); };
        video.addEventListener('seeked', onSeeked);
        video.currentTime = Math.min(0.1, video.duration / 2);
      });
    }
    const maxWidth = 1280;
    const ratio = Math.min(1, maxWidth / video.videoWidth);
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(2, Math.round(video.videoWidth * ratio));
    canvas.height = Math.max(2, Math.round(video.videoHeight * ratio));
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    return await new Promise<Blob | null>(resolve =>
      canvas.toBlob(b => resolve(b), 'image/jpeg', 0.88));
  } finally {
    URL.revokeObjectURL(url);
  }
}

// Single editable row for a community-board video. Mirrors
// InstagramImageRow's click-to-upload pattern with two pickers (the
// video itself + a thumbnail). The video upload caps at 100 MB to dodge
// Cloudflare's edge limit on non-chunked POSTs — anything larger should
// be hosted externally and pasted into SourceUrl.
function VideoRow({
  video, onChange, onRemove,
}: {
  video: { blobName: string; title: string; description: string; thumbnailBlobName: string; sourceUrl: string };
  onChange: (next: typeof video) => void;
  onRemove: () => void;
}) {
  const toast = useToast();
  const videoInput = useRef<HTMLInputElement>(null);
  const thumbInput = useRef<HTMLInputElement>(null);
  const [uploadingVideo, setUploadingVideo] = useState(false);
  const [uploadingThumb, setUploadingThumb] = useState(false);
  // Auto-grab the first frame of the uploaded video and use it as the
  // thumbnail. On by default — admins almost always want this and can
  // still upload a custom thumbnail afterwards (the custom one wins
  // because it overwrites thumbnailBlobName).
  const [autoThumb, setAutoThumb] = useState(true);

  const apex = typeof window !== 'undefined' && window.location.host.endsWith('localhost') ? 'localhost' : 'rec.net';
  const thumbPreview = video.thumbnailBlobName ? `https://img.${apex}/${encodeURIComponent(video.thumbnailBlobName)}?width=160&sig=p1` : null;
  // The watch fetches videos under cdn.{apex}/video/<BlobName>
  // (RecNet/CommunityBoard.txt:1068 — `String.Concat("/video/", BlobName)`).
  // Match that path here so the inline admin preview hits the same
  // route the watch does; previously we built a bare `cdn.{apex}/<name>`
  // URL which 404'd because CdnController's catch-all regex didn't
  // include video extensions.
  const videoUrl = video.blobName ? `https://cdn.${apex}/video/${encodeURIComponent(video.blobName)}` : null;

  // Video file picker → POST /communityboard/video/upload (timeout
  // generous: 100 MB at 5 MB/s is 20s of body time alone, plus S3
  // PutObject latency on cold cache). If the auto-thumbnail toggle is
  // on we extract the first frame BEFORE the upload kicks off — quick
  // (sub-second for most files) and a failure there doesn't block the
  // video upload itself; the admin can always pick a thumbnail by hand.
  const handleVideoFile = async (file: File) => {
    if (!file) return;
    if (!file.type.startsWith('video/') && !/\.(mp4|webm|mov|m4v)$/i.test(file.name)) {
      toast.push('Pick a video file (.mp4 / .webm / .mov / .m4v)', 'error');
      return;
    }
    if (file.size > 100_000_000) {
      toast.push('Max 100 MB — host larger files externally and use the SourceUrl field', 'error');
      return;
    }
    setUploadingVideo(true);
    try {
      // Snapshot the first frame BEFORE we lose the local File reference.
      // Store the resulting Blob — we'll upload it as a thumbnail after
      // the video upload succeeds. Captured up front so a single video
      // upload doesn't hold the spinner open while we extract a frame
      // sequentially.
      let framePromise: Promise<Blob | null> | null = null;
      if (autoThumb) framePromise = captureFirstFrame(file).catch(() => null);

      const fd = new FormData();
      fd.append('file', file);
      const res = await api<{ blobName: string; videoUrl: string; bytes: number; reused: boolean }>(
        '/communityboard/video/upload',
        { method: 'POST', formData: fd, timeoutMs: 300_000 },
      );
      let next = { ...video, blobName: res.blobName };
      toast.push(res.reused ? 'Video already on file — reused' : `Uploaded video (${(res.bytes / 1024 / 1024).toFixed(1)} MB)`, 'success');

      // Upload the captured frame as the thumbnail. Skipped silently if
      // the canvas grab failed (e.g. CORS-tainted decode on some
      // browsers) so the video itself isn't blocked on an optional
      // niceity.
      if (framePromise) {
        const frame = await framePromise;
        if (frame) {
          setUploadingThumb(true);
          try {
            const tfd = new FormData();
            tfd.append('file', new File([frame], `${res.blobName.replace(/\.[a-z0-9]+$/i, '')}.jpg`, { type: 'image/jpeg' }));
            const thumbRes = await api<{ blobName: string; imageUrl: string; bytes: number; reused: boolean }>(
              '/communityboard/videothumb/upload',
              { method: 'POST', formData: tfd, timeoutMs: 60_000 },
            );
            next = { ...next, thumbnailBlobName: thumbRes.blobName };
            toast.push(thumbRes.reused ? 'Frame thumbnail reused' : 'Captured first-frame thumbnail', 'success');
          } catch (e) {
            toast.push(`Auto-thumbnail failed: ${(e as Error).message}`, 'error');
          } finally {
            setUploadingThumb(false);
          }
        } else {
          toast.push('Could not decode a frame for the thumbnail — pick one manually', 'error');
        }
      }
      onChange(next);
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setUploadingVideo(false);
      if (videoInput.current) videoInput.current.value = '';
    }
  };

  // Thumbnail file picker → POST /communityboard/videothumb/upload.
  // Separate endpoint from videos so admins don't accidentally upload a
  // 50 MB poster image (which the watch only renders ~256px wide).
  const handleThumbFile = async (file: File) => {
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      toast.push('Pick an image file for the thumbnail', 'error');
      return;
    }
    setUploadingThumb(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const res = await api<{ blobName: string; imageUrl: string; bytes: number; reused: boolean }>(
        '/communityboard/videothumb/upload',
        { method: 'POST', formData: fd, timeoutMs: 60_000 },
      );
      onChange({ ...video, thumbnailBlobName: res.blobName });
      toast.push(res.reused ? 'Thumbnail already on file — reused' : `Uploaded thumbnail (${(res.bytes / 1024).toFixed(0)} KB)`, 'success');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setUploadingThumb(false);
      if (thumbInput.current) thumbInput.current.value = '';
    }
  };

  return (
    <div className="rounded border border-ink-800 p-3 space-y-2">
      <div className="flex gap-3 items-start">
        {/* Thumbnail tile — click-to-pick like InstagramImageRow */}
        <div
          className="shrink-0 size-20 rounded bg-ink-900 border border-ink-800 overflow-hidden flex items-center justify-center cursor-pointer hover:border-brand-500"
          onClick={() => thumbInput.current?.click()}
          title="Click to upload thumbnail"
        >
          {thumbPreview ? (
            <img src={thumbPreview} className="size-full object-cover" alt="" />
          ) : (
            <span className="text-[10px] text-ink-500 px-1 text-center">{uploadingThumb ? 'Uploading…' : 'Pick thumb'}</span>
          )}
        </div>
        <input
          ref={thumbInput}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={e => { const f = e.target.files?.[0]; if (f) handleThumbFile(f); }}
        />

        <div className="flex-1 flex flex-col gap-1.5">
          <input
            value={video.title}
            onChange={e => onChange({ ...video, title: e.target.value })}
            placeholder="Title"
            className="input text-xs"
          />
          <textarea
            value={video.description}
            onChange={e => onChange({ ...video, description: e.target.value })}
            placeholder="Description"
            rows={2}
            className="input text-xs"
          />
        </div>

        <button
          type="button"
          onClick={onRemove}
          className="btn-ghost text-xs text-danger self-start"
          title="Remove video"
        >
          <Trash />
        </button>
      </div>

      {/* Video file row + native preview if we have a blob to play */}
      <div className="flex flex-col gap-1.5">
        <div className="flex gap-1.5 items-center flex-wrap">
          <button
            type="button"
            onClick={() => videoInput.current?.click()}
            disabled={uploadingVideo}
            className="btn-secondary text-xs"
          >
            {uploadingVideo ? 'Uploading…' : video.blobName ? 'Replace video' : 'Upload video'}
          </button>
          <input
            ref={videoInput}
            type="file"
            accept="video/mp4,video/webm,video/quicktime,.mp4,.webm,.mov,.m4v"
            className="hidden"
            onChange={e => { const f = e.target.files?.[0]; if (f) handleVideoFile(f); }}
          />
          {/* Auto-thumbnail toggle. Lives next to the upload button so
              the admin sets it BEFORE clicking — checking it after the
              upload is too late, the File reference is gone. */}
          <label className="flex items-center gap-1.5 text-[11px] text-ink-300 cursor-pointer" title="Capture the first decoded frame from the uploaded video and store it as the thumbnail.">
            <input
              type="checkbox"
              checked={autoThumb}
              onChange={e => setAutoThumb(e.target.checked)}
              className="size-3.5 accent-brand-500"
            />
            Auto-thumb from first frame
          </label>
          <span className="font-mono text-[11px] text-ink-400 truncate flex-1" title={video.blobName}>
            {video.blobName || <span className="italic text-ink-600">no video uploaded (use SourceUrl for external hosts)</span>}
          </span>
          {video.blobName && (
            <button
              type="button"
              onClick={() => onChange({ ...video, blobName: '' })}
              className="btn-ghost text-[11px] py-0.5 px-1.5 text-danger"
              title="Clear uploaded video"
            >Clear</button>
          )}
        </div>
        {videoUrl && (
          <video
            src={videoUrl}
            controls
            preload="metadata"
            className="w-full max-h-48 rounded bg-black"
          />
        )}
      </div>

      <input
        value={video.sourceUrl}
        onChange={e => onChange({ ...video, sourceUrl: e.target.value })}
        placeholder="SourceUrl — external video URL (YouTube etc.). Leave blank if you uploaded above."
        className="input text-xs"
      />
    </div>
  );
}

function Panel({ title, blurb, children }: { title: string; blurb?: string; children: React.ReactNode }) {
  return (
    <div className="card !p-4">
      <h2 className="text-sm font-semibold text-ink-50">{title}</h2>
      {blurb && <p className="text-xs text-ink-400 mb-3">{blurb}</p>}
      <div>{children}</div>
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
