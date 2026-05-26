// Tiny stand-in for "nothing matches your filter / nothing exists yet"
// table-empty states. Centered, muted, optional CTA.

interface Props {
  title?: string;
  blurb?: string;
  cta?: React.ReactNode;
  className?: string;
}

export function Empty({ title = 'Nothing here', blurb, cta, className }: Props) {
  return (
    <div className={`flex flex-col items-center justify-center gap-1 py-10 text-center ${className ?? ''}`}>
      <div className="text-sm font-medium text-ink-200">{title}</div>
      {blurb && <div className="text-xs text-ink-400 max-w-xs">{blurb}</div>}
      {cta && <div className="mt-3">{cta}</div>}
    </div>
  );
}
