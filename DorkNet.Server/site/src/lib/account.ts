// Website sign-in through the server's OpenID Connect provider
// (ExternalSignInController). The session is an HttpOnly cookie on this host,
// so there is no token to keep here: same-origin fetches carry it.
import { get, post } from './api';

export interface AccountMe {
  signInAvailable: boolean;
  provider: string;
  signedIn: boolean;
  player: { id: string; username: string; email: string } | null;
  pendingLink: { email: string } | null;
}

let cached: Promise<AccountMe> | null = null;

/** Who is signed in. Fetched once per page load; `fresh` re-reads it. */
export function getMe(fresh = false): Promise<AccountMe> {
  if (!cached || fresh) cached = get<AccountMe>('/account/me').catch((e) => { cached = null; throw e; });
  return cached;
}

/** Starts sign-in at the provider; comes back to `returnUrl` on this site. */
export function signInUrl(returnUrl = window.location.pathname + window.location.search): string {
  return `/api/auth/external/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export const link = (username: string, password: string) =>
  post<{ linked: boolean; username: string }>('/account/link', { username, password });
export const signOut = () => post('/account/logout', {});
export const unlink = () => post('/account/unlink', {});

// Server error code -> friendly copy.
export const LINK_ERRORS: Record<string, string> = {
  invalid_credentials: "That username and password don't match a DorkNet account.",
  missing_credentials: 'Enter your DorkNet username and password.',
  too_many_attempts: 'Too many wrong attempts. Wait 15 minutes and try again.',
  banned: 'That account is banned.',
  identity_linked_to_another_account: 'This sign-in is already linked to a different DorkNet account.',
  no_pending_identity: 'Your sign-in expired. Start again with "Sign in".',
};

export const SIGNIN_ERRORS: Record<string, string> = {
  account_unavailable: 'The account linked to this sign-in is unavailable (deleted or banned).',
  no_identity: "The sign-in provider didn't return an identity. Try again.",
  provider_failed: 'The sign-in was cancelled or failed at the provider. Try again.',
};
