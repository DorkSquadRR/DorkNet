// Lightweight fetch wrapper for the public-facing site. Talks to the
// apex-hosted PublicSiteController at /api/site/v1/*. No Authorization
// header or token plumbing: the optional sign-in (lib/account.ts) is an
// HttpOnly cookie that same-origin fetches send on their own.

const BASE = '/api/site/v1';

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function get<T = unknown>(path: string): Promise<T> {
  const res = await fetch(BASE + path);
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try {
      const body = await res.json();
      if (body?.error) msg = body.error;
      else if (body?.message) msg = body.message;
    } catch { /* keep status text */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// POST with a JSON body. Used by the /join signup-code flow — the only
// write path the otherwise read-only public site has. On a non-2xx the
// server's { error } string (e.g. "code_expired") is surfaced as the
// thrown ApiError message so the page can map it to friendly copy.
export async function post<T = unknown>(path: string, body: unknown): Promise<T> {
  const res = await fetch(BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`;
    try {
      const b = await res.json();
      if (b?.error) msg = b.error;
      else if (b?.message) msg = b.message;
    } catch { /* keep status text */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}
