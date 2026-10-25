import { useEffect, useRef, useState } from 'react';
import { useNavigate, useLocation, useSearchParams } from 'react-router';
import { api, ApiError } from '../lib/api';
import { notifyAuthChange, setSession } from '../lib/auth';
import { adminApex } from '../lib/types';
import { BrandMark } from '../components/BrandMark';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  account_id: number;
  username: string;
  display_name: string;
}

// The website, where an identity from the sign-in provider is linked to an
// account (the admin panel only signs in identities that are linked already).
function siteUrl(path: string): string {
  const apex = adminApex();
  return `${apex === 'localhost' ? 'http' : 'https'}://${apex}${path}`;
}

const SSO_ERRORS: Record<string, React.ReactNode> = {
  not_admin: 'That account is not an admin.',
  not_signed_in: "Single sign-on didn't finish. Try again.",
  account_unavailable: 'The account linked to that sign-in is unavailable (deleted or banned).',
  no_identity: "The sign-in provider didn't return an identity. Try again.",
  provider_failed: 'The sign-in was cancelled or failed at the provider. Try again.',
};

export function Login() {
  const navigate = useNavigate();
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/';
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [err, setErr] = useState<React.ReactNode | null>(null);
  const [loading, setLoading] = useState(false);
  // Single sign-on through the server's OpenID Connect provider, if set up.
  const [sso, setSso] = useState<{ provider: string } | null>(null);
  const ssoHandled = useRef(false);

  const finish = (res: LoginResponse, to: string) => {
    setSession(res.access_token, {
      id: res.account_id,
      username: res.username,
      displayName: res.display_name,
    });
    notifyAuthChange();
    navigate(to, { replace: true });
  };

  const errorText = (e: unknown, codes: Record<string, React.ReactNode>): React.ReactNode => {
    if (e instanceof ApiError && e.body && typeof e.body === 'object' && 'error' in e.body) {
      const code = (e.body as { error: string }).error;
      return codes[code] ?? e.message;
    }
    return e instanceof Error ? e.message : String(e);
  };

  useEffect(() => {
    fetch('/api/site/v1/account/me', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((me) => { if (me?.signInAvailable) setSso({ provider: me.provider }); })
      .catch(() => { /* no single sign-on: password only */ });
  }, []);

  // Back from the provider: ?sso=1 swaps the session for the admin token;
  // ?signin_error=… explains why it didn't work.
  useEffect(() => {
    if (ssoHandled.current) return;
    const signinError = params.get('signin_error');
    if (signinError) {
      ssoHandled.current = true;
      setErr(signinError === 'not_linked'
        ? <>This sign-in isn't linked to a DorkNet account yet. Link it on the <a className="underline" href={siteUrl('/api/auth/external/login?returnUrl=%2Faccount')}>website</a> first, then sign in here again.</>
        : SSO_ERRORS[signinError] ?? `Single sign-on failed (${signinError}).`);
      setParams({}, { replace: true });
      return;
    }
    if (params.get('sso') !== '1') return;
    ssoHandled.current = true;
    setLoading(true);
    api<LoginResponse>('/sso')
      .then((res) => finish(res, params.get('to') || '/'))
      .catch((e) => { setErr(errorText(e, SSO_ERRORS)); setParams({}, { replace: true }); })
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErr(null);
    setLoading(true);
    try {
      const res = await api<LoginResponse>('/login', {
        method: 'POST',
        body: { username, password },
      });
      finish(res, from);
    } catch (e) {
      setErr(errorText(e, {
        not_admin: 'That account is not an admin.',
        invalid_credentials: 'Invalid username or password.',
      }));
    } finally {
      setLoading(false);
    }
  };

  const ssoHref = `/api/auth/external/login?returnUrl=${encodeURIComponent(`/login?sso=1&to=${encodeURIComponent(from)}`)}`;

  return (
    <div className="min-h-full flex items-center justify-center px-4 py-12 bg-[linear-gradient(135deg,#0a0b0e_0%,#0d1d25_48%,#131418_100%)]">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-2">
          <BrandMark className="size-16 rounded-xl" />
          <div className="text-center">
            <h1 className="text-xl font-semibold text-ink-50">DorkNet Admin</h1>
            <p className="text-xs text-ink-400">Sign in with an admin account.</p>
          </div>
        </div>

        <form onSubmit={onSubmit} className="card p-6 flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <label className="label" htmlFor="username">Username</label>
            <input
              id="username"
              autoComplete="username"
              required
              className="input"
              value={username}
              onChange={e => setUsername(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="label" htmlFor="password">Password</label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              className="input"
              value={password}
              onChange={e => setPassword(e.target.value)}
            />
          </div>
          {err && (
            <div className="rounded-lg border border-danger/30 bg-danger/10 px-3 py-2 text-xs text-danger">{err}</div>
          )}
          <button type="submit" disabled={loading} className="btn-primary mt-1">
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
          {sso && (
            <>
              <div className="flex items-center gap-3 text-[11px] uppercase tracking-wide text-ink-500">
                <span className="h-px flex-1 bg-ink-800" />or<span className="h-px flex-1 bg-ink-800" />
              </div>
              <a href={ssoHref} className="btn-secondary text-center">Sign in with {sso.provider}</a>
            </>
          )}
        </form>

        <p className="mt-6 text-center text-[11px] text-ink-500">DorkNet private server · admin console</p>
      </div>
    </div>
  );
}
