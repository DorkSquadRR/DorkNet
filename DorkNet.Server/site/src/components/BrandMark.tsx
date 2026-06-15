import logoUrl from '../assets/dorknet.svg';

export function BrandMark({ className = 'size-8' }: { className?: string }) {
  return (
    <span className={`inline-flex shrink-0 items-center justify-center rounded-md bg-ink-950 ring-1 ring-brand-300/30 shadow-lg shadow-brand-900/30 ${className}`}>
      <img src={logoUrl} alt="" className="h-full w-full object-contain p-0.5" />
    </span>
  );
}
