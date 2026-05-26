import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError, type ApiOptions } from './api';

interface State<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
}

// Read-side hook for "fetch on mount, expose refresh + error state".
// Not a replacement for TanStack Query — we don't need its cache /
// dedupe / focus-refetch behavior for an admin tool used by 1-2 people.
// But every page does the same useState/useEffect/abort dance, so this
// collapses it to one line.
export function useApi<T = unknown>(path: string, opts: ApiOptions & { manual?: boolean } = {}) {
  const [state, setState] = useState<State<T>>({ data: null, loading: !opts.manual, error: null });
  const abortRef = useRef<AbortController | null>(null);
  const { manual, ...rest } = opts;
  // Stabilise opts across renders by capturing in a ref the caller can ignore.
  // Anything that varies between renders (e.g. inline objects) is fine because
  // we re-run on `path` change only.
  const optsRef = useRef(rest);
  optsRef.current = rest;

  const refresh = useCallback(async () => {
    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;
    setState(s => ({ ...s, loading: true, error: null }));
    try {
      const data = await api<T>(path, { ...optsRef.current, signal: ctrl.signal });
      if (!ctrl.signal.aborted) setState({ data, loading: false, error: null });
    } catch (e) {
      if (ctrl.signal.aborted) return;
      const msg = e instanceof ApiError ? e.message : (e as Error).message;
      setState({ data: null, loading: false, error: msg });
    }
  }, [path]);

  useEffect(() => {
    if (manual) return;
    refresh();
    return () => { abortRef.current?.abort(); };
  }, [refresh, manual]);

  return { ...state, refresh };
}
