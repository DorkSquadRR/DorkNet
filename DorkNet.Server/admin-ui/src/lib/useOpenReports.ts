import { useEffect, useState } from 'react';
import { get } from './api';
import type { Stats } from './types';

// Open report count for the sidebar badge. Polls the same /stats endpoint
// the Live page uses, but slowly: the count only needs to be roughly fresh.
export function useOpenReports(intervalMs = 30_000): number | null {
  const [count, setCount] = useState<number | null>(null);
  useEffect(() => {
    let alive = true;
    const load = () => {
      get<Stats>('/stats')
        .then(s => { if (alive) setCount(s.moderation.openReports); })
        .catch(() => { /* keep the last known count; the page shows real errors */ });
    };
    load();
    const id = window.setInterval(load, intervalMs);
    return () => { alive = false; window.clearInterval(id); };
  }, [intervalMs]);
  return count;
}
