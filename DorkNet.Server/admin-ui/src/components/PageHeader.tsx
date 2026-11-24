interface Props {
  title: string;
  blurb?: string;
  actions?: React.ReactNode;
}

// Consistent page header: title + optional one-line blurb + a slot
// for primary actions (refresh, new, etc.) aligned to the right.
export function PageHeader({ title, blurb, actions }: Props) {
  return (
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3 sm:mb-5 sm:gap-4">
      <div className="min-w-0 flex-1">
        <h1 className="text-xl font-semibold tracking-tight text-ink-50">{title}</h1>
        {blurb && <p className="mt-1 max-w-prose text-[13px] text-ink-400">{blurb}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}
