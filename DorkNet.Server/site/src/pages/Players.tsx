import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { get } from '../lib/api';
import type { SitePlayerCard } from '../lib/types';
import { PlayerAvatar } from '../components/PlayerAvatar';
import { Empty } from '../components/Empty';

export function Players() {
  const [params, setParams] = useSearchParams();
  const initialQ = params.get('q') ?? '';
  const [q, setQ] = useState(initialQ);
  const [rows, setRows] = useState<SitePlayerCard[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  // Debounced search — fires 250 ms after the last keystroke. Reuses
  // the URL ?q= param so search results are linkable / shareable and
  // the back button restores the last query.
  const debounce = useMemo(() => {
    let t: number | undefined;
    return (value: string) => {
      window.clearTimeout(t);
      t = window.setTimeout(() => {
        setParams(value ? { q: value } : {}, { replace: true });
        if (!value.trim()) { setRows([]); return; }
        setLoading(true);
        setErr(null);
        get<SitePlayerCard[]>(`/players/search?q=${encodeURIComponent(value.trim())}&take=40`)
          .then(setRows)
          .catch(e => setErr((e as Error).message))
          .finally(() => setLoading(false));
      }, 250);
    };
  }, [setParams]);

  useEffect(() => {
    const next = params.get('q') ?? '';
    setQ(current => current === next ? current : next);
  }, [params]);

  useEffect(() => { debounce(q); }, [q, debounce]);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-semibold text-ink-50">Players</h1>
        <p className="text-sm text-ink-400">Find an account by display name or username.</p>
      </div>

      <div className="card !p-3 flex items-center gap-2">
        <input
          autoFocus
          value={q}
          onChange={e => setQ(e.target.value)}
          placeholder="Search players by name…"
          className="input"
        />
      </div>

      {err && (
        <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">{err}</div>
      )}

      {loading && <div className="py-6 text-center text-xs text-ink-400">Searching…</div>}

      {!loading && rows && rows.length === 0 && q.trim().length > 0 && (
        <Empty title="No matches" blurb="Try a different name." />
      )}

      {!loading && (!q.trim() || rows === null) && (
        <Empty title="Type to search" blurb="Start typing a username or display name above." />
      )}

      {rows && rows.length > 0 && (
        <ul className="grid grid-cols-1 sm:grid-cols-2 gap-2">
          {rows.map(p => (
            <li key={p.id}>
              <Link
                to={`/players/${p.id}`}
                className="card !p-3 flex items-center gap-3 transition-colors hover:bg-ink-800/60"
              >
                <PlayerAvatar name={p.profileImageName} displayName={p.displayName || p.username} size={44} />
                <div className="min-w-0 flex-1">
                  <div className="font-medium text-ink-50 truncate flex items-center gap-1.5">
                    {p.displayName || p.username}
                    {p.isAdmin && <span className="badge-admin">Admin</span>}
                    {p.isVerified && !p.isAdmin && <span className="badge-neutral">Verified</span>}
                    {p.isJunior && <span className="badge-junior">Junior</span>}
                  </div>
                  <div className="text-xs text-ink-400 truncate">
                    @{p.username} <span className="text-ink-600">·</span> #{p.id} <span className="text-ink-600">·</span> Lv {p.level}
                  </div>
                </div>
                <span className="text-xs text-ink-500">→</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
