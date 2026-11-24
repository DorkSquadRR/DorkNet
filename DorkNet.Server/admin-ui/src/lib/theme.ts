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

// An explicit pick, remembered in memory too so it still wins over the OS
// setting for this tab when storage is unavailable.
let explicitChoice = false;

function hasExplicitChoice(): boolean {
  if (explicitChoice) return true;
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved === 'light' || saved === 'dark';
  } catch {
    return false;
  }
}

export function setTheme(theme: Theme): void {
  explicitChoice = true;
  document.documentElement.setAttribute('data-theme', theme);
  try { localStorage.setItem(STORAGE_KEY, theme); } catch { /* private mode: still applies for this tab */ }
  listeners.forEach(fn => fn());
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  // Follow the OS setting until the admin picks a theme explicitly.
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const onSystem = () => {
    if (hasExplicitChoice()) return;
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
