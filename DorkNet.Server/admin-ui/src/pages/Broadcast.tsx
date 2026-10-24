import { useState } from 'react';
import { api } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';

export function Broadcast() {
  const [message, setMessage] = useState('');
  const [minutes, setMinutes] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const startsInMinutes = minutes.trim() === '' ? null : Number(minutes);
  const minutesValid = startsInMinutes === null || (Number.isInteger(startsInMinutes) && startsInMinutes >= 0);
  const canSend = !busy && minutesValid && (message.trim() !== '' || startsInMinutes !== null);

  const send = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSend) return;
    const parts = [
      message.trim() && `"${message.trim()}"`,
      startsInMinutes !== null &&
        (startsInMinutes === 0 ? 'Cancel the maintenance countdown' : `Maintenance countdown: ${startsInMinutes} min`),
    ].filter(Boolean);
    if (!confirm(`Broadcast this to every connected player?\n\n${parts.join('\n')}`)) return;
    setBusy(true);
    try {
      const res = await api<{ recipients: number }>('/broadcast', {
        method: 'POST',
        body: { Message: message.trim() || null, StartsInMinutes: startsInMinutes },
      });
      toast.push(`Broadcast sent to ${res.recipients} player(s)`, 'success');
      setMessage('');
      setMinutes('');
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Broadcast"
        blurb="Message every connected player. The message lands in their in-game notifications from the system account; the optional countdown starts the game's own maintenance timer."
      />

      <form onSubmit={send} className="card !p-5 max-w-2xl">
        <label className="label">Message</label>
        <textarea
          value={message}
          onChange={e => setMessage(e.target.value)}
          rows={5}
          className="input mt-1.5 font-mono text-sm"
          placeholder="Server going down for maintenance in 5 minutes — log out cleanly to avoid losing your dorm save."
        />
        <div className="mt-2 text-xs text-ink-400">
          {message.length} characters
        </div>

        <label className="label mt-4">Maintenance countdown (minutes, optional)</label>
        <input
          value={minutes}
          onChange={e => setMinutes(e.target.value)}
          inputMode="numeric"
          className="input mt-1.5 w-40 text-sm"
          placeholder="e.g. 5"
        />
        <div className="mt-2 text-xs text-ink-400">
          {minutesValid ? 'Leave empty for no countdown; 0 cancels a running one.' : 'Enter a whole number of minutes.'}
        </div>

        <div className="mt-4 flex gap-2 justify-end">
          <button
            type="button"
            onClick={() => { setMessage(''); setMinutes(''); }}
            className="btn-ghost text-xs"
            disabled={busy || (!message && !minutes)}
          >
            Clear
          </button>
          <button className="btn-primary text-xs" disabled={!canSend}>
            {busy ? 'Sending…' : 'Send broadcast'}
          </button>
        </div>
      </form>
    </div>
  );
}
