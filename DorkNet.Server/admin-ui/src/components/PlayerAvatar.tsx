import { useState } from 'react';
import { profileImageUrl } from '../lib/types';

// Round avatar tile that falls back to the player's display-name
// initial if there's no ProfileImageName or the request 404s. The
// img.* CDN signs every response, so we don't have to do anything
// special on this side — just point at the URL and let the browser
// render or fail to render. Size in pixels controls both the rendered
// box and the `?width=N` hint we send so the CDN can resize.
export function PlayerAvatar({
  name,
  displayName,
  size = 28,
  className = '',
}: {
  name?: string | null;
  displayName?: string | null;
  size?: number;
  className?: string;
}) {
  const [errored, setErrored] = useState(false);
  const url = errored ? null : profileImageUrl(name, Math.max(size * 2, 64));
  const initial = (displayName ?? '?').charAt(0).toUpperCase();
  const dim = { width: size, height: size };

  if (!url) {
    return (
      <div
        style={dim}
        className={`shrink-0 rounded-full bg-gradient-to-br from-ink-700 to-ink-800 flex items-center justify-center font-semibold text-ink-200 ${className}`}
      >
        <span style={{ fontSize: size * 0.42 }}>{initial}</span>
      </div>
    );
  }
  return (
    <img
      src={url}
      style={dim}
      onError={() => setErrored(true)}
      alt={displayName ? `${displayName}'s avatar` : 'player avatar'}
      className={`shrink-0 rounded-full bg-ink-800 object-cover ${className}`}
      loading="lazy"
    />
  );
}
