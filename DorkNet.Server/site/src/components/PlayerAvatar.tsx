import { useState } from 'react';
import { profileImageUrl } from '../lib/types';

// Round avatar with onError fallback to a gradient circle stamped with
// the display-name initial. Mirrors the admin SPA's PlayerAvatar so the
// two surfaces feel consistent. img.* CDN signs the URL via the p1
// key-id baked into the patched client; browsers either render or 404,
// no extra client-side handling required.
export function PlayerAvatar({
  name,
  displayName,
  size = 36,
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
