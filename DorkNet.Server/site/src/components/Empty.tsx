export function Empty({ title, blurb }: { title: string; blurb?: string }) {
  return (
    <div className="py-12 text-center">
      <div className="text-sm font-medium text-ink-200">{title}</div>
      {blurb && <div className="mt-1 text-xs text-ink-400">{blurb}</div>}
    </div>
  );
}
