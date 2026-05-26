import { useState } from 'react';
import { api } from '../lib/api';
import { PageHeader } from '../components/PageHeader';
import { useToast } from '../components/Toast';

export function Broadcast() {
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const send = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!message.trim()) return;
    if (!confirm(`Broadcast this to every connected player?\n\n"${message}"`)) return;
    setBusy(true);
    try {
      await api('/broadcast', { method: 'POST', body: { Message: message } });
      toast.push('Broadcast sent', 'success');
      setMessage('');
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
        blurb="Push a server-maintenance notification to every connected player. The watch shows it as a system toast."
      />

      <form onSubmit={send} className="card !p-5 max-w-2xl">
        <label className="label">Message</label>
        <textarea
          value={message}
          onChange={e => setMessage(e.target.value)}
          rows={5}
          className="input mt-1.5 font-mono text-sm"
          placeholder="Server going down for maintenance in 5 minutes — log out cleanly to avoid losing your dorm save."
          required
        />
        <div className="mt-2 text-xs text-ink-400">
          {message.length} characters
        </div>
        <div className="mt-4 flex gap-2 justify-end">
          <button type="button" onClick={() => setMessage('')} className="btn-ghost text-xs" disabled={busy || !message}>Clear</button>
          <button className="btn-primary text-xs" disabled={busy || !message.trim()}>
            {busy ? 'Sending…' : 'Send broadcast'}
          </button>
        </div>
      </form>
    </div>
  );
}
