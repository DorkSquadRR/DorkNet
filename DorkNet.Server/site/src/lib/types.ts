// Wire shapes returned by PublicSiteController. All keys are
// camelCase (server uses default ASP.NET serializer for this
// controller; only the legacy game-facing endpoints stick with
// PascalCase to placate LitJson on the watch).

export interface SitePlayerCard {
  id: number;
  username: string;
  displayName: string;
  level: number;
  isAdmin: boolean;
  isDeveloper: boolean;
  isVerified: boolean;
  isJunior: boolean;
  profileImageName: string | null;
}

export interface SitePlayerDetail extends SitePlayerCard {
  bio: string;
  xp: number;
  createdAt: string;
  photoCount: number;
}

export interface SitePhoto {
  id: number;
  uploaderPlayerId: number;
  uploaderUsername: string;
  uploaderDisplayName: string;
  uploaderProfileImageName: string | null;
  blobName: string;
  imageUrl: string;
  caption: string;
  roomId: number;
  roomName: string;
  isPublic: boolean;
  cheerCount: number;
  viewCount: number;
  createdAt: string;
}

export interface SiteRoom {
  id: number;
  name: string;
  description: string;
  creatorPlayerId: number;
  isDormRoom: boolean;
  isAGRoom: boolean;
  visitCount: number;
  visitorCount: number;
  cheerCount: number;
  imageName: string;
}

export interface SiteStats {
  playerCount: number;
  roomCount: number;
  photoCount: number;
  inventionCount: number;
}

// CDN URL helper for profile images. Mirrors the admin SPA's helper
// so the apex visit ends up calling img.localhost (signed via the
// hardcoded p1 key id baked into the patched client). On rec.net
// fallbacks to img.rec.net so the site works on either apex.
export function profileImageUrl(name: string | null | undefined, width = 96): string | null {
  if (!name) return null;
  const apex = typeof window !== 'undefined' && window.location.host.endsWith('localhost') ? 'localhost' : 'rec.net';
  return `https://img.${apex}/${encodeURIComponent(name)}?width=${width}&cropSquare=1&sig=p1`;
}
