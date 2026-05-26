import { clearSession, getToken, notifyAuthChange } from './auth';

const API_BASE = '/api/admin/v1';

export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: unknown) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface ApiOptions extends Omit<RequestInit, 'body'> {
  body?: unknown;
  timeoutMs?: number;
  // For multipart/form-data uploads — pass the FormData and skip JSON encoding.
  formData?: FormData;
}

export async function api<T = unknown>(path: string, opts: ApiOptions = {}): Promise<T> {
  const headers = new Headers(opts.headers ?? {});
  const token = getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  let body: BodyInit | undefined;
  if (opts.formData) {
    body = opts.formData;
  } else if (opts.body !== undefined && opts.body !== null) {
    if (typeof opts.body === 'string') {
      body = opts.body;
    } else {
      headers.set('Content-Type', 'application/json');
      body = JSON.stringify(opts.body);
    }
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), opts.timeoutMs ?? 15000);
  let res: Response;
  try {
    res = await fetch(API_BASE + path, {
      ...opts,
      headers,
      body,
      signal: controller.signal,
    });
  } catch (err) {
    if ((err as Error).name === 'AbortError') {
      throw new ApiError(0, `request timed out (${opts.timeoutMs ?? 15000}ms)`);
    }
    throw err;
  } finally {
    clearTimeout(timer);
  }

  if (res.status === 401) {
    clearSession();
    notifyAuthChange();
    throw new ApiError(401, 'Unauthorized — sign in again');
  }
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    let bodyJson: unknown;
    try {
      bodyJson = await res.json();
      const err = bodyJson as { error?: string; error_description?: string; message?: string };
      if (err?.error) message = err.error + (err.error_description ? ` (${err.error_description})` : '');
      else if (err?.message) message = err.message;
    } catch { /* leave message as status */ }
    throw new ApiError(res.status, message, bodyJson);
  }

  if (res.status === 204) return undefined as T;
  const ct = res.headers.get('content-type') ?? '';
  if (ct.includes('application/json')) {
    const raw = await res.json();
    return camelizeKeys(raw) as T;
  }
  return (await res.text()) as T;
}

// Server responses are PascalCase because Program.cs sets
// PropertyNamingPolicy=null project-wide (the old Rec Room watch
// depends on exact wire keys). The admin SPA is the only place we
// control both ends, so we camelCase on ingress so TS code stays
// idiomatic. Outbound POST bodies don't need the inverse — ASP.NET
// Core's JSON binder has PropertyNameCaseInsensitive=true by default,
// so camelCase request fields bind to PascalCase record properties
// fine.
function camelizeKey(s: string): string {
  if (s.length === 0) return s;
  // Leave already-lower (OAuth-style snake_case keys like access_token)
  // and single-character keys alone — only flip the leading capital.
  return s[0] === s[0].toLowerCase() ? s : s[0].toLowerCase() + s.slice(1);
}

function camelizeKeys(input: unknown): unknown {
  if (Array.isArray(input)) return input.map(camelizeKeys);
  if (input !== null && typeof input === 'object') {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(input as Record<string, unknown>)) {
      out[camelizeKey(k)] = camelizeKeys(v);
    }
    return out;
  }
  return input;
}

// Convenience shorthands so callers don't have to spell out the options object.
export const get  = <T = unknown>(path: string)              => api<T>(path);
export const post = <T = unknown>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body });
export const put  = <T = unknown>(path: string, body?: unknown) => api<T>(path, { method: 'PUT',  body });
export const del  = <T = unknown>(path: string, body?: unknown) => api<T>(path, { method: 'DELETE', body });

// Upload progress callback fires repeatedly during the request body
// send (request bytes), then once with {total, loaded: total} just
// before the server starts processing. fetch() doesn't expose request
// progress at all, so we fall back to XHR for this one helper.
export interface UploadProgress { loaded: number; total: number; percent: number }

// Chunk size for chunked uploads. Cloudflare's free/pro plans cap
// request bodies at 100 MB end-to-end (Tunnel inherits the same
// limit), so any chunk we send has to stay below that. 50 MB leaves
// generous headroom for the multipart envelope, TLS framing, and
// HTTP/2 stream buffers.
export const ZIP_CHUNK_BYTES = 50 * 1024 * 1024;

export async function uploadWithProgress<T = unknown>(
  path: string,
  formData: FormData,
  onProgress?: (p: UploadProgress) => void,
  timeoutMs = 30 * 60_000,
): Promise<T> {
  const token = getToken();
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/admin/v1' + path, true);
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.timeout = timeoutMs;
    xhr.responseType = 'text';

    xhr.upload.onprogress = (e) => {
      if (!onProgress) return;
      const total = e.lengthComputable ? e.total : 0;
      const percent = total > 0 ? Math.round((e.loaded / total) * 100) : 0;
      onProgress({ loaded: e.loaded, total, percent });
    };

    xhr.onload = () => {
      if (xhr.status === 401) {
        clearSession();
        notifyAuthChange();
        reject(new ApiError(401, 'Unauthorized — sign in again'));
        return;
      }
      if (xhr.status < 200 || xhr.status >= 300) {
        let msg = `${xhr.status} ${xhr.statusText}`;
        try {
          const body = JSON.parse(xhr.responseText) as { error?: string; message?: string };
          if (body?.error) msg = body.error;
          else if (body?.message) msg = body.message;
        } catch { /* keep status text */ }
        reject(new ApiError(xhr.status, msg));
        return;
      }
      // Parse response with the same camelize behavior as fetch path.
      try {
        const ct = xhr.getResponseHeader('content-type') ?? '';
        if (ct.includes('application/json') && xhr.responseText) {
          const raw = JSON.parse(xhr.responseText);
          resolve(camelizeKeys(raw) as T);
        } else {
          resolve(xhr.responseText as T);
        }
      } catch (e) {
        reject(e);
      }
    };

    xhr.onerror = () => reject(new ApiError(0, 'network error'));
    xhr.ontimeout = () => reject(new ApiError(0, `request timed out (${timeoutMs}ms)`));

    xhr.send(formData);
  });
}

