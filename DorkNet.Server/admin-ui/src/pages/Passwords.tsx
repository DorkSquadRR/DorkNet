import { useState } from 'react';
import { api } from '../lib/api';
import type { Player } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';

export function Passwords() {
  const { data: players } = useApi<Player[]>('/players?take=500');
  const [playerId, setPlayerId] = useState<number | null>(null);
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
    if (!playerId) return;
    if (pw !== pw2) return toast.push("Passwords don't match", 'error');
    if (pw.length < 8) return toast.push('Password must be at least 8 characters', 'error');
    const player = players?.find(p => p.id === playerId);
    if (!confirm(`Reset password for @${player?.username ?? `#${playerId}`}? This forces a logout on every active session.`)) return;
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
    <div>
      <PageHeader
        title="Reset passwords"
        blurb="Admin-set a new password for any account. Issues a Logout push so any active sessions on the old password get dropped."
      />

      <form onSubmit={submit} className="card !p-5 max-w-xl space-y-4">
        <label className="flex flex-col gap-1">
          <span className="label">Account</span>
          <select value={playerId ?? ''} onChange={e => setPlayerId(e.target.value ? parseInt(e.target.value) : null)} required className="input">
            <option value="">— pick an account —</option>
            {(players ?? []).map(p => <option key={p.id} value={p.id}>{p.displayName || p.username} · @{p.username} · #{p.id}</option>)}
          </select>
        </label>

        <label className="flex flex-col gap-1">
          <span className="label">New password</span>
          <div className="flex gap-2">
            <input
              type={show ? 'text' : 'password'}
              value={pw}
              onChange={e => setPw(e.target.value)}
              minLength={8}
              required
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
            required
            autoComplete="new-password"
            className="input font-mono"
          />
        </label>

        <div className="rounded-lg border border-warn/30 bg-warn/10 px-3 py-2 text-xs text-warn">
          The plaintext is hashed with BCrypt before write — we never store it. The audit log records the action but not the password.
        </div>

        <div className="flex justify-end gap-2">
          <button type="button" onClick={() => { setPw(''); setPw2(''); setShow(false); }} className="btn-ghost text-xs" disabled={busy}>Clear</button>
          <button className="btn-primary text-xs" disabled={busy || !playerId || pw.length < 8 || pw !== pw2}>
            {busy ? 'Updating…' : 'Update password'}
          </button>
        </div>
      </form>
    </div>
  );
}
