// Placeholder used by routes whose page hasn't been built yet so the
// router/nav is wireable today and pages can land incrementally without
// breaking links.

export function Stub({ title, blurb }: { title: string; blurb?: string }) {
  return (
    <div className="space-y-3">
      <h1 className="text-2xl font-semibold tracking-tight text-ink-50">{title}</h1>
      <div className="card p-6 text-sm text-ink-300">
        <div className="text-ink-200 mb-1">Coming soon.</div>
        {blurb && <p className="text-ink-400">{blurb}</p>}
      </div>
    </div>
  );
}