// Chunked upload for the zip importer. Splits a File into
// ZIP_CHUNK_BYTES-sized pieces with Blob.slice(), POSTs each piece's
// raw bytes to /zip-upload-chunk?offset=N (no multipart wrapper —
// keeps the bytes-on-wire identical to the in-memory slice), then
// calls /zip-upload-finalize once everything's uploaded. Progress
// callback fires per chunk-completion as well as during each chunk's
// upload phase, so the UI bar advances smoothly through a multi-GB
// archive instead of stepping in 50 MB jumps.
export interface ZipImportOptions {
  /// Top-level archive folders (e.g. "Rooms/Room_3163127_YarrHarrHeist_…"
  /// or "Inventions/Invention_…") to actually import. Leave undefined
  /// or empty to import everything in the archive — the SPA's preview
  /// builds these lists from the room/invention checkboxes.
  selectedRoomFolders?: string[];
  selectedInventionFolders?: string[];
  /// Round-trip the imported .room blobs through the 2020 protobuf
  /// schema before persisting. Defaults off because the re-encode
  /// currently crashes the watch on load; the SPA exposes the toggle
  /// in the importer's options panel for cases where the admin wants
  /// to opt back in.
  normalizeBlobs?: boolean;
}

export async function uploadZipInChunks<T = unknown>(
  file: File,
  creatorPlayerId: number,
  onProgress: (p: UploadProgress) => void,
  timeoutMs = 30 * 60_000,
  options: ZipImportOptions = {},
): Promise<T> {
  // 1. Init — server allocates a temp file and hands back an uploadId.
  const init = await api<{ uploadId: string }>('/rooms/zip-upload-init', {
    method: 'POST',
    body: { FileName: file.name, TotalBytes: file.size },
  });
  const uploadId = init.uploadId;

  try {
    let uploadedSoFar = 0;
    for (let offset = 0; offset < file.size; offset += ZIP_CHUNK_BYTES) {
      const end = Math.min(offset + ZIP_CHUNK_BYTES, file.size);
      const chunk = file.slice(offset, end);
      const chunkStart = offset;
      const chunkSize = end - offset;

      await new Promise<void>((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        const token = getToken();
        xhr.open('POST', `/api/admin/v1/rooms/zip-upload-chunk/${uploadId}?offset=${chunkStart}`, true);
        if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
        xhr.setRequestHeader('Content-Type', 'application/octet-stream');
        xhr.timeout = timeoutMs;
        xhr.responseType = 'text';

        xhr.upload.onprogress = (e) => {
          const loaded = uploadedSoFar + (e.lengthComputable ? e.loaded : 0);
          const percent = file.size > 0 ? Math.round((loaded / file.size) * 100) : 0;
          onProgress({ loaded, total: file.size, percent });
        };
        xhr.onload = () => {
          if (xhr.status === 401) {
            clearSession(); notifyAuthChange();
            reject(new ApiError(401, 'Unauthorized — sign in again'));
            return;
          }
          if (xhr.status < 200 || xhr.status >= 300) {
            let msg = `${xhr.status} ${xhr.statusText}`;
            try {
              const body = JSON.parse(xhr.responseText) as { error?: string; message?: string };
              if (body?.error) msg = body.error;
              else if (body?.message) msg = body.message;
            } catch { /* keep status */ }
            reject(new ApiError(xhr.status, msg));
            return;
          }
          uploadedSoFar += chunkSize;
          onProgress({ loaded: uploadedSoFar, total: file.size, percent: Math.round((uploadedSoFar / file.size) * 100) });
          resolve();
        };
        xhr.onerror = () => reject(new ApiError(0, `chunk @${chunkStart} network error`));
        xhr.ontimeout = () => reject(new ApiError(0, `chunk @${chunkStart} timed out`));
        xhr.send(chunk);
      });
    }

    // 2. Finalize — server runs the existing import on the assembled file.
    return await api<T>(`/rooms/zip-upload-finalize/${uploadId}`, {
      method: 'POST',
      body: {
        CreatorPlayerId: creatorPlayerId,
        SelectedRoomFolders: options.selectedRoomFolders ?? null,
        SelectedInventionFolders: options.selectedInventionFolders ?? null,
        NormalizeBlobs: options.normalizeBlobs ?? false,
      },
      timeoutMs,
    });
  } catch (err) {
    // Best-effort cleanup of the temp file on the server. The session
    // also auto-expires after 2 hours, so a missed abort isn't fatal.
    try { await api(`/rooms/zip-upload-abort/${uploadId}`, { method: 'DELETE' }); } catch { /* swallow */ }
    throw err;
  }
}
