import { useState } from 'react';
import { Modal } from './Modal';

interface Props {
  title: string;
  body: React.ReactNode;
  confirmLabel?: string;
  destructive?: boolean;
  open: boolean;
  onClose: () => void;
  onConfirm: () => Promise<void> | void;
}

export function Confirm({ title, body, confirmLabel = 'Confirm', destructive, open, onClose, onConfirm }: Props) {
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    try { await onConfirm(); onClose(); }
    finally { setBusy(false); }
  };
  return (
    <Modal
      title={title}
      open={open}
      onClose={onClose}
      size="sm"
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={run} className={(destructive ? 'btn-danger' : 'btn-primary') + ' text-xs'} disabled={busy}>
          {busy ? 'Working…' : confirmLabel}
        </button>
      </>}
    >
      <div className="text-sm text-ink-200">{body}</div>
    </Modal>
  );
}
