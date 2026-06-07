import { useEffect, useMemo, useState } from 'react';
import { get } from '../lib/api';
import type { SiteRoom } from '../lib/types';
import { imageApex } from '../lib/types';
import { Empty } from '../components/Empty';
import { num } from '../lib/format';

export function Rooms() {
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<SiteRoom[] | null>(null);
  const [loading, setLoading] = useState(false);

  const debounce = useMemo(() => {
    let t: number | undefined;
    return (value: string) => {
      window.clearTimeout(t);
      t = window.setTimeout(() => {
        setLoading(true);
        get<SiteRoom[]>(`/rooms/search?q=${encodeURIComponent(value.trim())}&take=40`)
          .then(setRows).catch(() => setRows([])).finally(() => setLoading(false));
      }, 200);
    };
  }, []);

  useEffect(() => { debounce(q); }, [q, debounce]);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Rooms</h1>
        <p className="text-sm text-ink-400">Browse the community-made rooms on the server.</p>
      </div>

      <div className="card !p-3">
        <input
          value={q}
          onChange={e => setQ(e.target.value)}
          placeholder="Filter rooms by name…"
          className="input"
        />
      </div>

      {loading && <div className="py-6 text-center text-xs text-ink-400">Loading…</div>}

      {!loading && rows && rows.length === 0 && (
        <Empty title="No rooms match" blurb="Try a shorter or different query." />
      )}

      {rows && rows.length > 0 && (
        <ul className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          {rows.map(r => <li key={r.id}><RoomCard room={r} /></li>)}
        </ul>
      )}
    </div>
  );
}

export function RoomCard({ room }: { room: SiteRoom }) {
  const img = room.imageName ? `https://img.${imageApex()}/${encodeURIComponent(room.imageName)}?width=320&sig=p1` : null;
  return (
    <div className="card overflow-hidden">
      <div className="aspect-[16/9] bg-ink-800 flex items-center justify-center">
        {img
          ? <img src={img} alt={room.name} className="size-full object-cover" loading="lazy"
                 onError={e => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }} />
          : <span className="text-ink-500 font-mono text-sm">^{room.name}</span>
        }
      </div>
      <div className="p-3 space-y-1">
        <div className="flex items-center gap-1.5">
          <span className="font-medium text-ink-50 truncate">^{room.name}</span>
          {room.isDormRoom && <span className="badge-neutral">Dorm</span>}
          {!room.isAGRoom && !room.isDormRoom && <span className="badge-neutral">Custom</span>}
        </div>
        {room.description && (
          <p className="text-xs text-ink-400 line-clamp-2">{room.description}</p>
        )}
        <div className="flex gap-3 text-[11px] text-ink-500 pt-1">
          <span>{num(room.visitCount)} visits</span>
          <span>{num(room.visitorCount)} visitors</span>
          <span>♥ {num(room.cheerCount)}</span>
        </div>
      </div>
    </div>
  );
}
