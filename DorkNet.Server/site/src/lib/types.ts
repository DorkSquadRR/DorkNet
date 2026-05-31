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

// Resolve the image-CDN apex for the current host. The site is served
// from the DorkNet apex (or www.apex) and the server exposes its
// image-transform pipeline at img.{apex}, signed with the hardcoded p1
// key id baked into the patched client. Derive the apex from the live
// host so images load from THIS server's own CDN instead of leaking to
// real Rec Room (img.rec.net) — which also drops the Azure affinity
// cookies the browser was rejecting. Localhost dev → img.localhost.
export function imageApex(): string {
  if (typeof window === 'undefined') return 'localhost';
  const host = window.location.host.split(':')[0];
  if (host === 'localhost' || host.endsWith('.localhost')) return 'localhost';
  return host.startsWith('www.') ? host.slice(4) : host;
}

// CDN URL helper for profile images, served from the server's own
// img.{apex} (see imageApex).
export function profileImageUrl(name: string | null | undefined, width = 96): string | null {
  if (!name) return null;
  return `https://img.${imageApex()}/${encodeURIComponent(name)}?width=${width}&cropSquare=1&sig=p1`;
}
