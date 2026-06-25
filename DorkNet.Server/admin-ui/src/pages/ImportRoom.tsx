import { useEffect, useMemo, useRef, useState } from 'react';
import { BlobReader, TextWriter, ZipReader, type Entry } from '@zip.js/zip.js';
import { ApiError, api, uploadZipInChunks, type UploadProgress } from '../lib/api';
import { isChromium } from '../lib/browser';
import type { Player } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';
import { Upload, Trash, RefreshCw } from '../components/Icons';

// Matches RecNet's real export layout (see comments in
// Controllers/Admin/RoomZipImportController.cs for the schema):
//
//   Rooms/Room_<id>_<name>_<ts>/
//     RoomDetails.json
//     RoomImage.jpg
//     Promo/Image/PromoImage_*.jpg
//     SubRooms/<SceneName>/
//       Subroom.json
//       <hash>.room          (referenced by Subroom.CurrentSave.DataBlob)
//       AudioSampler/<hash>.htr
//       Holotar/<hash>.htr
//       Image/PVImage_*.jpg
//   Inventions/Invention_<id>_<name>_<ts>/
//     Invention.json
//     InventionDetails.json
//     InventionVersion.json
//     InventionImage.jpg
//     <hash>.inv
//
// Studio single-room dump zips are also accepted directly:
//
//   <RoomName>__<RoomId>/
//     room.json
//     manifest.json
//     photos/main.jpg
//     <SubRoomName>__<SubRoomId>/saves/<SaveId>.json
//     <SubRoomName>__<SubRoomId>/saves/<SaveId>__data.room
//     <SubRoomName>__<SubRoomId>/saves/<SaveId>__ref_<hash>.htr|jpg|png
//
// We parse the archive client-side purely for a preview/validation
// pass — the same blob is then POSTed to the server, which re-parses
// it authoritatively.

interface RoomDetailsJson {
  RoomId?: number;
  Name?: string;
  FriendlyName?: string;
  Description?: string;
  ImageName?: string;
  CreatedAt?: string;
  PublishedAt?: string;
  ModifiedAt?: string;
  Stats?: { CheerCount?: number; VisitCount?: number; VisitorCount?: number; FavoriteCount?: number };
  Tags?: Array<{ Tag?: string; Type?: number }>;
  SubRooms?: Array<{ Name?: string; CurrentSave?: { DataBlob?: string }; IsSandbox?: boolean; MaxPlayers?: number; ModifiedAt?: string }>;
}

/// The 2020 Rec Room watch was finalized around September 2020; rooms
/// authored after end-of-2020 commonly reference circuits, props, and
/// UI components added in 2021+ builds that the 2020 binary can't
/// resolve. Importing them still produces a row in the DB but the
/// scene either loads with missing chunks or crashes the watch at
/// load time. We don't refuse the import — just flag in the preview
/// so admins know to expect breakage and can untick those rooms.
const CLIENT_BUILD_CUTOFF = new Date('2020-12-31T00:00:00Z');

interface SubroomJson {
  Name?: string;
  CurrentSave?: { DataBlob?: string };
  IsSandbox?: boolean;
  MaxPlayers?: number;
  UnitySceneId?: string;
  ModifiedAt?: string;
}

interface InventionJson {
  Name?: string;
  Description?: string;
  ImageName?: string;
  CurrentVersionNumber?: number;
}

interface PreviewRoom {
  folder: string;
  name: string;
  description: string;
  tags: string[];
  stats: { visits: number; visitors: number; cheers: number; favorites: number };
  subrooms: PreviewSubroom[];
  hasImage: boolean;
  promoImageCount: number;
  /// Latest of CreatedAt / PublishedAt / ModifiedAt / per-subroom
  /// ModifiedAt. Used to flag rooms newer than the 2020 client's
  /// release window — see CLIENT_BUILD_CUTOFF.
  latestModifiedAt: string | null;
  postBuildCutoff: boolean;
  /// True when this room came from a Studio single-room dump (room.json +
  /// SubRooms/<scene>/ saves) rather than a standard RoomDetails.json export.
  isStudio: boolean;
}

interface PreviewSubroom {
  scene: string;
  blobFilename: string | null;
  blobFound: boolean;
  htrAssetCount: number;
  pvImageCount: number;
  polaroidCount: number;
  hasSubroomJson: boolean;
}

interface PreviewInvention {
  folder: string;
  name: string;
  description: string;
  hasBlob: boolean;
  hasImage: boolean;
  versionNumber: number;
}

interface ZipPreview {
  rooms: PreviewRoom[];
  inventions: PreviewInvention[];
  unknownTopLevel: string[];
}

interface AssetBreakdown { referenced: number; newlyImported: number; alreadyInDb: number }

interface ImportResultRoom {
  name: string;
  ok: boolean;
  error?: string;
  roomId?: number;
  folder?: string;
  sceneCount?: number;
  entryScene?: string;
  entryDetectionSource?: string;
  htrAssets?: AssetBreakdown;
  pvImages?: AssetBreakdown;
  polaroids?: AssetBreakdown;
  history?: AssetBreakdown & { skipped?: number };
  // Legacy flat counters — keep reading them as a fallback in case
  // we point this UI at an older server build.
  htrAssetsImported?: number;
  pvImagesImported?: number;
  missingScenes?: string[] | null;
  tags?: string[];
  stats?: { cheers: number; favorites: number; visitors: number; visits: number };
  scenes?: Array<{ name: string; blobName: string; bytes: number; htrAssets: number; pvImages: number; isSandbox?: boolean; maxPlayers?: number }>;
}

interface ImportResultInvention {
  name: string;
  ok: boolean;
  error?: string;
  inventionId?: number;
  folder?: string;
  blobName?: string;
  bytes?: number;
  tags?: string[];
}

interface ImportResult {
  archiveBytes: number;
  roomCount: number;
  inventionCount: number;
  rooms: ImportResultRoom[];
  inventions: ImportResultInvention[];
}

// Stages for the visible upload progress strip. "parsing" lasts until
// the client-side zip preview lands; "uploading" tracks bytes-on-wire;
// "server" is the indeterminate window between upload completion and
// the server response (during which the server is walking the archive,
// normalising blobs, inserting rows). "done" stays terminal until the
// user clears the file.
type ImportStage = 'idle' | 'parsing' | 'uploading' | 'server' | 'done';

