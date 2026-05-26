import { Link } from 'react-router-dom';
import type { SitePhoto } from '../lib/types';
import { PlayerAvatar } from './PlayerAvatar';
import { num, relativeTime } from '../lib/format';

// Single-photo tile for the masonry feed. The outer block is a Link
// to the photo-detail route; the avatar inside is its OWN Link to the
// uploader profile — the inner Link wins click-precedence because
// React Router stops event propagation. event.stopPropagation on the
// inner anchor preserves that even on touch devices where iOS
// occasionally fires both handlers.
export function PhotoCard({ photo }: { photo: SitePhoto }) {
  return (
    <Link
      to={`/photo/${photo.id}`}
      className="card overflow-hidden block transition-transform hover:-translate-y-0.5 hover:shadow-lg"
    >
      <div className="relative">
        <img
          src={photo.imageUrl}
          alt={photo.caption || `Photo by ${photo.uploaderDisplayName}`}
          loading="lazy"
          onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
          className="w-full h-auto bg-ink-800 object-cover"
        />
      </div>
      <div className="p-2.5">
        <Link
          to={`/players/${photo.uploaderPlayerId}`}
          onClick={e => e.stopPropagation()}
          className="flex items-center gap-2 group"
        >
          <PlayerAvatar
            name={photo.uploaderProfileImageName}
            displayName={photo.uploaderDisplayName}
            size={24}
          />
          <span className="text-xs text-ink-200 group-hover:text-ink-50 truncate">
            {photo.uploaderDisplayName}
          </span>
        </Link>
        {photo.caption && (
          <p className="mt-2 text-xs text-ink-300 line-clamp-2">{photo.caption}</p>
        )}
        <div className="mt-2 flex items-center justify-between text-[11px] text-ink-400">
          <span title="cheers">♥ {num(photo.cheerCount)}</span>
          {photo.roomName
            ? <span className="font-mono truncate ml-2" title={`Room: ${photo.roomName}`}>^{photo.roomName}</span>
            : <span>{relativeTime(photo.createdAt)}</span>
          }
        </div>
      </div>
    </Link>
  );
}
