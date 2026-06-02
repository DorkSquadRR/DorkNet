import { useState } from 'react';
import { api } from '../lib/api';
import { useToast } from '../components/Toast';

// Admin password reset, rendered as a card inside the player detail
// modal's Profile tab. The target account is the open player, so there's
// no picker — just set-and-confirm. Issues a Logout push so any active
// sessions on the old password get dropped.
export function PasswordResetCard({ playerId, username }: { playerId: number; username: string }) {
  const [pw, setPw] = useState('');
  const [pw2, setPw2] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const random = () => {
    // Simple human-friendly random password — letters, digits, and a
    // touch of punctuation. 16 chars is comfortably above the BCrypt
    // 72-byte cap and the server's 8-char minimum.
    const alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!?#@*';
    const buf = new Uint32Array(16);
    crypto.getRandomValues(buf);
    const next = Array.from(buf, n => alphabet[n % alphabet.length]).join('');
    setPw(next); setPw2(next); setShow(true);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (pw !== pw2) return toast.push("Passwords don't match", 'error');
    if (pw.length < 8) return toast.push('Password must be at least 8 characters', 'error');
    if (!confirm(`Reset password for @${username}? This forces a logout on every active session.`)) return;
    setBusy(true);
    try {
      await api(`/players/${playerId}/password`, {
        method: 'POST',
        body: { NewPassword: pw },
      });
      toast.push('Password updated', 'success');
      setPw(''); setPw2(''); setShow(false);
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="card !p-4 md:col-span-2 space-y-3">
      <div>
        <h3 className="text-sm font-semibold text-ink-50 mb-1">Reset password</h3>
        <p className="text-xs text-ink-400">
          Admin-set a new password. The plaintext is hashed with BCrypt before write — we never store it, and the audit log
          records the action but not the password. Issues a Logout push that drops any session still on the old password.
        </p>
      </div>

      <label className="flex flex-col gap-1">
        <span className="label">New password</span>
        <div className="flex gap-2">
          <input
            type={show ? 'text' : 'password'}
            value={pw}
            onChange={e => setPw(e.target.value)}
            minLength={8}
            autoComplete="new-password"
            className="input flex-1 font-mono"
          />
          <button type="button" onClick={() => setShow(s => !s)} className="btn-ghost text-xs">{show ? 'Hide' : 'Show'}</button>
          <button type="button" onClick={random} className="btn-secondary text-xs">Random</button>
        </div>
      </label>

      <label className="flex flex-col gap-1">
        <span className="label">Confirm new password</span>
        <input
          type={show ? 'text' : 'password'}
          value={pw2}
          onChange={e => setPw2(e.target.value)}
          minLength={8}
          autoComplete="new-password"
          className="input font-mono"
        />
      </label>

      <div className="flex justify-end gap-2">
        <button type="button" onClick={() => { setPw(''); setPw2(''); setShow(false); }} className="btn-ghost text-xs" disabled={busy}>Clear</button>
        <button className="btn-primary text-xs" disabled={busy || pw.length < 8 || pw !== pw2}>
          {busy ? 'Updating…' : 'Update password'}
        </button>
      </div>
    </form>
  );
}
