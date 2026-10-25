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

let cached: Promise<DownloadsManifest | null> | null = null;

/** The manifest, or null when this server doesn't offer downloads. Fetched
 * once per page load; pass `fresh` to re-read it. */
export function getDownloads(fresh = false): Promise<DownloadsManifest | null> {
  if (!cached || fresh) {
    cached = get<DownloadsManifest>('/downloads').catch((e) => {
      if (e instanceof ApiError && e.status === 404) return null;
      cached = null;
      throw e;
    });
  }
  return cached;
}

export const fileUrl = (f: DownloadFile) =>
  `/api/site/v1/downloads/files/${f.path.split('/').map(encodeURIComponent).join('/')}`;

/** The newest release's Quest package, if one is published. */
export function latestQuest(m: DownloadsManifest | null): { release: DownloadRelease; file: DownloadFile } | null {
  for (const release of m?.releases ?? []) {
    const file = release.files.find((f) => f.id === 'quest');
    if (file) return { release, file };
  }
  return null;
}
