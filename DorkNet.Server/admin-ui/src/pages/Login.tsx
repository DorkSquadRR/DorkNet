import { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { api, ApiError } from '../lib/api';
import { notifyAuthChange, setSession } from '../lib/auth';
import { BrandMark } from '../components/BrandMark';

interface LoginResponse {
  access_token: string;
  refresh_token: string;
  account_id: number;
  username: string;
  display_name: string;
}

export function Login() {
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: { pathname: string } } | null)?.from?.pathname ?? '/';
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErr(null);
    setLoading(true);
    try {
      const res = await api<LoginResponse>('/login', {
        method: 'POST',
        body: { username, password },
      });
      setSession(res.access_token, {
        id: res.account_id,
        username: res.username,
        displayName: res.display_name,
      });
      notifyAuthChange();
      navigate(from, { replace: true });
    } catch (e) {
      if (e instanceof ApiError) {
        if (e.body && typeof e.body === 'object' && 'error' in e.body) {
          const code = (e.body as { error: string }).error;
          if (code === 'not_admin') setErr('That account is not an admin.');
          else if (code === 'invalid_credentials') setErr('Invalid username or password.');
          else setErr(e.message);
        } else setErr(e.message);
      } else setErr(String(e));
    } finally {
      setLoading(false);
    }
  };

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
        </form>

        <p className="mt-6 text-center text-[11px] text-ink-500">DorkNet private server · admin console</p>
      </div>
    </div>
  );
}
