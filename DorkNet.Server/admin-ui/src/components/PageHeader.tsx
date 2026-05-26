interface Props {
  title: string;
  blurb?: string;
  actions?: React.ReactNode;
}

// Consistent page header: title + optional one-line blurb + a slot
// for primary actions (refresh, new, etc.) aligned to the right.
export function PageHeader({ title, blurb, actions }: Props) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 sm:gap-4 mb-4 sm:mb-5">
      <div className="min-w-0 flex-1">
        <h1 className="text-xl sm:text-2xl font-semibold tracking-tight text-ink-50">{title}</h1>
        {blurb && <p className="text-xs sm:text-sm text-ink-400 mt-0.5">{blurb}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}
