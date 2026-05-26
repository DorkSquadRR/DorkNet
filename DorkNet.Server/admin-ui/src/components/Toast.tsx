import { createContext, useCallback, useContext, useEffect, useState } from 'react';

type Kind = 'info' | 'success' | 'error';
interface ToastMsg { id: number; kind: Kind; text: string }

interface Ctx { push: (text: string, kind?: Kind) => void }
const ToastCtx = createContext<Ctx | null>(null);

export function useToast(): Ctx {
  const ctx = useContext(ToastCtx);
  if (!ctx) throw new Error('useToast must be used inside <ToastProvider>');
  return ctx;
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<ToastMsg[]>([]);
  const push = useCallback((text: string, kind: Kind = 'info') => {
    const id = Date.now() + Math.random();
    setItems(prev => [...prev, { id, kind, text }]);
    setTimeout(() => setItems(prev => prev.filter(t => t.id !== id)), 4000);
  }, []);

  return (
    <ToastCtx.Provider value={{ push }}>
      {children}
      <div className="pointer-events-none fixed bottom-6 right-6 z-50 flex flex-col gap-2">
        {items.map(t => (
          <ToastItem key={t.id} msg={t} />
        ))}
      </div>
    </ToastCtx.Provider>
  );
}

function ToastItem({ msg }: { msg: ToastMsg }) {
  const [open, setOpen] = useState(false);
  useEffect(() => { requestAnimationFrame(() => setOpen(true)); }, []);
  const colors =
    msg.kind === 'success' ? 'bg-success/15 text-success border-success/30'
    : msg.kind === 'error' ? 'bg-danger/15 text-danger border-danger/30'
    : 'bg-ink-800 text-ink-100 border-ink-700';
  return (
    <div
      className={`pointer-events-auto min-w-[240px] max-w-sm rounded-lg border px-3.5 py-2 text-sm shadow-card transition-all duration-200 ${colors} ${open ? 'translate-x-0 opacity-100' : 'translate-x-2 opacity-0'}`}
      role="status"
    >
      {msg.text}
    </div>
  );
}
