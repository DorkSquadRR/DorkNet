import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { get } from '../lib/api';
import type { SitePhoto } from '../lib/types';
import { PlayerAvatar } from '../components/PlayerAvatar';
import { absoluteTime, num, relativeTime } from '../lib/format';

export function PhotoDetail() {
  const { id } = useParams<{ id: string }>();
  const [photo, setPhoto] = useState<SitePhoto | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    setErr(null);
    setPhoto(null);
    get<SitePhoto>(`/photos/${id}`).then(setPhoto).catch(e => setErr((e as Error).message));
  }, [id]);

  if (err) return (
    <div className="card !p-6">
      <h2 className="text-lg font-semibold text-ink-50">Photo not found</h2>
      <p className="text-sm text-ink-400 mt-1">{err}</p>
      <Link to="/feed" className="btn-secondary text-sm mt-4">← Back to feed</Link>
    </div>
  );

  if (!photo) return <div className="py-10 text-center text-xs text-ink-400">Loading…</div>;

  return (
    <div className="space-y-4">
      <Link to="/feed" className="btn-ghost text-xs">← Back to feed</Link>

      <div className="card overflow-hidden grid grid-cols-1 lg:grid-cols-[1fr,360px]">
        <div className="bg-ink-950 flex items-center justify-center">
          <img
            src={photo.imageUrl}
            alt={photo.caption || `Photo #${photo.id}`}
            className="w-full h-auto max-h-[80vh] object-contain"
          />
        </div>
        <aside className="p-5 border-t lg:border-t-0 lg:border-l border-ink-800 flex flex-col gap-4">
          <div>
            <Link
              to={`/players/${photo.uploaderPlayerId}`}
              className="flex items-center gap-3 group"
            >
              <PlayerAvatar
                name={photo.uploaderProfileImageName}
                displayName={photo.uploaderDisplayName}
                size={44}
              />
              <div className="min-w-0">
                <div className="font-semibold text-ink-50 group-hover:text-brand-200 truncate">
                  {photo.uploaderDisplayName}
                </div>
                <div className="text-xs text-ink-400 truncate">@{photo.uploaderUsername}</div>
              </div>
            </Link>
          </div>

          {photo.caption && (
            <p className="text-sm text-ink-200 whitespace-pre-wrap">{photo.caption}</p>
          )}

          <div className="grid grid-cols-2 gap-2 text-xs">
            <div className="rounded-md border border-ink-800 bg-ink-900/60 px-3 py-2">
              <div className="text-[10px] uppercase tracking-widest text-ink-400">Cheers</div>
              <div className="text-sm font-semibold text-ink-50 tabular-nums">{num(photo.cheerCount)}</div>
            </div>
            <div className="rounded-md border border-ink-800 bg-ink-900/60 px-3 py-2">
              <div className="text-[10px] uppercase tracking-widest text-ink-400">Views</div>
              <div className="text-sm font-semibold text-ink-50 tabular-nums">{num(photo.viewCount)}</div>
            </div>
          </div>

          <div className="text-xs text-ink-400 space-y-1">
            {photo.roomName && (
              <div>
                <span className="text-ink-500">In room: </span>
                <span className="font-mono text-ink-200">^{photo.roomName}</span>
              </div>
            )}
            <div title={absoluteTime(photo.createdAt)}>
              <span className="text-ink-500">Posted: </span>
              <span className="text-ink-200">{relativeTime(photo.createdAt)}</span>
            </div>
          </div>
        </aside>
      </div>
    </div>
  );
}