export function ImportRoom() {
  const chromium = useMemo(() => isChromium(), []);
  const { data: players } = useApi<Player[]>('/players?take=500');
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ZipPreview | null>(null);
  const [previewErr, setPreviewErr] = useState<string | null>(null);
  const [parseProgress, setParseProgress] = useState(0);          // 0..100 — number of entries we've inspected
  const [ownerId, setOwnerId] = useState<number | null>(null);
  // Per-folder selection. Initialised to "everything checked except
  // post-2020-cutoff rooms" once the preview lands — admins typically
  // want to import everything compatible by default but skip the
  // flagged ones until they've vetted them. The Set holds folder paths
  // (Rooms/Room_*… or Inventions/Invention_*…).
  const [selectedFolders, setSelectedFolders] = useState<Set<string>>(new Set());
  // Per-import "round-trip the .room bytes through the 2020 protobuf
  // normaliser" toggle. Defaults OFF because the re-encode crashes the
  // watch on load; flip on to opt back into the normaliser for a
  // specific zip once we're confident its bytes survive the round-trip.
  const [normalizeBlobs, setNormalizeBlobs] = useState(true);
  // "Wipe duplicates first" — POST every selected room name + invention
  // id from the preview to /import/wipe-targets before kicking off the
  // upload. Defaults ON because the zip-recovery workflow re-imports the
  // same room with refreshed .inv bytes most of the time, and without
  // the wipe (1) the room rejects with duplicate_name and (2) every
  // invention auto-ids to a fresh row, leaving placement refs in the
  // room blob pointing at orphans. Admins who really want a no-wipe
  // import can flip this off explicitly.
  const [wipeFirst, setWipeFirst] = useState(true);
  const [stage, setStage] = useState<ImportStage>('idle');
  const [uploadProgress, setUploadProgress] = useState<UploadProgress>({ loaded: 0, total: 0, percent: 0 });
  const [serverStartedAt, setServerStartedAt] = useState<number | null>(null);
  const [serverElapsed, setServerElapsed] = useState(0);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [drag, setDrag] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const toast = useToast();

  // Tick a clock while the server is processing so the user knows
  // something is happening — large archives can spend 10s+ in
  // System.IO.Compression + EF inserts with no wire traffic.
  useEffect(() => {
    if (stage !== 'server' || serverStartedAt === null) return;
    setServerElapsed(0);
    const id = window.setInterval(() => setServerElapsed(Math.round((Date.now() - serverStartedAt) / 1000)), 250);
    return () => window.clearInterval(id);
  }, [stage, serverStartedAt]);

  useEffect(() => {
    if (!file) { setPreview(null); setPreviewErr(null); setStage('idle'); setSelectedFolders(new Set()); return; }
    let cancelled = false;
    (async () => {
      setStage('parsing');
      setPreviewErr(null);
      setParseProgress(0);
      try {
        const p = await parseZipPreview(file, pct => { if (!cancelled) setParseProgress(pct); });
        if (!cancelled) {
          setPreview(p);
          setStage('idle');
          // Default selection: every importable room + invention,
          // EXCEPT rooms flagged as post-2020-cutoff (admin can tick
          // them back on explicitly after acknowledging the warning).
          const next = new Set<string>();
          for (const r of p.rooms) if (!r.postBuildCutoff) next.add(r.folder);
          for (const inv of p.inventions) next.add(inv.folder);
          setSelectedFolders(next);
        }
      } catch (e) {
        if (!cancelled) { setPreview(null); setPreviewErr((e as Error).message); setStage('idle'); }
      }
    })();
    return () => { cancelled = true; };
  }, [file]);

  const toggleFolder = (folder: string) => {
    setSelectedFolders(prev => {
      const next = new Set(prev);
      if (next.has(folder)) next.delete(folder); else next.add(folder);
      return next;
    });
  };

  const upload = async () => {
    if (!file || !ownerId) return;
    setResult(null);

    // Optional pre-flight wipe — clears the DB rows that would conflict
    // with this import so a re-upload after a recovery pass cleanly
    // overwrites instead of producing duplicate-named rooms and
    // auto-id-on-collision inventions. Folder names embed the original
    // Rec Room InventionId: `Invention_<id>_RecRoom.InventionData`.
    if (wipeFirst && preview) {
      const inventionIds = (preview.inventions ?? [])
        .filter(inv => selectedFolders.has(inv.folder))
        .map(inv => {
          const m = inv.folder.match(/Invention_(\d+)_/);
          return m ? Number(m[1]) : NaN;
        })
        .filter(n => Number.isFinite(n));
      const roomNames = (preview.rooms ?? [])
        .filter(r => selectedFolders.has(r.folder))
        .map(r => r.name);

      if (inventionIds.length > 0 || roomNames.length > 0) {
        try {
          const wipe = await api<{
            inventionsDeleted: number; inventionsNotFound: number; inventionBlobsRemoved: number;
            roomsDeleted: number; roomsNotFound: number; roomBlobsRemoved: number; roomScenesRemoved: number;
            skippedRooms?: Array<{ id: number; name: string; reason: string }>;
          }>('/import/wipe-targets', {
            method: 'POST',
            body: { InventionIds: inventionIds, RoomNames: roomNames },
          });
          const skipped = wipe.skippedRooms?.length ?? 0;
          toast.push(
            `Wiped ${wipe.roomsDeleted} room${wipe.roomsDeleted === 1 ? '' : 's'} + ${wipe.inventionsDeleted} invention${wipe.inventionsDeleted === 1 ? '' : 's'}`
            + (skipped > 0 ? ` (${skipped} room${skipped === 1 ? '' : 's'} preserved — dorms/seeded)` : ''),
            'info',
          );
        } catch (e) {
          const msg = e instanceof ApiError ? e.message : (e as Error).message;
          toast.push(`Wipe failed (continuing anyway): ${msg}`, 'error');
        }
      }
    }

    setStage('uploading');
    setUploadProgress({ loaded: 0, total: file.size, percent: 0 });
    try {
      // Chunked upload — Cloudflare's edge caps request bodies at
      // 100 MB on free/pro plans, so even our 165 MB sample zip needs
      // splitting. The helper slices into ZIP_CHUNK_BYTES (50 MB) pieces
      // sent to /zip-upload-chunk, then calls /zip-upload-finalize which
      // runs the same import logic the single-shot endpoint does.
      // Split the selection into the two server-side filters. Empty
      // arrays mean "don't filter" on the server, so if the admin
      // unticked everything in a category we send a sentinel ([""])
      // that matches no real folder and effectively imports nothing
      // from that category.
      const selectedRoomFolders = (preview?.rooms ?? [])
        .filter(r => selectedFolders.has(r.folder))
        .map(r => r.folder);
      const selectedInventionFolders = (preview?.inventions ?? [])
        .filter(inv => selectedFolders.has(inv.folder))
        .map(inv => inv.folder);
      const anyRoomSelected = (preview?.rooms.length ?? 0) > 0 && selectedRoomFolders.length === 0;
      const anyInventionSelected = (preview?.inventions.length ?? 0) > 0 && selectedInventionFolders.length === 0;
      const res = await uploadZipInChunks<ImportResult>(
        file,
        ownerId,
        p => {
          setUploadProgress(p);
          if (p.total > 0 && p.loaded >= p.total) {
            setStage(prev => (prev === 'uploading' ? 'server' : prev));
            setServerStartedAt(prev => prev ?? Date.now());
          }
        },
        30 * 60_000,
        {
          selectedRoomFolders: anyRoomSelected ? ['__NONE__'] : selectedRoomFolders,
          selectedInventionFolders: anyInventionSelected ? ['__NONE__'] : selectedInventionFolders,
          normalizeBlobs,
        },
      );
      setStage('done');
      setResult(res);
      const ok = res.rooms.filter(r => r.ok).length;
      toast.push(
        `Imported ${ok}/${res.rooms.length} rooms · ${res.inventions.filter(i => i.ok).length}/${res.inventions.length} inventions`,
        ok === res.rooms.length ? 'success' : 'info',
      );
    } catch (e) {
      setStage('idle');
      const msg = e instanceof ApiError ? e.message : (e as Error).message;
      toast.push(msg, 'error');
    } finally {
      setServerStartedAt(null);
    }
  };

  const busy = stage === 'uploading' || stage === 'server';
  const previewBusy = stage === 'parsing';

  const validRooms = preview?.rooms.filter(r => r.subrooms.some(s => s.blobFound)) ?? [];
  const totalScenes = validRooms.reduce((n, r) => n + r.subrooms.length, 0);
  const totalHtr = validRooms.reduce((n, r) => n + r.subrooms.reduce((m, s) => m + s.htrAssetCount, 0), 0);
  const totalPolaroids = validRooms.reduce((n, r) => n + r.subrooms.reduce((m, s) => m + s.polaroidCount, 0), 0);

  return (
    <div>
      <PageHeader
        title="Import room (zip)"
        blurb="Drop a RecNet-format export archive. Each Rooms/Room_*/ folder is one room; SubRooms/<Scene>/ holds the scene blob + bundled .htr assets + preview images. The original RoomDetails.json + Subroom.json drive everything — no rec.net round-trip."
        actions={<a href="/import-room-legacy" className="btn-ghost text-xs">Use legacy importer →</a>}
      />

      {!chromium && (
        <div className="card !p-4 mb-4 border-warn/40 bg-warn/10">
          <div className="flex items-start gap-3">
            <div className="text-warn shrink-0 mt-0.5">⚠</div>
            <div>
              <div className="text-sm font-semibold text-warn">Chromium-based browser required</div>
              <p className="text-xs text-ink-300 mt-1 max-w-prose">
                The zip importer uploads multi-hundred-megabyte archives via <code className="font-mono text-[11px]">FormData</code> and uses streaming Blob slicing. Firefox and Safari both choke on this — Firefox's upload progress buffers
                stall over ~500 MB, and Safari hits a memory ceiling cloning the form body and aborts mid-upload. Use Chrome, Edge, Brave, Vivaldi, or any other Chromium-based browser for this page. (The rest of the admin works fine everywhere.)
              </p>
            </div>
          </div>
        </div>
      )}

      <div
        onDragOver={e => { e.preventDefault(); setDrag(true); }}
        onDragLeave={() => setDrag(false)}
        onDrop={e => {
          e.preventDefault();
          setDrag(false);
          if (!chromium) { toast.push('Switch to a Chromium-based browser first', 'error'); return; }
          const f = e.dataTransfer.files?.[0];
          if (f && f.name.toLowerCase().endsWith('.zip')) {
            setFile(f); setResult(null);
          } else if (f) {
            toast.push('Drop a .zip file', 'error');
          }
        }}
        className={`card !p-8 mb-4 border-2 border-dashed transition-colors ${
          drag ? 'border-brand-500 bg-brand-500/5' : 'border-ink-700 bg-ink-900/40'
        }`}
      >
        <div className="flex flex-col items-center justify-center gap-3 text-center">
          <Upload className="size-8 text-ink-400" />
          <div>
            <div className="text-sm font-medium text-ink-100">Drop your archive here, or pick a file</div>
            <div className="text-xs text-ink-400 mt-0.5">.zip up to ~2 GB</div>
          </div>
          <input
            ref={fileInputRef}
            type="file"
            accept=".zip,application/zip"
            disabled={!chromium}
            onChange={e => { setFile(e.target.files?.[0] ?? null); setResult(null); }}
            className="hidden"
          />
          <div className="flex gap-2">
            <button onClick={() => fileInputRef.current?.click()} disabled={!chromium} className="btn-secondary text-xs">Browse…</button>
            {file && (
              <button
                onClick={() => { setFile(null); setPreview(null); setResult(null); if (fileInputRef.current) fileInputRef.current.value = ''; }}
                className="btn-ghost text-xs text-danger"
              >
                <Trash /> Clear
              </button>
            )}
          </div>
          {file && (
            <div className="text-xs text-ink-300 font-mono">
              {file.name} · {(file.size / 1024 / 1024).toFixed(1)} MB
            </div>
          )}
        </div>
      </div>

      {previewBusy && (
        <div className="card !p-4 mb-4">
          <div className="flex items-center gap-3 text-xs text-ink-300 mb-2">
            <RefreshCw className="animate-spin" />
            <span>Parsing archive — reading the central directory and walking room manifests…</span>
            <span className="ml-auto tabular-nums">{Math.min(100, parseProgress)}%</span>
          </div>
          <ProgressBar percent={parseProgress} />
        </div>
      )}

      {previewErr && (
        <div className="card border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger mb-4">
          Failed to parse archive: {previewErr}
        </div>
      )}

      {/* Live import status strip — appears while we're uploading the
          archive and then while the server processes it. */}
      {busy && (
        <div className="card !p-4 mb-4">
          {stage === 'uploading' && (
            <>
              <div className="flex items-center gap-3 text-xs text-ink-200 mb-2">
                <RefreshCw className="animate-spin text-brand-300" />
                <span className="font-medium">Uploading archive…</span>
                <span className="ml-auto tabular-nums text-ink-300">
                  {(uploadProgress.loaded / 1024 / 1024).toFixed(1)} / {(uploadProgress.total / 1024 / 1024).toFixed(1)} MB
                  {' · '}{uploadProgress.percent}%
                </span>
              </div>
              <ProgressBar percent={uploadProgress.percent} />
            </>
          )}
          {stage === 'server' && (
            <>
              <div className="flex items-center gap-3 text-xs text-ink-200 mb-2">
                <RefreshCw className="animate-spin text-brand-300" />
                <span className="font-medium">Server is processing the archive…</span>
                <span className="ml-auto tabular-nums text-ink-300">{serverElapsed}s elapsed</span>
              </div>
              {uploadProgress.phase && (
                <p className="text-[11px] text-brand-200 mb-2 font-mono truncate">
                  {uploadProgress.phase}
                </p>
              )}
              <p className="text-[11px] text-ink-400 mb-2">
                Unpacking, normalising every <code className="font-mono">.room</code> blob, inserting room + scene rows, persisting bundled <code className="font-mono">.htr</code> assets and preview images.
                Multi-GB archives can take several minutes; the importer now runs as a background job and the SPA polls for status, so Cloudflare won't drop the connection mid-import.
              </p>
              <ProgressBar indeterminate />
            </>
          )}
        </div>
      )}

      {preview && (
        <div className="card !p-5 mb-4">
          <div className="grid grid-cols-2 sm:grid-cols-5 gap-3 mb-4">
            <Stat label="Rooms" value={preview.rooms.length} />
            <Stat label="Scenes" value={totalScenes} />
            <Stat label=".htr assets" value={totalHtr} />
            <Stat label="Polaroids" value={totalPolaroids} />
            <Stat label="Inventions" value={preview.inventions.length} />
          </div>

          {preview.rooms.length > 0 && (
            <details open className="mb-3">
              <summary className="cursor-pointer text-sm font-semibold text-ink-100 mb-2 flex items-center gap-3">
                <span>Rooms ({preview.rooms.filter(r => selectedFolders.has(r.folder)).length}/{preview.rooms.length} selected)</span>
                <button
                  type="button"
                  onClick={(e) => {
                    e.preventDefault();
                    const allOn = preview.rooms.every(r => selectedFolders.has(r.folder));
                    setSelectedFolders(prev => {
                      const next = new Set(prev);
                      for (const r of preview.rooms) {
                        if (allOn) next.delete(r.folder); else next.add(r.folder);
                      }
                      return next;
                    });
                  }}
                  className="btn-ghost text-[10px] uppercase tracking-widest"
                >
                  {preview.rooms.every(r => selectedFolders.has(r.folder)) ? 'Deselect all' : 'Select all'}
                </button>
              </summary>
              <div className="space-y-2">
                {preview.rooms.map(r => (
                  <div
                    key={r.folder}
                    className={`rounded-lg border p-3 transition-colors ${
                      selectedFolders.has(r.folder)
                        ? 'border-ink-800'
                        : 'border-ink-800/50 bg-ink-900/30 opacity-70'
                    }`}
                  >
                    <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                      <label className="flex items-center gap-2 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={selectedFolders.has(r.folder)}
                          onChange={() => toggleFolder(r.folder)}
                          className="size-4 accent-brand-500"
                        />
                        <span className="font-medium text-ink-50">{r.name}</span>
                      </label>
                      <span className="text-xs text-ink-500 font-mono">{r.folder.split('/').pop()}</span>
                      {r.isStudio && (
                        <span className="badge-online" title="Imported from a RR Studio single-room dump (room.json + SubRooms/<scene>/ saves).">
                          Studio
                        </span>
                      )}
                      {!r.hasImage && <span className="badge-junior">no image</span>}
                      {r.postBuildCutoff && (
                        <span
                          className="badge-banned"
                          title={`Latest timestamp ${r.latestModifiedAt} is after the 2020 client cutoff (${CLIENT_BUILD_CUTOFF.toISOString().slice(0, 10)}). Scene may use circuits/props the watch doesn't ship.`}
                        >
                          may be incompatible
                        </span>
                      )}
                      {r.latestModifiedAt && (
                        <span className="text-[10px] text-ink-500 font-mono" title={r.latestModifiedAt}>
                          {r.latestModifiedAt.slice(0, 10)}
                        </span>
                      )}
                    </div>
                    {r.description && <p className="text-xs text-ink-400 mt-1 line-clamp-2">{r.description}</p>}
                    <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
                      {r.tags.map(t => <span key={t} className="badge-neutral">{t}</span>)}
                    </div>
                    <div className="mt-2 grid grid-cols-2 md:grid-cols-5 gap-2 text-xs text-ink-300">
                      <Mini label="Scenes" value={r.subrooms.length} />
                      <Mini label="HTR" value={r.subrooms.reduce((n, s) => n + s.htrAssetCount, 0)} />
                      <Mini label="PV imgs" value={r.subrooms.reduce((n, s) => n + s.pvImageCount, 0)} />
                      <Mini label="Polaroids" value={r.subrooms.reduce((n, s) => n + s.polaroidCount, 0)} />
                      <Mini label="Promo" value={r.promoImageCount} />
                    </div>
                    {r.subrooms.length > 0 && (
                      <details className="mt-2">
                        <summary className="cursor-pointer text-xs text-ink-400">Scene breakdown</summary>
                        <table className="w-full text-xs mt-1.5">
                          <thead className="text-[10px] uppercase tracking-wider text-ink-500">
                            <tr>
                              <th className="text-left font-medium pb-1">Scene</th>
                              <th className="text-left font-medium pb-1">Blob</th>
                              <th className="text-right font-medium pb-1">HTR</th>
                              <th className="text-right font-medium pb-1">PV</th>
                              <th className="text-right font-medium pb-1">Pol</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-ink-800">
                            {r.subrooms.map(s => (
                              <tr key={s.scene}>
                                <td className="py-1 text-ink-100">{s.scene}</td>
                                <td className="py-1 font-mono text-[11px] text-ink-300">
                                  {s.blobFilename ?? <span className="text-danger">no DataBlob in Subroom.json</span>}
                                  {s.blobFilename && !s.blobFound && <span className="text-danger ml-1">(missing!)</span>}
                                  {!s.hasSubroomJson && <span className="text-danger ml-1">(no Subroom.json)</span>}
                                </td>
                                <td className="py-1 text-right text-ink-300 tabular-nums">{s.htrAssetCount}</td>
                                <td className="py-1 text-right text-ink-300 tabular-nums">{s.pvImageCount}</td>
                                <td className="py-1 text-right text-ink-300 tabular-nums">{s.polaroidCount}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </details>
                    )}
                  </div>
                ))}
              </div>
            </details>
          )}

          {preview.inventions.length > 0 && (
            <details className="mb-3">
              <summary className="cursor-pointer text-sm font-semibold text-ink-100 flex items-center gap-3">
                <span>Inventions ({preview.inventions.filter(inv => selectedFolders.has(inv.folder)).length}/{preview.inventions.length} selected)</span>
                <button
                  type="button"
                  onClick={(e) => {
                    // Mirror the Rooms section toggle — "select all"
                    // when at least one is unchecked, "deselect all"
                    // once every invention is on. Stops the <details>
                    // collapsing on click via preventDefault.
                    e.preventDefault();
                    const allOn = preview.inventions.every(inv => selectedFolders.has(inv.folder));
                    setSelectedFolders(prev => {
                      const next = new Set(prev);
                      for (const inv of preview.inventions) {
                        if (allOn) next.delete(inv.folder); else next.add(inv.folder);
                      }
                      return next;
                    });
                  }}
                  className="btn-ghost text-[10px] uppercase tracking-widest"
                >
                  {preview.inventions.every(inv => selectedFolders.has(inv.folder)) ? 'Deselect all' : 'Select all'}
                </button>
              </summary>
              <table className="w-full text-sm mt-2">
                <thead className="text-[11px] uppercase tracking-wider text-ink-400 border-b border-ink-800">
                  <tr>
                    <th className="w-8 pb-1" />
                    <th className="text-left font-medium pb-1">Name</th>
                    <th className="text-left font-medium pb-1">Folder</th>
                    <th className="text-right font-medium pb-1">Version</th>
                    <th />
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-800">
                  {preview.inventions.map(inv => (
                    <tr key={inv.folder} className={selectedFolders.has(inv.folder) ? '' : 'opacity-60'}>
                      <td className="py-1.5">
                        <input
                          type="checkbox"
                          checked={selectedFolders.has(inv.folder)}
                          onChange={() => toggleFolder(inv.folder)}
                          className="size-4 accent-brand-500"
                        />
                      </td>
                      <td className="py-1.5 text-ink-100">{inv.name}</td>
                      <td className="py-1.5 font-mono text-xs text-ink-400">{inv.folder.split('/').pop()}</td>
                      <td className="py-1.5 text-right text-ink-200 tabular-nums">{inv.versionNumber}</td>
                      <td className="py-1.5">
                        <div className="flex gap-1">
                          {!inv.hasBlob && <span className="badge-banned">no blob</span>}
                          {!inv.hasImage && <span className="badge-junior">no image</span>}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </details>
          )}

          {preview.unknownTopLevel.length > 0 && (
            <details>
              <summary className="cursor-pointer text-xs text-ink-500">{preview.unknownTopLevel.length} file(s) outside the schema</summary>
              <ul className="text-[11px] text-ink-500 font-mono pl-3 mt-1 max-h-32 overflow-y-auto">
                {preview.unknownTopLevel.map(f => <li key={f}>{f}</li>)}
              </ul>
            </details>
          )}
        </div>
      )}

      {preview && (preview.rooms.length > 0 || preview.inventions.length > 0) && (
        <div className="card !p-4 mb-4 space-y-3">
          <div className="flex flex-wrap items-end gap-2">
            <label className="flex flex-col gap-1 flex-1 min-w-[260px]">
              <span className="label">Owner (becomes the creator of every imported room + invention)</span>
              <select value={ownerId ?? ''} onChange={e => setOwnerId(e.target.value ? parseInt(e.target.value) : null)} disabled={!chromium} className="input">
                <option value="">— pick an owner —</option>
                {(players ?? []).map(p => <option key={p.id} value={p.id}>{p.displayName || p.username} · @{p.username} · #{p.id}</option>)}
              </select>
            </label>
            <button onClick={upload} disabled={busy || !ownerId || !chromium} className="btn-primary text-xs">
              {stage === 'uploading' ? `Uploading… ${uploadProgress.percent}%`
                : stage === 'server'   ? `Server processing… ${serverElapsed}s`
                : (() => {
                    const selectedRoomsCount = preview.rooms.filter(r => selectedFolders.has(r.folder)).length;
                    const selectedInvCount = preview.inventions.filter(inv => selectedFolders.has(inv.folder)).length;
                    const verb = wipeFirst ? 'Wipe & import' : 'Import';
                    return `${verb} ${selectedRoomsCount} room${selectedRoomsCount === 1 ? '' : 's'} · ${selectedInvCount} invention${selectedInvCount === 1 ? '' : 's'}`;
                  })()}
            </button>
          </div>

          {/* "Wipe duplicates first" toggle. Clears DB rows that the
              importer would otherwise reject (duplicate-name rooms) or
              auto-id (existing-id inventions). Defaults ON because the
              zip-recovery workflow is overwhelmingly the common case
              now — admins re-import the same room with refreshed .inv
              bytes from HTTPCache and need a clean slate to avoid
              orphaning placement refs. Off only for first-ever imports
              where there's nothing to clobber anyway, so leaving it on
              is safe in both directions. */}
          <label className="flex items-start gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={wipeFirst}
              onChange={e => setWipeFirst(e.target.checked)}
              disabled={busy}
              className="size-4 mt-0.5 accent-brand-500"
            />
            <span className="text-xs leading-snug">
              <span className="text-ink-100">Wipe duplicates first</span>
              <span className="block text-[11px] text-ink-500 mt-0.5">
                Before uploading, delete any existing rooms by name + invention rows by id that
                match the selected folders. Required when re-importing a refreshed zip — without
                it, room re-imports reject with <code className="font-mono text-[10px]">duplicate_name</code>
                {' '}and invention re-imports auto-id, leaving placement refs in the room blob
                pointing at orphans (visible in-game as missing/empty invention placements).
                Dorms and seeded RR-Originals are preserved regardless.
              </span>
            </span>
          </label>

          {/* Per-import blob-normaliser toggle. Defaults ON now that
              the normaliser projects modern-quaternion TransformData to
              the legacy Euler form the 2020.12 watch reads — without
              that pass, modern-authored rooms render with every shape
              rotated to (0,0,0) (the Studio-import rotation
              "shapes in the wrong place" bug). Leave ON for any
              modern Rec Room export. */}
          <label className="flex items-start gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={normalizeBlobs}
              onChange={e => setNormalizeBlobs(e.target.checked)}
              disabled={busy}
              className="size-4 mt-0.5 accent-brand-500"
            />
            <span className="text-xs leading-snug">
              <span className="text-ink-100">Project modern-quaternion transforms to 2020 Euler + re-encode .room blobs</span>
              <span className="block text-[11px] text-ink-500 mt-0.5">
                Modern Rec Room writes rotations as quaternions (TransformData field 6) that the
                2020.12 watch doesn't read — without projecting them to the legacy Euler
                Vector3 (field 2), every shape loads at rotation (0, 0, 0). Recommended ON
                for any blob exported from 2024+ Rec Room. Disable only if the source blob is
                already 2020-era (e.g. Rockulator) and you want a byte-for-byte passthrough.
              </span>
            </span>
          </label>
        </div>
      )}

      {result && (
        <div className="card !p-5">
          <h2 className="text-sm font-semibold text-ink-50 mb-3">Import result</h2>
          <div className="grid grid-cols-3 gap-3 mb-4 text-sm">
            <Stat label="Archive size" value={`${(result.archiveBytes / 1024 / 1024).toFixed(1)} MB`} />
            <Stat label="Rooms" value={result.roomCount} />
            <Stat label="Inventions" value={result.inventions.length} />
          </div>
          {result.rooms.length > 0 && (
            <>
              <h3 className="text-xs font-semibold uppercase tracking-widest text-ink-300 mb-1.5">Rooms</h3>
              <table className="w-full text-sm mb-4">
                <thead className="text-[11px] uppercase tracking-wider text-ink-400 border-b border-ink-800">
                  <tr>
                    <th className="text-left font-medium pb-1">Room</th>
                    <th className="text-left font-medium pb-1">Result</th>
                    <th className="text-left font-medium pb-1">Entry</th>
                    <th className="text-right font-medium pb-1">Scenes</th>
                    <th className="text-left font-medium pb-1">HTR</th>
                    <th className="text-left font-medium pb-1">PV</th>
                    <th className="text-left font-medium pb-1">Polaroids</th>
                    <th className="text-left font-medium pb-1">History</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-800">
                  {result.rooms.map(r => (
                    <tr key={r.folder ?? r.name}>
                      <td className="py-1.5 text-ink-100">{r.name}{r.roomId !== undefined && <span className="text-ink-500 text-xs"> · #{r.roomId}</span>}</td>
                      <td className="py-1.5">
                        {r.ok
                          ? <span className="badge-online">imported</span>
                          : <span className="badge-banned" title={r.error}>failed{r.error ? ` — ${r.error}` : ''}</span>}
                      </td>
                      <td className="py-1.5 text-xs text-ink-300">
                        {r.entryScene ?? '—'}
                        {r.entryDetectionSource && <span className="text-ink-500"> ({r.entryDetectionSource})</span>}
                      </td>
                      <td className="py-1.5 text-right text-ink-200 tabular-nums">{r.sceneCount ?? '—'}</td>
                      <td className="py-1.5"><AssetCell breakdown={r.htrAssets} legacy={r.htrAssetsImported} /></td>
                      <td className="py-1.5"><AssetCell breakdown={r.pvImages}  legacy={r.pvImagesImported} /></td>
                      <td className="py-1.5"><AssetCell breakdown={r.polaroids} /></td>
                      <td className="py-1.5"><AssetCell breakdown={r.history} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          {result.inventions.length > 0 && (
            <>
              <h3 className="text-xs font-semibold uppercase tracking-widest text-ink-300 mb-1.5">Inventions</h3>
              <table className="w-full text-sm">
                <tbody className="divide-y divide-ink-800">
                  {result.inventions.map(i => (
                    <tr key={i.folder ?? i.name}>
                      <td className="py-1.5 text-ink-100">{i.name}</td>
                      <td className="py-1.5">
                        {i.ok
                          ? <span className="badge-online">invention #{i.inventionId}</span>
                          : <span className="badge-banned">{i.error ?? 'failed'}</span>}
                      </td>
                      <td className="py-1.5 text-xs text-ink-400">{i.blobName}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="card !p-3">
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className="mt-0.5 text-lg font-semibold tabular-nums text-ink-50">
        {typeof value === 'number' ? value.toLocaleString() : value}
      </div>
    </div>
  );
}

// "16 new · 44 already · 60 total" so admins can see at a glance
// whether HTR/PV blobs are present even after a re-import where most
// were already in the DB from a prior run (purge-custom-rooms keeps
// shared assets by design).
function AssetCell({ breakdown, legacy }: { breakdown?: AssetBreakdown; legacy?: number }) {
  if (!breakdown) {
    return <span className="text-ink-300 tabular-nums">{legacy ?? 0}</span>;
  }
  const present = breakdown.newlyImported + breakdown.alreadyInDb;
  if (breakdown.referenced === 0) return <span className="text-ink-500">—</span>;
  const allPresent = present >= breakdown.referenced;
  return (
    <div className="flex items-center gap-1.5 text-xs">
      <span className={`tabular-nums font-medium ${allPresent ? 'text-success' : 'text-warn'}`}>
        {present}/{breakdown.referenced}
      </span>
      <span className="text-ink-500">
        ({breakdown.newlyImported} new
        {breakdown.alreadyInDb > 0 ? ` · ${breakdown.alreadyInDb} kept` : ''})
      </span>
    </div>
  );
}

function Mini({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded border border-ink-800 px-2 py-1">
      <div className="text-[10px] uppercase tracking-widest text-ink-500">{label}</div>
      <div className="text-sm text-ink-100 tabular-nums">{value}</div>
    </div>
  );
}

// Determinate bar when percent is given (0-100); indeterminate uses a
// keyframed sliding strip so the user can see something's happening
// even though we don't know how long the server will take.
function ProgressBar({ percent, indeterminate }: { percent?: number; indeterminate?: boolean }) {
  if (indeterminate) {
    return (
      <div className="relative h-1.5 w-full overflow-hidden rounded-full bg-ink-800">
        <div className="absolute inset-y-0 left-0 w-1/3 rounded-full bg-brand-500 animate-[progress-slide_1.4s_ease-in-out_infinite]" />
        <style>{`@keyframes progress-slide { 0% { transform: translateX(-100%); } 50% { transform: translateX(150%); } 100% { transform: translateX(350%); } }`}</style>
      </div>
    );
  }
  const clamped = Math.max(0, Math.min(100, percent ?? 0));
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-ink-800">
      <div className="h-full rounded-full bg-brand-500 transition-[width] duration-200" style={{ width: `${clamped}%` }} />
    </div>
  );
}

// ── Zip parsing ───────────────────────────────────────────────────────

async function parseZipPreview(file: File, onProgress?: (percent: number) => void): Promise<ZipPreview> {
  const reader = new ZipReader(new BlobReader(file));
  let entries: Entry[];
  try {
    entries = await reader.getEntries();
  } finally {
    await reader.close();
  }
  // ZipReader.getEntries reads the central directory — that's the
  // slow bit. Once we have the entries array, the rest is in-memory
  // map building + small JSON reads for the manifest files. We tick
  // progress as we walk; manifest JSONs read after that bump us to
  // 100%.
  onProgress?.(20);

  // Build a path-indexed view first; the rest of the parse runs against
  // this map, never touching the zip stream again.
  const allPaths = new Set<string>();
  const entryByPath = new Map<string, Entry>();
  for (const e of entries) {
    if (e.directory) continue;
    const p = e.filename.replace(/\\/g, '/').replace(/^\/+/, '');
    allPaths.add(p);
    entryByPath.set(p, e);
  }

  // Count manifests up front so we can advance progress smoothly
  // as we read each one — the central-directory parse and the
  // manifest reads dominate; the rest is O(entries) string work.
  //
  // CRITICAL: the counter has to match what the iteration loops below
  // actually call `bump()` on, or progress overflows past 100%. Invention
  // manifests are accepted at TWO locations (top-level `Inventions/...`
  // AND per-room `Rooms/Room_X/Inventions/...`); originally we only
  // counted the top-level ones, so a zip with many per-room inventions
  // hit 374% on the parse bar. Match the same predicates here.
  const roomManifestPaths: string[] = [];
  const studioRoomManifestPaths: string[] = [];
  const inventionManifestPaths: string[] = [];
  for (const p of allPaths) {
    if (p.startsWith('Rooms/') && p.endsWith('/RoomDetails.json')) {
      roomManifestPaths.push(p);
    } else if (p.endsWith('/room.json') && hasStudioSaveEntries(p.slice(0, -'/room.json'.length), allPaths)) {
      studioRoomManifestPaths.push(p);
    } else if (p.endsWith('/Invention.json')
            && (p.startsWith('Inventions/') || p.includes('/Inventions/'))) {
      inventionManifestPaths.push(p);
    }
  }
  const totalManifests = roomManifestPaths.length + studioRoomManifestPaths.length + inventionManifestPaths.length;
  let processed = 0;
  const bump = () => {
    processed++;
    // Reserve 20% for the central-dir parse already done, leave 10%
    // for the final cleanup, so manifest reads occupy 20..90%. Clamp
    // defensively in case the counters drift again — better to peg the
    // bar at 90% than show "374%" to the user.
    const raw = totalManifests > 0 ? 20 + (processed / totalManifests) * 70 : 90;
    onProgress?.(Math.min(90, Math.round(raw)));
  };

  // Per-room walk. Anchor on Rooms/<folder>/RoomDetails.json.
  const rooms: PreviewRoom[] = [];
  for (const path of allPaths) {
    if (!path.startsWith('Rooms/') || !path.endsWith('/RoomDetails.json')) continue;
    const roomFolder = path.slice(0, -'/RoomDetails.json'.length);

    const detailsEntry = entryByPath.get(path);
    let details: RoomDetailsJson = {};
    if (detailsEntry) {
      try { details = JSON.parse(await readText(detailsEntry)); } catch { /* leave as default */ }
    }

    // Scene folders under SubRooms/
    const sceneRoot = `${roomFolder}/SubRooms/`;
    const sceneNames = new Set<string>();
    for (const p of allPaths) {
      if (!p.startsWith(sceneRoot)) continue;
      const rest = p.slice(sceneRoot.length);
      const slash = rest.indexOf('/');
      if (slash <= 0) continue;
      sceneNames.add(rest.slice(0, slash));
    }

    const subrooms: PreviewSubroom[] = [];
    for (const scene of sceneNames) {
      const sceneFolder = `${sceneRoot}${scene}`;
      const subroomJsonPath = `${sceneFolder}/Subroom.json`;
      let subroomJson: SubroomJson = {};
      const hasSubroomJson = entryByPath.has(subroomJsonPath);
      if (hasSubroomJson) {
        try { subroomJson = JSON.parse(await readText(entryByPath.get(subroomJsonPath)!)); } catch { /* default */ }
      }
      const blobFilename = subroomJson.CurrentSave?.DataBlob ?? null;
      const blobPath = blobFilename ? `${sceneFolder}/${blobFilename}` : null;
      const blobFound = blobPath ? entryByPath.has(blobPath) : false;

      let htrAssetCount = 0;
      let pvImageCount = 0;
      let polaroidCount = 0;
      const imgPrefix = `${sceneFolder}/Image/`;
      const polPrefix = `${sceneFolder}/Polaroids/`;
      // Server-side mirrors this: a `PVImage_<hash>.ext` file in
      // Image/ counts as BOTH a PV image (its own filename) and a
      // polaroid (with the prefix stripped). The watch fetches both
      // URLs, so the importer writes the bytes twice.
      for (const p of allPaths) {
        if (p.startsWith(`${sceneFolder}/AudioSampler/`) && p.endsWith('.htr')) htrAssetCount++;
        else if (p.startsWith(`${sceneFolder}/Holotar/`) && p.endsWith('.htr')) htrAssetCount++;
        else if (p.startsWith(imgPrefix)) {
          pvImageCount++;
          const file = p.slice(imgPrefix.length);
          if (file.toLowerCase().startsWith('pvimage_')) polaroidCount++;
        }
        else if (p.startsWith(polPrefix)) polaroidCount++;
      }
      subrooms.push({ scene, blobFilename, blobFound, htrAssetCount, pvImageCount, polaroidCount, hasSubroomJson });
    }

    // Sort by RoomDetails.SubRooms[] order if present so the preview
    // shows the entry-scene first.
    if (details.SubRooms && details.SubRooms.length > 0) {
      const manifestOrder = details.SubRooms.map(s => s.Name ?? '').filter(Boolean);
      subrooms.sort((a, b) => {
        const ai = manifestOrder.indexOf(a.scene);
        const bi = manifestOrder.indexOf(b.scene);
        if (ai === -1 && bi === -1) return a.scene.localeCompare(b.scene);
        if (ai === -1) return 1;
        if (bi === -1) return -1;
        return ai - bi;
      });
    } else {
      subrooms.sort((a, b) => a.scene.localeCompare(b.scene));
    }

    // Image / promo discovery
    const hasImage = !!details.ImageName && entryByPath.has(`${roomFolder}/${details.ImageName}`)
                  || Array.from(allPaths).some(p => p.startsWith(`${roomFolder}/RoomImage.`));
    let promoImageCount = 0;
    for (const p of allPaths) if (p.startsWith(`${roomFolder}/Promo/Image/`)) promoImageCount++;

    // Pick the most-recent timestamp across the room's own CreatedAt /
    // PublishedAt / ModifiedAt and each subroom's ModifiedAt — the
    // most aggressive signal of "made on a build newer than 2020".
    const candidateDates: number[] = [];
    for (const raw of [details.CreatedAt, details.PublishedAt, details.ModifiedAt]) {
      if (raw) {
        const t = Date.parse(raw);
        if (!Number.isNaN(t)) candidateDates.push(t);
      }
    }
    // We need to re-read the subroom jsons we already parsed above
    // for their ModifiedAt fields. Cheap second pass — preview only
    // parses small JSONs.
    for (const scene of sceneNames) {
      const subroomJsonPath = `${sceneRoot}${scene}/Subroom.json`;
      if (!entryByPath.has(subroomJsonPath)) continue;
      try {
        const sr = JSON.parse(await readText(entryByPath.get(subroomJsonPath)!)) as SubroomJson;
        if (sr.ModifiedAt) {
          const t = Date.parse(sr.ModifiedAt);
          if (!Number.isNaN(t)) candidateDates.push(t);
        }
      } catch { /* skip */ }
    }
    const latestMs = candidateDates.length > 0 ? Math.max(...candidateDates) : null;
    const latestModifiedAt = latestMs !== null ? new Date(latestMs).toISOString() : null;
    const postBuildCutoff = latestMs !== null && latestMs > CLIENT_BUILD_CUTOFF.getTime();

    rooms.push({
      folder: roomFolder,
      name: details.Name ?? details.FriendlyName ?? roomFolder.split('/').pop()!,
      description: details.Description ?? '',
      tags: (details.Tags ?? []).map(t => t.Tag).filter((t): t is string => !!t),
      stats: {
        visits: details.Stats?.VisitCount ?? 0,
        visitors: details.Stats?.VisitorCount ?? 0,
        cheers: details.Stats?.CheerCount ?? 0,
        favorites: details.Stats?.FavoriteCount ?? 0,
      },
      subrooms,
      hasImage,
      promoImageCount,
      latestModifiedAt,
      postBuildCutoff,
      isStudio: false,
    });
    bump();
  }

  // Studio single-room dump walk. Anchor on <RoomName>__<RoomId>/room.json.
  for (const path of studioRoomManifestPaths) {
    const roomFolder = path.slice(0, -'/room.json'.length);
    let details: RoomDetailsJson = {};
    try { details = JSON.parse(await readText(entryByPath.get(path)!)); } catch { /* leave as default */ }

    const roomRoot = `${roomFolder}/`;
    const sceneFolders = new Set<string>();
    for (const p of allPaths) {
      if (!p.startsWith(roomRoot)) continue;
      const rest = p.slice(roomRoot.length);
      const slash = rest.indexOf('/');
      if (slash <= 0) continue;
      const scene = rest.slice(0, slash);
      if (rest.slice(slash + 1).startsWith('saves/')) sceneFolders.add(scene);
    }

    const subrooms: PreviewSubroom[] = [];
    const candidateDates: number[] = [];
    for (const raw of [details.CreatedAt, details.PublishedAt, details.ModifiedAt]) {
      if (raw) {
        const t = Date.parse(raw);
        if (!Number.isNaN(t)) candidateDates.push(t);
      }
    }

    for (const sceneFolder of sceneFolders) {
      const savesPrefix = `${roomRoot}${sceneFolder}/saves/`;
      const saveIds: number[] = [];
      for (const p of allPaths) {
        if (!p.startsWith(savesPrefix) || !p.endsWith('.json')) continue;
        const stem = p.slice(savesPrefix.length, -'.json'.length);
        if (/^\d+$/.test(stem)) saveIds.push(parseInt(stem, 10));
      }
      saveIds.sort((a, b) => a - b);
      const latestSave = saveIds.at(-1) ?? null;
      let latestSidecar: { CreatedAt?: string; DataBlob?: string } = {};
      if (latestSave !== null) {
        const sidecarEntry = entryByPath.get(`${savesPrefix}${latestSave}.json`);
        if (sidecarEntry) {
          try { latestSidecar = JSON.parse(await readText(sidecarEntry)); } catch { /* default */ }
          if (latestSidecar.CreatedAt) {
            const t = Date.parse(latestSidecar.CreatedAt);
            if (!Number.isNaN(t)) candidateDates.push(t);
          }
        }
      }

      const blobFilename = latestSave !== null ? `${latestSave}__data.room` : null;
      const blobFound = blobFilename ? entryByPath.has(`${savesPrefix}${blobFilename}`) : false;
      let htrAssetCount = 0;
      let pvImageCount = 0;
      let polaroidCount = 0;
      if (latestSave !== null) {
        const refPrefix = `${savesPrefix}${latestSave}__ref_`;
        for (const p of allPaths) {
          if (!p.startsWith(refPrefix)) continue;
          const assetName = p.slice(refPrefix.length);
          const lower = assetName.toLowerCase();
          if (lower.endsWith('.htr')) htrAssetCount++;
          else if (isImageName(lower)) {
            pvImageCount++;
            polaroidCount++;
          }
        }
      }

      subrooms.push({
        scene: stripStudioSuffix(sceneFolder),
        blobFilename: latestSidecar.DataBlob ?? blobFilename,
        blobFound,
        htrAssetCount,
        pvImageCount,
        polaroidCount,
        hasSubroomJson: true,
      });
    }

    if (details.SubRooms && details.SubRooms.length > 0) {
      const manifestOrder = details.SubRooms.map(s => s.Name ?? '').filter(Boolean);
      subrooms.sort((a, b) => {
        const ai = manifestOrder.indexOf(a.scene);
        const bi = manifestOrder.indexOf(b.scene);
        if (ai === -1 && bi === -1) return a.scene.localeCompare(b.scene);
        if (ai === -1) return 1;
        if (bi === -1) return -1;
        return ai - bi;
      });
    } else {
      subrooms.sort((a, b) => a.scene.localeCompare(b.scene));
    }

    const hasImage = (!!details.ImageName && Array.from(allPaths).some(p =>
                    p.startsWith(`${roomFolder}/photos/`) && p.endsWith(`/${details.ImageName}`)))
                  || allPaths.has(`${roomFolder}/photos/main.jpg`)
                  || allPaths.has(`${roomFolder}/photos/main.png`);
    let promoImageCount = 0;
    for (const p of allPaths) {
      if (p.startsWith(`${roomFolder}/photos/`) && p.split('/').pop()?.toLowerCase().startsWith('promo')) promoImageCount++;
    }

    const latestMs = candidateDates.length > 0 ? Math.max(...candidateDates) : null;
    const latestModifiedAt = latestMs !== null ? new Date(latestMs).toISOString() : null;
    const postBuildCutoff = latestMs !== null && latestMs > CLIENT_BUILD_CUTOFF.getTime();

    rooms.push({
      folder: roomFolder,
      name: details.Name ?? details.FriendlyName ?? stripStudioSuffix(roomFolder.split('/').pop()!),
      description: details.Description ?? '',
      tags: (details.Tags ?? []).map(t => t.Tag).filter((t): t is string => !!t),
      stats: {
        visits: details.Stats?.VisitCount ?? 0,
        visitors: details.Stats?.VisitorCount ?? 0,
        cheers: details.Stats?.CheerCount ?? 0,
        favorites: details.Stats?.FavoriteCount ?? 0,
      },
      subrooms,
      hasImage,
      promoImageCount,
      latestModifiedAt,
      postBuildCutoff,
      isStudio: true,
    });
    bump();
  }

  // Inventions — top-level `Inventions/` OR per-room `Rooms/<r>/Inventions/`.
  // The exporter uses either layout depending on the room (rooms with
  // their own attached inventions ship them inside the room folder).
  const inventions: PreviewInvention[] = [];
  for (const path of allPaths) {
    if (!path.endsWith('/Invention.json')) continue;
    if (!path.startsWith('Inventions/') && !path.includes('/Inventions/')) continue;
    const folder = path.slice(0, -'/Invention.json'.length);
    let meta: InventionJson = {};
    try { meta = JSON.parse(await readText(entryByPath.get(path)!)); } catch { /* default */ }

    const hasBlob = Array.from(allPaths).some(p => p.startsWith(`${folder}/`) && p.endsWith('.inv'));
    const hasImage = (!!meta.ImageName && entryByPath.has(`${folder}/${meta.ImageName}`))
                  || Array.from(allPaths).some(p => p.startsWith(`${folder}/InventionImage.`));
    inventions.push({
      folder,
      name: meta.Name ?? folder.split('/').pop()!,
      description: meta.Description ?? '',
      hasBlob,
      hasImage,
      versionNumber: meta.CurrentVersionNumber ?? 1,
    });
    bump();
  }

  // Anything not under Rooms/ or top-level Inventions/ is flagged.
  // Per-room Inventions/ paths are valid (live under Rooms/) so they
  // don't trigger here.
  const unknownTopLevel: string[] = [];
  const studioRoomFolders = studioRoomManifestPaths.map(p => p.slice(0, -'/room.json'.length));
  for (const p of allPaths) {
    if (studioRoomFolders.some(f => p === f || p.startsWith(`${f}/`))) continue;
    if (!p.startsWith('Rooms/') && !p.startsWith('Inventions/')) unknownTopLevel.push(p);
  }
  unknownTopLevel.sort();

  rooms.sort((a, b) => a.name.localeCompare(b.name));
  inventions.sort((a, b) => a.name.localeCompare(b.name));

  onProgress?.(100);
  return { rooms, inventions, unknownTopLevel };
}

// Entry is a union of FileEntry | DirectoryEntry; only FileEntry has
// .getData. We already filter out directories before reaching this,
// but TS can't carry that info through the Map, so narrow explicitly.
async function readText(entry: Entry): Promise<string> {
  const file = entry as Entry & { getData?: (writer: TextWriter) => Promise<string> };
  if (!file.getData) throw new Error(`zip entry "${entry.filename}" has no data (directory entry)`);
  return file.getData(new TextWriter());
}

function stripStudioSuffix(name: string): string {
  const idx = name.lastIndexOf('__');
  if (idx <= 0) return name;
  return /^\d+$/.test(name.slice(idx + 2)) ? name.slice(0, idx) : name;
}

function hasStudioSaveEntries(roomFolder: string, paths: Set<string>): boolean {
  const prefix = `${roomFolder.replace(/\/+$/, '')}/`;
  for (const p of paths) {
    if (!p.startsWith(prefix) || !p.endsWith('__data.room')) continue;
    const rel = p.slice(prefix.length);
    const parts = rel.split('/');
    if (parts.length === 3 && parts[0].includes('__') && parts[1].toLowerCase() === 'saves') return true;
  }
  return false;
}

function isImageName(name: string): boolean {
  return /\.(jpe?g|png|webp|gif)$/i.test(name);
}
