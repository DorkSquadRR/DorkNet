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
      <div className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex flex-col items-end gap-2 sm:inset-x-auto sm:bottom-6 sm:right-6">
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
    msg.kind === 'success' ? 'border-success/40 text-success'
    : msg.kind === 'error' ? 'border-danger/40 text-danger'
    : 'border-ink-700 text-ink-100';
  return (
    <div
      className={`pointer-events-auto w-full max-w-sm rounded-md border bg-ink-950 px-3.5 py-2 text-[13.5px] shadow-float transition-all duration-200 sm:w-auto sm:min-w-[240px] ${colors} ${open ? 'translate-x-0 opacity-100' : 'translate-x-2 opacity-0'}`}
      role="status"
    >
      {msg.text}
    </div>
  );
}
