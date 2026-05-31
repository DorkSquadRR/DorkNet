import { useEffect, useState } from 'react';
import { get, post } from '../lib/api';

interface PendingDevice {
  deviceId: string;
  platform: number;
  lastSeenAt: string;
}

// Server error code -> friendly copy.
const ERRORS: Record<string, string> = {
  missing_code: 'Enter your signup code.',
  invalid_code: "That code isn't valid. Double-check it with whoever gave it to you.",
  code_revoked: 'That code has been revoked. Ask for a new one.',
  code_used: 'That code has already been used.',
  code_expired: 'That code has expired. Ask for a new one.',
  invalid_username: 'Pick a username 2–24 characters long (letters, numbers, _ or -).',
  username_taken: 'That username is taken — try another.',
  missing_device: 'Pick or paste the device id from your game client.',
  device_in_use: 'That device already has an account — just launch the game to log in.',
};

export function Join() {
  const [code, setCode] = useState('');
  const [username, setUsername] = useState('');
  const [deviceId, setDeviceId] = useState('');
  const [pending, setPending] = useState<PendingDevice[]>([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [doneUsername, setDoneUsername] = useState<string | null>(null);

  // Pull the devices our caller's IP was recently refused from, so the
  // player can pick the one their own game client reported instead of
  // hunting for the Unity device id by hand.
  useEffect(() => {
    get<PendingDevice[]>('/join/pending-devices')
      .then((rows) => {
        setPending(rows);
        if (rows.length > 0) setDeviceId((cur) => cur || rows[0].deviceId);
      })
      .catch(() => { /* picker is a convenience; manual paste still works */ });
  }, []);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      const res = await post<{ ok: boolean; username: string }>('/join/redeem', {
        Code: code.trim(),
        Username: username.trim(),
        DeviceId: deviceId.trim(),
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
            Your account is ready. Launch the game through the DorkNet launcher on this
            same device and you'll be logged straight in — no signup step needed.
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
          Got a signup code? Redeem it here to create your account, then launch the game.
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
          />
        </label>

        <div className="block">
          <span className="label">Your device</span>
          {pending.length > 0 ? (
            <>
              <select
                className="input mt-1"
                value={pending.some((d) => d.deviceId === deviceId) ? deviceId : '__manual__'}
                onChange={(e) => setDeviceId(e.target.value === '__manual__' ? '' : e.target.value)}
              >
                {pending.map((d) => (
                  <option key={d.deviceId} value={d.deviceId}>
                    {d.deviceId.slice(0, 16)}… · last seen {new Date(d.lastSeenAt).toLocaleTimeString()}
                  </option>
                ))}
                <option value="__manual__">Enter device id manually…</option>
              </select>
              {!pending.some((d) => d.deviceId === deviceId) && (
                <input
                  className="input mt-2 font-mono text-xs"
                  value={deviceId}
                  onChange={(e) => setDeviceId(e.target.value)}
                  placeholder="paste your device id"
                />
              )}
              <p className="mt-1 text-[11px] text-ink-500">
                These are devices that just tried to sign in from your network. Pick yours.
              </p>
            </>
          ) : (
            <>
              <input
                className="input mt-1 font-mono text-xs"
                value={deviceId}
                onChange={(e) => setDeviceId(e.target.value)}
                placeholder="paste your device id"
              />
              <p className="mt-1 text-[11px] text-ink-500">
                Launch the game once first — when it says signups are disabled, come back here
                and your device should appear automatically.
              </p>
            </>
          )}
        </div>

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
