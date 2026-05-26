// Lightweight fetch wrapper for the public-facing site. Talks to the
// apex-hosted PublicSiteController at /api/site/v1/*. Anonymous-only —
// the public site has no login flow, so there's no Authorization
// header or token plumbing.

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
