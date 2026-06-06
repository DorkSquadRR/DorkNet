import { useState } from 'react';
import { post } from '../lib/api';

// Server error code -> friendly copy.
const ERRORS: Record<string, string> = {
  missing_code: 'Enter your signup code.',
  invalid_code: "That code isn't valid. Double-check it with whoever gave it to you.",
  code_revoked: 'That code has been revoked. Ask for a new one.',
  code_used: 'That code has already been used.',
  code_expired: 'That code has expired. Ask for a new one.',
  invalid_username: 'Pick a username 2–24 characters long (letters, numbers, _ or -).',
  username_taken: 'That username is taken — try another.',
  missing_password: 'Enter a password.',
  password_too_short: 'Use at least 8 characters for your password.',
  password_mismatch: 'The passwords do not match.',
};

export function Join() {
  const [code, setCode] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [doneUsername, setDoneUsername] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    if (password !== confirmPassword) {
      setErr(ERRORS.password_mismatch);
      setBusy(false);
      return;
    }
    try {
      const res = await post<{ ok: boolean; username: string }>('/join/redeem', {
        Code: code.trim(),
        Username: username.trim(),
        Password: password,
      });
      setDoneUsername(res.username);
    } catch (e) {
      const key = (e as Error).message;
      setErr(ERRORS[key] ?? key);
    } finally {
      setBusy(false);
    }
  };

  if (doneUsername) {
    return (
      <div className="max-w-lg mx-auto space-y-4">
        <div className="card !p-6 text-center">
          <h1 className="text-2xl font-semibold text-ink-50">You're in, {doneUsername}! 🎉</h1>
          <p className="mt-3 text-sm text-ink-300">
            Your account is ready. Launch the game through the DorkNet launcher and sign in
            with your username and password.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-lg mx-auto space-y-4">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Join the server</h1>
        <p className="text-sm text-ink-400">
          Got a signup code? Redeem it here to create your account, then sign in from the game.
        </p>
      </div>

      <form onSubmit={submit} className="card !p-5 space-y-4">
        <label className="block">
          <span className="label">Signup code</span>
          <input
            className="input mt-1 font-mono tracking-wider"
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder="XXXX-XXXX"
            autoFocus
          />
        </label>

        <label className="block">
          <span className="label">Choose a username</span>
          <input
            className="input mt-1"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="2–24 chars: letters, numbers, _ or -"
            autoComplete="username"
          />
        </label>

        <label className="block">
          <span className="label">Password</span>
          <input
            className="input mt-1"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="at least 8 characters"
            autoComplete="new-password"
          />
        </label>

        <label className="block">
          <span className="label">Confirm password</span>
          <input
            className="input mt-1"
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="type it again"
            autoComplete="new-password"
          />
        </label>

        {err && (
          <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">{err}</div>
        )}

        <button type="submit" disabled={busy} className="btn-primary text-sm w-full justify-center">
          {busy ? 'Creating your account…' : 'Create my account'}
        </button>
      </form>
    </div>
  );
}
