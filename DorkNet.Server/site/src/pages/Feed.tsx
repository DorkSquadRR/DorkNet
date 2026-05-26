import { useCallback, useEffect, useState } from 'react';
import { get } from '../lib/api';
import type { SitePhoto } from '../lib/types';
import { PhotoCard } from '../components/PhotoCard';
import { Empty } from '../components/Empty';

const PAGE = 24;

export function Feed() {
  const [items, setItems] = useState<SitePhoto[]>([]);
  const [loading, setLoading] = useState(false);
  const [done, setDone] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const loadMore = useCallback(async () => {
    if (loading || done) return;
    setLoading(true);
    setErr(null);
    try {
      const next = await get<SitePhoto[]>(`/feed?take=${PAGE}&skip=${items.length}`);
      setItems(prev => [...prev, ...next]);
      if (next.length < PAGE) setDone(true);
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [items.length, loading, done]);

  // Initial load.
  useEffect(() => { void loadMore(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, []);

  return (
    <div className="space-y-4">
      <div className="flex items-baseline justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-ink-50">Photo feed</h1>
          <p className="text-sm text-ink-400">Pictures from across the server, newest first.</p>
        </div>
      </div>

      {err && (
        <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">
          {err}
        </div>
      )}

      {items.length === 0 && !loading && !err && (
        <Empty title="No photos yet" blurb="Pick up the in-game camera, snap a shot, and tap Share." />
      )}

      {items.length > 0 && (
        <div className="photo-grid">
          {items.map(p => <PhotoCard key={p.id} photo={p} />)}
        </div>
      )}

      <div className="py-6 text-center">
        {!done ? (
          <button onClick={loadMore} disabled={loading} className="btn-secondary text-sm">
            {loading ? 'Loading…' : 'Load more'}
          </button>
        ) : items.length > 0 && (
          <div className="text-xs text-ink-500">You've reached the end.</div>
        )}
      </div>
    </div>
  );
}
