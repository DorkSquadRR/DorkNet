// Token + identity storage. The server's same-origin AdminLoginController
// hands back an access_token plus a small identity blob; we stash both
// in localStorage and re-hydrate on page load so a refresh doesn't kick
// the admin back to the login screen.

const TOKEN_KEY = 'dorknet.admin.token';
const ME_KEY = 'dorknet.admin.me';

export interface AdminMe {
  id: number;
  username: string;
  displayName: string;
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

// getMe is read on every render by useSyncExternalStore. Re-parsing
// localStorage each call would return a fresh object reference even
// when the underlying data hasn't changed, which React detects as a
// state change and re-renders forever (React error #185). Cache by
// the raw string so consecutive identical reads return the same
// AdminMe instance and the snapshot stays stable.
let cachedMeRaw: string | null | undefined;
let cachedMe: AdminMe | null = null;
export function getMe(): AdminMe | null {
  const raw = localStorage.getItem(ME_KEY);
  if (raw === cachedMeRaw) return cachedMe;
  cachedMeRaw = raw;
  if (!raw) { cachedMe = null; return null; }
  try { cachedMe = JSON.parse(raw) as AdminMe; }
  catch { cachedMe = null; }
  return cachedMe;
}

export function setSession(token: string, me: AdminMe): void {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(ME_KEY, JSON.stringify(me));
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(ME_KEY);
}

// Simple subscription so components can react to login/logout without
// pulling in a state lib. Anything that calls getToken() in render and
// wants live updates can useSyncExternalStore against this.
type Listener = () => void;
const listeners = new Set<Listener>();
export function subscribeAuth(fn: Listener): () => void {
  listeners.add(fn);
  return () => { listeners.delete(fn); };
}
export function notifyAuthChange(): void {
  listeners.forEach(fn => fn());
}
