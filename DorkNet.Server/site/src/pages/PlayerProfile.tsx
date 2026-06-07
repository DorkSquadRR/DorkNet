import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { get } from '../lib/api';
import type { SitePhoto, SitePlayerDetail, SiteRoom } from '../lib/types';
import { PlayerAvatar } from '../components/PlayerAvatar';
import { PhotoCard } from '../components/PhotoCard';
import { Empty } from '../components/Empty';
import { absoluteTime, num, relativeTime } from '../lib/format';
import { RoomCard } from './Rooms';

export function PlayerProfile() {
  const { id } = useParams<{ id: string }>();
  const [player, setPlayer] = useState<SitePlayerDetail | null>(null);
  const [photos, setPhotos] = useState<SitePhoto[] | null>(null);
  const [taggedPhotos, setTaggedPhotos] = useState<SitePhoto[] | null>(null);
  const [rooms, setRooms] = useState<SiteRoom[] | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    setErr(null);
    setPlayer(null);
    setPhotos(null);
    setTaggedPhotos(null);
    setRooms(null);
    get<SitePlayerDetail>(`/players/${id}`)
      .then(setPlayer)
      .catch(e => setErr((e as Error).message));
    get<SitePhoto[]>(`/players/${id}/photos?take=48`).then(setPhotos).catch(() => setPhotos([]));
    get<SitePhoto[]>(`/players/${id}/photos/of?take=48`).then(setTaggedPhotos).catch(() => setTaggedPhotos([]));
    get<SiteRoom[]>(`/players/${id}/rooms?take=24`).then(setRooms).catch(() => setRooms([]));
  }, [id]);

  if (err) return (
    <div className="card !p-6">
      <h2 className="text-lg font-semibold text-ink-50">Player not found</h2>
      <p className="text-sm text-ink-400 mt-1">{err}</p>
      <Link to="/players" className="btn-secondary text-sm mt-4">← Back to players</Link>
    </div>
  );

  if (!player) return <div className="py-10 text-center text-xs text-ink-400">Loading…</div>;

  return (
    <div className="space-y-6">
      <Link to="/players" className="btn-ghost text-xs">← Back to players</Link>

      <header className="card !p-5 flex flex-col sm:flex-row gap-4 sm:items-center">
        <PlayerAvatar
          name={player.profileImageName}
          displayName={player.displayName || player.username}
          size={88}
        />
        <div className="min-w-0 flex-1">
          <h1 className="text-2xl font-semibold text-ink-50 truncate">{player.displayName || player.username}</h1>
          <div className="text-sm text-ink-400 truncate">@{player.username} <span className="text-ink-600">·</span> #{player.id}</div>
          <div className="mt-2 flex flex-wrap items-center gap-1.5">
            {player.isAdmin     && <span className="badge-admin">Admin</span>}
            {player.isDeveloper && <span className="badge-neutral">Developer</span>}
            {player.isVerified  && <span className="badge-neutral">Verified</span>}
            {player.isJunior    && <span className="badge-junior">Junior</span>}
          </div>
          {player.bio && (
            <p className="mt-3 text-sm text-ink-200 whitespace-pre-wrap">{player.bio}</p>
          )}
        </div>
        <div className="grid grid-cols-3 sm:grid-cols-1 sm:w-32 gap-2 text-xs">
          <Stat label="Level"  value={num(player.level)} />
          <Stat label="Friends" value={num(player.friendCount)} />
          <Stat label="Joined" value={relativeTime(player.createdAt)} title={absoluteTime(player.createdAt)} />
        </div>
      </header>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-xs">
        <Stat label="Photos Taken" value={num(player.photosTakenCount ?? player.photoCount)} />
        <Stat label="Photos Of" value={num(player.photosOfPlayerCount)} />
        <Stat label="Public Rooms" value={num(player.publicRoomCount)} />
        <Stat label="XP" value={num(player.xp)} />
      </div>

      <section>
        <h2 className="text-lg font-semibold text-ink-50 mb-3">Photos taken</h2>
        {photos === null
          ? <div className="text-xs text-ink-400 py-6">Loading…</div>
          : photos.length === 0
            ? <Empty title="No photos yet" blurb={`${player.displayName || player.username} hasn't shared any photos.`} />
            : <div className="photo-grid">
                {photos.map(p => <PhotoCard key={p.id} photo={p} />)}
              </div>
        }
      </section>

      <section>
        <h2 className="text-lg font-semibold text-ink-50 mb-3">Pictures they appear in</h2>
        {taggedPhotos === null
          ? <div className="text-xs text-ink-400 py-6">Loading…</div>
          : taggedPhotos.length === 0
            ? <Empty title="No tagged photos" blurb={`${player.displayName || player.username} hasn't been tagged in public photos yet.`} />
            : <div className="photo-grid">
                {taggedPhotos.map(p => <PhotoCard key={p.id} photo={p} />)}
              </div>
        }
      </section>

      <section>
        <h2 className="text-lg font-semibold text-ink-50 mb-3">Public rooms</h2>
        {rooms === null
          ? <div className="text-xs text-ink-400 py-6">Loading…</div>
          : rooms.length === 0
            ? <Empty title="No public rooms" blurb={`${player.displayName || player.username} hasn't published any public rooms.`} />
            : <ul className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {rooms.map(r => <li key={r.id}><RoomCard room={r} /></li>)}
              </ul>
        }
      </section>
    </div>
  );
}

function Stat({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="rounded-md border border-ink-800 bg-ink-900/60 px-3 py-2 text-center" title={title}>
      <div className="text-[10px] uppercase tracking-widest text-ink-400">{label}</div>
      <div className="text-sm font-semibold text-ink-50 truncate">{value}</div>
    </div>
  );
}
