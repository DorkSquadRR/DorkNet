// The client builds CI publishes (ci/downloads/publish.py), served through
// the server's DownloadsController. The manifest keeps the snake_case keys
// publish.py writes.
import { ApiError, get } from './api';

export interface DownloadFile {
  id: string; // pc-full | pc-patch | quest | quest-installer
  platform: 'pc' | 'quest' | string;
  name: string;
  path: string;
  size: number;
  sha256: string;
  build?: string;
  commit?: string;
  built_at?: string;
}

export interface DownloadRelease {
  id: string;
  title: string;
  game_version?: string;
  pc_exe?: string;
  files: DownloadFile[];
}

export interface DownloadsManifest {
  releases: DownloadRelease[];
  updated?: string;
}

/** The server only serves downloads to signed-in players
 * (DorkNet:Downloads:RequireSignIn) and nobody is signed in. */
export class SignInRequiredError extends Error {
  constructor() { super('sign_in_required'); this.name = 'SignInRequiredError'; }
}

let cached: Promise<DownloadsManifest | null> | null = null;

/** The manifest, or null when this server doesn't offer downloads. Fetched
 * once per page load; pass `fresh` to re-read it. */
export function getDownloads(fresh = false): Promise<DownloadsManifest | null> {
  if (!cached || fresh) {
    cached = get<DownloadsManifest>('/downloads').catch((e) => {
      if (e instanceof ApiError && e.status === 404) return null;
      if (e instanceof ApiError && e.status === 401) { cached = null; throw new SignInRequiredError(); }
      cached = null;
      throw e;
    });
  }
  return cached;
}

export const fileUrl = (f: DownloadFile) =>
  `/api/site/v1/downloads/files/${f.path.split('/').map(encodeURIComponent).join('/')}`;

export interface QuestBuild { release: DownloadRelease; file: DownloadFile }

/** A published Quest package: the one at `path` when given, otherwise the
 * newest release's. Null when there's no match. */
export function questBuild(m: DownloadsManifest | null, path?: string | null): QuestBuild | null {
  for (const release of m?.releases ?? []) {
    const file = release.files.find((f) => f.id === 'quest' && (!path || f.path === path));
    if (file) return { release, file };
  }
  return null;
}
