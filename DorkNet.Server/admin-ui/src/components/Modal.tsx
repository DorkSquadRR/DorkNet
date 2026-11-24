import { useEffect } from 'react';

interface Props {
  title: string;
  open: boolean;
  onClose: () => void;
  children: React.ReactNode;
  // Wider for dense edit forms (e.g. player detail with multiple sections).
  size?: 'sm' | 'md' | 'lg' | 'xl';
  // Sticky action bar at the bottom — pass buttons here so the form
  // body can scroll but the actions stay visible on small screens.
  footer?: React.ReactNode;
}

export function Modal({ title, open, onClose, children, size = 'md', footer }: Props) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prevOverflow;
    };
  }, [open, onClose]);

  if (!open) return null;
  const sizeClass = {
    sm: 'max-w-sm',
    md: 'max-w-lg',
    lg: 'max-w-2xl',
    xl: 'max-w-4xl',
  }[size];
  // Big dialogs (player detail, editors) take the whole screen on a phone;
  // small ones (confirmations) stay a centred box.
  const fullOnPhone = size === 'lg' || size === 'xl';

  return (
    <div
      className={`fixed inset-0 z-40 flex items-center justify-center bg-black/50 sm:px-4 sm:py-6 ${fullOnPhone ? '' : 'px-4 py-6'}`}
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label={title}
    >
      <div
        className={`flex w-full flex-col border-ink-800 bg-ink-950 shadow-float sm:h-auto sm:max-h-full sm:rounded-lg sm:border ${
          fullOnPhone ? 'h-full' : 'max-h-full rounded-lg border'
        } ${sizeClass}`}
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-ink-800 px-4 py-3 sm:px-5">
          <h2 className="text-[15px] font-semibold text-ink-50">{title}</h2>
          <button
            onClick={onClose}
            className="rounded-[5px] p-1 text-ink-400 hover:bg-ink-850 hover:text-ink-100"
            aria-label="Close"
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 sm:px-5">
          {children}
        </div>
        {footer && (
          <div className="flex items-center justify-end gap-2 border-t border-ink-800 px-4 py-3 sm:px-5">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
