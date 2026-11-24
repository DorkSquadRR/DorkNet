import { useSyncExternalStore } from 'react';

// Light/dark theme. index.html applies the saved choice (or the OS
// preference) before first paint; this module keeps it in sync after that.
// Keep STORAGE_KEY in sync with the inline script in index.html.

export type Theme = 'light' | 'dark';
const STORAGE_KEY = 'dorknet.admin.theme';

function current(): Theme {
  return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
}

const listeners = new Set<() => void>();

export function setTheme(theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme);
  try { localStorage.setItem(STORAGE_KEY, theme); } catch { /* private mode: still applies for this tab */ }
  listeners.forEach(fn => fn());
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  // Follow the OS setting until the admin picks a theme explicitly.
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const onSystem = () => {
    let saved: string | null = null;
    try { saved = localStorage.getItem(STORAGE_KEY); } catch { /* ignore */ }
    if (saved === 'light' || saved === 'dark') return;
    document.documentElement.setAttribute('data-theme', media.matches ? 'dark' : 'light');
    fn();
  };
  media.addEventListener('change', onSystem);
  return () => {
    listeners.delete(fn);
    media.removeEventListener('change', onSystem);
  };
}

export function useTheme(): [Theme, () => void] {
  const theme = useSyncExternalStore(subscribe, current, () => 'light' as Theme);
  return [theme, () => setTheme(theme === 'dark' ? 'light' : 'dark')];
}
