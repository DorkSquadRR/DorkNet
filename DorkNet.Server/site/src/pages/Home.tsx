import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { get } from '../lib/api';
import type { SitePhoto, SiteStats } from '../lib/types';
import { PhotoCard } from '../components/PhotoCard';
import { num } from '../lib/format';

export function Home() {
  const [stats, setStats] = useState<SiteStats | null>(null);
  const [photos, setPhotos] = useState<SitePhoto[] | null>(null);

  useEffect(() => {
    get<SiteStats>('/stats').then(setStats).catch(() => setStats(null));
    get<SitePhoto[]>('/feed?take=8').then(setPhotos).catch(() => setPhotos([]));
  }, []);

  return (
    <div className="space-y-10">
      <section className="card !p-6 sm:!p-10 relative overflow-hidden">
        <div className="absolute -top-20 -right-24 size-72 rounded-full bg-brand-500/20 blur-3xl pointer-events-none" />
        <div className="absolute -bottom-20 -left-24 size-72 rounded-full bg-brand-700/20 blur-3xl pointer-events-none" />
        <div className="relative max-w-2xl">
          <h1 className="text-3xl sm:text-5xl font-bold text-ink-50 tracking-tight">
            Rec Room, kept alive.
          </h1>
          <p className="mt-3 text-ink-300 text-base sm:text-lg">
            DorkNet is a private server for the 2020.03.10 Rec Room client. Browse the
            photo feed below, find friends, and explore rooms made by the community.
          </p>
          <div className="mt-5 flex flex-wrap gap-2">
            <Link to="/feed" className="btn-primary text-sm">Browse the feed</Link>
            <Link to="/players" className="btn-secondary text-sm">Find players</Link>
          </div>
        </div>
        {stats && (
          <div className="relative mt-8 grid grid-cols-2 sm:grid-cols-4 gap-3 max-w-2xl">
            <Stat label="Players"    value={num(stats.playerCount)} />
            <Stat label="Rooms"      value={num(stats.roomCount)} />
            <Stat label="Photos"     value={num(stats.photoCount)} />
            <Stat label="Inventions" value={num(stats.inventionCount)} />
          </div>
        )}
      </section>

      <section>
        <div className="flex items-baseline justify-between mb-3">
          <h2 className="text-lg font-semibold text-ink-50">Latest photos</h2>
          <Link to="/feed" className="text-xs text-ink-300 hover:text-ink-100">View all →</Link>
        </div>
        {photos === null
          ? <div className="text-xs text-ink-400 py-6">Loading photos…</div>
          : photos.length === 0
            ? <div className="card !p-6 text-center text-sm text-ink-400">
                No photos yet — be the first to share one from the in-game camera.
              </div>
            : <div className="photo-grid">
                {photos.map(p => <PhotoCard key={p.id} photo={p} />)}
              </div>
        }
      </section>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-ink-800 bg-ink-900/60 backdrop-blur px-3 py-2">
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className="text-lg font-semibold text-ink-50 tabular-nums">{value}</div>
    </div>
  );
}
