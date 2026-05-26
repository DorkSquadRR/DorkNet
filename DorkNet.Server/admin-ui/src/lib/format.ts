// Display helpers used across pages. Kept here so we render dates,
// durations, and numeric quantities the same way everywhere — a player
// table and a player detail page should not disagree about how to
// format "5 days ago".

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const t = new Date(iso).getTime();
  if (!Number.isFinite(t)) return '—';
  const diffSec = Math.round((Date.now() - t) / 1000);
  const abs = Math.abs(diffSec);
  if (abs < 45) return diffSec >= 0 ? 'just now' : 'in a moment';
  const units: Array<[number, string]> = [
    [60, 'sec'],
    [60, 'min'],
    [24, 'hr'],
    [7, 'day'],
    [4.345, 'wk'],
    [12, 'mo'],
    [Infinity, 'yr'],
  ];
  let v = abs;
  let label = 'sec';
  for (let i = 0; i < units.length; i++) {
    const [step, name] = units[i];
    if (v < step) { label = name; break; }
    v = v / step;
    label = units[i + 1]?.[1] ?? 'yr';
  }
  const rounded = v >= 10 ? Math.round(v) : Math.round(v * 10) / 10;
  return diffSec >= 0 ? `${rounded} ${label} ago` : `in ${rounded} ${label}`;
}

export function absoluteTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (!Number.isFinite(d.getTime())) return '—';
  return d.toLocaleString();
}

export function num(n: number | null | undefined): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return '—';
  return n.toLocaleString();
}

export function clip(s: string | null | undefined, max = 80): string {
  if (!s) return '';
  return s.length <= max ? s : s.slice(0, max - 1) + '…';
}

// Currency type IDs follow the canonical Rec Room enum:
//   1 = Tokens, 2 = Coins (RecRoom currency).
export function currencyName(type: number): string {
  switch (type) {
    case 1: return 'Tokens';
    case 2: return 'Coins';
    default: return `Currency #${type}`;
  }
}
