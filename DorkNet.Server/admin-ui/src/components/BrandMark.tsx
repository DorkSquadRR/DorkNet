import logoUrl from '../assets/dorknet.svg';

// The DorkNet logo, drawn as-is: it carries its own outline, so it reads on
// both the light and the dark theme without a backing tile.
export function BrandMark({ className = 'size-6' }: { className?: string }) {
  return <img src={logoUrl} alt="" className={`shrink-0 object-contain ${className}`} />;
}
