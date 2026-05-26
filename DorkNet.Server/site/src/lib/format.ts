// Friendly date formatting — mirrors the admin SPA's helper but kept
// local to avoid sharing source across two TS projects.

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(+d)) return '';
  const s = (Date.now() - d.getTime()) / 1000;
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  if (s < 604800) return `${Math.floor(s / 86400)}d ago`;
  return d.toLocaleDateString();
}

export function absoluteTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(+d) ? '' : d.toLocaleString();
}

export function num(n: number | null | undefined): string {
  if (n === null || n === undefined) return '0';
  return n.toLocaleString();
}
