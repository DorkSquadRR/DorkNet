import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../lib/api';
import type { Room } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { Modal } from '../components/Modal';
import { useToast } from '../components/Toast';
import { Confirm } from '../components/Confirm';
import { RefreshCw, Trash, Upload } from '../components/Icons';

export function Rooms() {
  const { data, loading, error, refresh } = useApi<Room[]>('/rooms');
  const [filter, setFilter] = useState('');
  const [filterKind, setFilterKind] = useState<'all' | 'original' | 'custom' | 'dorm'>('all');
  const [pendingDelete, setPendingDelete] = useState<Room | null>(null);
  const [pendingPurge, setPendingPurge] = useState<Room | null>(null);
  const toast = useToast();
  // Truth table for what's actually canonical:
  //   isSeededOriginal  = IsAGRoom AND creator == system seed (id 1).
  //   isUserOwnedCustom = NOT dorm AND creator != system seed.
  //                       (covers user-cloned rooms that incorrectly
  //                       inherited IsAGRoom=true from their source.)
  //   isDorm            = IsDormRoom.
  // IsAGRoom alone is not a reliable "is this canonical?" signal,
  // because user-cloned rooms inherited it from RR Originals for years
  // before we fixed the clone path.
  const SYSTEM_ACCOUNT_ID = 1;
  const isSeededOriginal = (r: Room) => r.isAGRoom && r.creatorPlayerId === SYSTEM_ACCOUNT_ID && !r.isDormRoom;
  const isUserCustom     = (r: Room) => !r.isDormRoom && r.creatorPlayerId !== SYSTEM_ACCOUNT_ID;

  const filtered = (data ?? []).filter(r => {
    if (filterKind === 'original' && !isSeededOriginal(r)) return false;
    if (filterKind === 'custom'   && !isUserCustom(r))     return false;
    if (filterKind === 'dorm'     && !r.isDormRoom)        return false;
    if (filter.trim() && !r.name.toLowerCase().includes(filter.toLowerCase())) return false;
    return true;
  });

  const del = async () => {
    if (!pendingDelete) return;
    try {
      await api(`/rooms/${pendingDelete.id}`, { method: 'DELETE', body: { Reason: 'admin' } });
      toast.push(`Archived ${pendingDelete.name}`, 'success');
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div>
      <PageHeader
        title="Rooms"
        blurb="Every room in the DB. Archive (trash icon) is soft and reversible. Purge is a hard wipe — drops scene rows + per-room blobs + thumbnail and requires typing the room name twice."
        actions={<>
          <Link to="/import-room" className="btn-primary text-xs">
            <Upload /> Import room
          </Link>
          <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        </>}
      />

      <div className="card overflow-hidden">
        <div className="flex flex-wrap items-center gap-2 border-b border-ink-800 px-3 py-2.5">
          <input value={filter} onChange={e => setFilter(e.target.value)} placeholder="Filter by name…" className="input max-w-xs" />
          <div className="flex gap-1">
            {(['all', 'original', 'custom', 'dorm'] as const).map(k => (
              <button
                key={k}
                onClick={() => setFilterKind(k)}
                className={`px-2.5 py-1 rounded-md text-xs ${filterKind === k ? 'bg-brand-500/15 text-brand-100 ring-1 ring-inset ring-brand-500/30' : 'text-ink-300 hover:bg-ink-800'}`}
              >
                {k === 'all' ? 'All' : k === 'original' ? 'RR Originals' : k === 'custom' ? 'Custom' : 'Dorms'}
              </button>
            ))}
          </div>
          <div className="ml-auto text-xs text-ink-400">
            {data ? `${filtered.length} / ${data.length}` : ''}
          </div>
        </div>

        {error && <div className="px-4 py-3 text-sm text-danger">{error}</div>}
        {data && filtered.length === 0 && <Empty title="No rooms match" />}
        {filtered.length > 0 && (
          <div className="table-scroll"><table className="w-full text-sm min-w-[640px]">
            <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
              <tr>
                <th className="text-left font-medium px-4 py-2.5">#</th>
                <th className="text-left font-medium px-4 py-2.5">Name</th>
                <th className="text-left font-medium px-4 py-2.5">Type</th>
                <th className="text-right font-medium px-4 py-2.5">Creator</th>
                <th className="text-right font-medium px-4 py-2.5">Blobs</th>
                <th />
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-800">
              {filtered.map(r => (
                <tr key={r.id} className="table-row-hover">
                  <td className="px-4 py-2.5 text-ink-400 tabular-nums">{r.id}</td>
                  <td className="px-4 py-2.5 font-medium">
                    {/* Whole row's primary action is "open detail view"
                        — the link makes the room name keyboard- and
                        middle-click-friendly, and the click target
                        large enough on mobile. Archive/Purge stay as
                        action buttons in the rightmost cell. */}
                    <Link to={`/rooms/${r.id}`} className="text-ink-50 hover:text-brand-200">{r.name}</Link>
                  </td>
                  <td className="px-4 py-2.5">
                    {r.isDormRoom && <span className="badge-neutral">Dorm</span>}
                    {/* "RR Original" badge is reserved for rows the
                        system seed account owns. User clones that
                        inherited IsAGRoom=true from their source are
                        rendered as Custom — that matches reality. */}
                    {isSeededOriginal(r) && <span className="badge-admin">RR Original</span>}
                    {isUserCustom(r) && <span className="badge-neutral">Custom</span>}
                  </td>
                  <td className="px-4 py-2.5 text-right text-ink-300 text-xs">
                    {r.creatorPlayerId ? `#${r.creatorPlayerId}` : '—'}
                  </td>
                  <td className="px-4 py-2.5 text-right text-ink-200 tabular-nums">{r.blobCount}</td>
                  <td className="px-4 py-2.5 text-right whitespace-nowrap">
                    <Link to={`/rooms/${r.id}`} className="btn-ghost text-xs">Manage</Link>
                    <button onClick={() => setPendingDelete(r)} className="btn-ghost text-xs text-danger ml-1" title="Soft archive">
                      <Trash />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table></div>
        )}
      </div>

      <Confirm
        open={pendingDelete !== null}
        onClose={() => setPendingDelete(null)}
        title="Archive room"
        body={<>Set <span className="font-medium text-ink-50">{pendingDelete?.name}</span> to State=1 (Archived)? The room will stop appearing in browse/search but stays restorable.</>}
        confirmLabel="Archive"
        destructive
        onConfirm={del}
      />

      {pendingPurge && (
        <PurgeRoomModal
          room={pendingPurge}
          onClose={() => setPendingPurge(null)}
          onPurged={refresh}
        />
      )}
    </div>
  );
}

// ── Per-room hard-delete modal with two typed-name confirmations ─────────
// The previous bulk "purge all custom" button was removed — too easy to
// click by accident. Each room now gets its own purge flow that requires
// typing the room's exact name TWICE in separate inputs (case-sensitive
// per the server's StringComparison.Ordinal check). Same pattern GitHub
// uses for "delete repository" — slows a fat finger down enough to read
// what they're about to wipe.

function PurgeRoomModal({
  room,
  onClose,
  onPurged,
}: {
  room: Room;
  onClose: () => void;
  onPurged: () => void;
}) {
  const [confirm1, setConfirm1] = useState('');
  const [confirm2, setConfirm2] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  // Both inputs MUST exactly equal the room name. The server validates
  // identically — keeping the same comparison here means we disable the
  // submit button rather than letting the user click through to a 400.
  const ready = confirm1 === room.name && confirm2 === room.name && !busy;

  // Reset typed values whenever the modal switches to a different room.
  useEffect(() => {
    setConfirm1('');
    setConfirm2('');
  }, [room.id]);

  const submit = async () => {
    if (!ready) return;
    setBusy(true);
    try {
      const res = await api<{ deleted: number; blobs: number; scenes: number }>(
        `/rooms/${room.id}/purge`,
        {
          method: 'POST',
          body: {
            Reason: 'admin',
            ConfirmName1: confirm1,
            ConfirmName2: confirm2,
          },
        },
      );
      toast.push(
        `Purged ${room.name} (${res.blobs} blobs · ${res.scenes} scenes)`,
        'success',
      );
      onPurged();
      onClose();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Purge room (hard delete)"
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button
          onClick={submit}
          disabled={!ready}
          className="btn-danger text-xs"
          title={!ready ? 'Type the room name in both inputs to enable purge' : undefined}
        >
          {busy ? 'Purging…' : 'Purge permanently'}
        </button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <p>
          Hard-delete <span className="font-medium text-ink-50">{room.name}</span> (room #{room.id})?
          This drops the room row, every scene, every per-room
          <code className="font-mono text-xs mx-1">.room</code>
          blob, and the thumbnail. Shared HTR / PV image assets stay. The
          Name frees up so a re-import works.
        </p>
        <p className="text-xs text-warn">
          Irreversible — no soft-archive fallback.
        </p>
        <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">
          To confirm, type the room name <span className="font-mono text-ink-100">{room.name}</span> in BOTH inputs below.
          Case-sensitive.
        </div>
        <label className="flex flex-col gap-1">
          <span className="label">Confirm name (1 of 2)</span>
          <input
            value={confirm1}
            onChange={e => setConfirm1(e.target.value)}
            className="input font-mono"
            placeholder={room.name}
            autoComplete="off"
            spellCheck={false}
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Confirm name (2 of 2)</span>
          <input
            value={confirm2}
            onChange={e => setConfirm2(e.target.value)}
            className="input font-mono"
            placeholder={room.name}
            autoComplete="off"
            spellCheck={false}
          />
        </label>
      </div>
    </Modal>
  );
}
