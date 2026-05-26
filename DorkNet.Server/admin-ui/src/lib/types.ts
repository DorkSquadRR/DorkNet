// Wire shapes returned by AdminController. Mirrors the anonymous-object
// projections in DorkNet.Server/Controllers/Admin/AdminController.cs.
// Loosely typed where the server returns dynamic structures; tightened
// to specific fields where we render columns.

export interface Player {
  id: number;
  username: string;
  displayName: string;
  email: string | null;
  isAdmin: boolean;
  isDeveloper: boolean;
  isCommunityTeam: boolean;
  isVerified: boolean;
  isJunior: boolean;
  bannedUntil: string | null;
  lastIp: string | null;
  lastSeenAt: string | null;
  createdAt: string;
  level: number;
  xp: number;
  profileImageName: string | null;
  online: boolean;
}

export interface PlayerDetail extends Player {
  bio: string | null;
  balances: Array<{ currencyType: number; balance: number }>;
  avatar: {
    outfitSelections: string | null;
    hairColor: string | null;
    skinColor: string | null;
    faceFeatures: string | null;
  } | null;
}

// img.* CDN URL for a player's stored profile image — same path the
// 2020 watch uses (cropSquare=1 produces a face-zoomed square, sig
// matches the host's signing key id).
export function profileImageUrl(name: string | null | undefined, width = 96): string | null {
  if (!name) return null;
  const apex = typeof window !== 'undefined' && window.location.host.endsWith('localhost') ? 'localhost' : 'rec.net';
  return `https://img.${apex}/${encodeURIComponent(name)}?width=${width}&cropSquare=1&sig=p1`;
}

export interface Room {
  id: number;
  name: string;
  isAGRoom: boolean;
  isDormRoom: boolean;
  creatorPlayerId: number;
  blobCount: number;
}

// RoomRole.Role enum (server-side RoomRoleEntity.Role):
//   0 = CoOwner, 1 = Moderator, 2 = Host.
// Accepted=true ⇒ surfaces in CoOwners / Moderators / Hosts arrays.
// Accepted=false ⇒ surfaces in the Invited* counterparts (pending grant).
export interface RoomRoleGrant {
  id: number;
  playerId: number;
  role: 0 | 1 | 2;
  accepted: boolean;
  grantedByPlayerId: number | null;
  grantedAt: string;
  player: {
    id: number;
    username: string;
    displayName: string;
    profileImageName: string | null;
  };
}

// Per-room live Photon instance — GET /api/admin/v1/rooms/{id}/instances.
// masterPlayerId is a best-effort proxy for the Photon master client
// (server doesn't see ActorNumbers; we use dorm-creator-if-present
// else lowest-pid). The matching participant has isMaster=true so the
// UI can tag a row without recomputing.
export interface RoomInstance {
  roomInstanceId: number;
  roomId: number;
  subRoomId: number;
  roomName: string;
  photonRoomId: string;
  photonRegionId: string;
  location: string;
  maxCapacity: number;
  isPrivate: boolean;
  masterPlayerId: number;
  participants: Array<{
    id: number;
    username: string;
    displayName: string;
    isMaster: boolean;
  }>;
}

// Full per-room detail returned by GET /api/admin/v1/rooms/{id}. The
// admin SPA's per-room detail page consumes this in one round trip.
export interface RoomDetail {
  id: number;
  name: string;
  description: string;
  imageName: string;
  state: number;
  accessibility: number;
  isAGRoom: boolean;
  isDormRoom: boolean;
  cloningAllowed: boolean;
  supportsLevelVoting: boolean;
  supportsVRLow: boolean;
  supportsMobile: boolean;
  supportsScreens: boolean;
  supportsWalkVR: boolean;
  supportsTeleportVR: boolean;
  allowsJuniors: boolean;
  disableMicAutoMute: boolean;
  roomWarningMask: number;
  customRoomWarning: string;
  tagsCsv: string;
  cheerCount: number;
  favoriteCount: number;
  visitCount: number;
  visitorCount: number;
  hotScore: number;
  locationReplicationId: string;
  currentDataBlobName: string;
  createdAt: string;
  updatedAt: string;
  sceneCount: number;
  blobCount: number;
  owner: {
    id: number;
    username: string;
    displayName: string;
    profileImageName: string | null;
  };
  roles: RoomRoleGrant[];
}

export interface IpBan {
  id: number;
  cidr: string;
  reason: string;
  bannedByAdminId: number;
  bannedAt: string;
  until: string | null;
}

export interface AuditEntry {
  id: number;
  adminPlayerId: number;
  action: string;
  targetType: string;
  targetId: number;
  reason: string;
  timestamp: string;
}

export interface Report {
  id: number;
  reporterPlayerId: number;
  targetPlayerId: number;
  reason: string;
  context: string;
  createdAt: string;
  resolvedAt: string | null;
  resolverAdminId: number | null;
  resolutionNote: string | null;
}

export interface Stats {
  players: { total: number; onlineNow: number; bannedNow: number };
  rooms: {
    total: number;
    topByVisits: Array<{ id: number; name: string; visitCount: number; visitorCount: number; cheerCount: number }>;
  };
  inventions: number;
  moderation: { openReports: number; activeIpBans: number };
  recentJoins: Array<{ id: number; username: string; createdAt: string }>;
  serverTime: string;
}

export interface StoreItem {
  id: number;
  slug: string;
  displayName: string;
  description: string;
  category: string;
  imageName: string;
  currencyType: number;
  price: number;
  isActive: boolean;
  isLimitedTime: boolean;
  availableUntil: string | null;
  storefront: string;
  createdAt: string;
  updatedAt: string;
}

export interface StorefrontDefinition {
  key: string;
  storefrontType: number | null;
  displayName: string;
  scope: string;
}

export interface Instance {
  roomInstanceId: number;
  roomId: number;
  subRoomId: number;
  roomName: string;
  photonRoomId: string;
  photonRegionId: string;
  location: string;
  maxCapacity: number;
  isPrivate: boolean;
  participants: Array<{ id: number; username: string; displayName: string }>;
}

export interface PlayerLogEntry {
  timestamp: string;
  method: string;
  host: string;
  path: string;
  query: string;
  status: number;
  elapsedMs: number;
}

// Mirrors DorkNet.Server.Services.CommunityBoardState (and the
// nested DTOs in CommunityBoardService.cs). The 2020 watch decodes
// the same shape from /api/communityboard/v1/current — admins
// edit it here via /api/admin/v1/communityboard.
export interface CommunityBoardState {
  currentAnnouncement: { message: string; moreInfoUrl: string } | null;
  featuredPlayer: { id: number; titleOverride: string; urlOverride: string } | null;
  featuredRoomGroup: { name: string; featuredRooms: number[] } | null;
  instagramImages: Array<{ imageName: string; imageUrl: string }>;
  videos: Array<{ blobName: string; title: string; description: string; thumbnailBlobName: string; sourceUrl: string }>;
}
