import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../lib/api';
import type { RoomDetail, RoomInstance, RoomRoleGrant } from '../lib/types';
import { profileImageUrl } from '../lib/types';
import { useApi } from '../lib/useApi';
import { PageHeader } from '../components/PageHeader';
import { Empty } from '../components/Empty';
import { Modal } from '../components/Modal';
import { Confirm } from '../components/Confirm';
import { useToast } from '../components/Toast';
import { ArrowLeft, RefreshCw, Trash } from '../components/Icons';

// Unified per-room admin view. One screen replaces what used to be
// scattered across Rooms / RR Originals / Instances / individual edit
// modals: general info edit, owner / co-owner / mod / host
// management, live photon instances, recent visitors, danger zone.

type Tab = 'general' | 'roles' | 'instances' | 'leaderboards' | 'visitors' | 'danger';

const ROLE_LABEL: Record<0 | 1 | 2, string> = {
  0: 'Co-owner',
  1: 'Moderator',
  2: 'Host',
};

const ACCESSIBILITY: Record<number, string> = {
  0: 'Private',
  1: 'Public',
  2: 'Friends only',
};

export function Room() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const roomId = Number(id);
  const { data: room, loading, error, refresh } = useApi<RoomDetail>(`/rooms/${roomId}`);
  const [tab, setTab] = useState<Tab>('general');

  if (Number.isNaN(roomId) || roomId <= 0) {
    return (
      <div>
        <PageHeader title="Room" />
        <Empty title="Invalid room id" />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={room?.name ?? `Room #${roomId}`}
        blurb={room?.description || undefined}
        actions={<>
          <Link to="/rooms" className="btn-ghost text-xs"><ArrowLeft /> Back to rooms</Link>
          <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
            <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
          </button>
        </>}
      />

      {error && <div className="card px-4 py-3 text-sm text-danger mb-4">{error}</div>}
      {loading && !room && <div className="text-sm text-ink-400">Loading…</div>}

      {room && (
        <>
          <RoomHeaderCard room={room} />
          <div className="flex gap-1 mt-4 mb-3 overflow-x-auto border-b border-ink-800">
            {(['general', 'roles', 'instances', 'leaderboards', 'visitors', 'danger'] as Tab[]).map(t => (
              <button
                key={t}
                onClick={() => setTab(t)}
                className={`px-3 py-2 text-sm whitespace-nowrap border-b-2 -mb-px ${
                  tab === t
                    ? 'border-brand-500 text-brand-100'
                    : 'border-transparent text-ink-300 hover:text-ink-50'
                }`}
              >
                {t === 'general' ? 'General' :
                 t === 'roles' ? 'Roles & ownership' :
                 t === 'instances' ? 'Live instances' :
                 t === 'leaderboards' ? 'Leaderboards' :
                 t === 'visitors' ? 'Recent visitors' :
                 'Danger zone'}
              </button>
            ))}
          </div>

          {tab === 'general' && <GeneralTab room={room} onSaved={refresh} />}
          {tab === 'roles' && <RolesTab room={room} onChanged={refresh} />}
          {tab === 'instances' && <InstancesTab roomId={room.id} />}
          {tab === 'leaderboards' && <LeaderboardsTab roomId={room.id} />}
          {tab === 'visitors' && <VisitorsTab roomId={room.id} />}
          {tab === 'danger' && <DangerTab room={room} onAfter={() => navigate('/rooms')} />}
        </>
      )}
    </div>
  );
}

// ── Header card ──────────────────────────────────────────────────────

function RoomHeaderCard({ room }: { room: RoomDetail }) {
  const img = profileImageUrl(room.imageName || null, 192);
  const tags = room.tagsCsv.split(',').map(s => s.trim()).filter(Boolean);
  return (
    <div className="card p-4 flex flex-col sm:flex-row gap-4 items-start">
      <div className="size-24 rounded-lg overflow-hidden bg-ink-800 flex-shrink-0 flex items-center justify-center">
        {img
          ? <img src={img} alt={room.name} className="size-full object-cover" />
          : <div className="text-ink-500 text-xs">no image</div>}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-lg font-semibold text-ink-50">{room.name}</h2>
          {room.isDormRoom && <span className="badge-neutral">Dorm</span>}
          {!room.isDormRoom && room.isAGRoom && room.owner.id === 1 && <span className="badge-admin">RR Original</span>}
          {!room.isDormRoom && room.owner.id !== 1 && <span className="badge-neutral">Custom</span>}
          {room.state === 1 && <span className="badge-banned">Archived</span>}
        </div>
        <div className="text-xs text-ink-400 mt-1">
          #{room.id} · {ACCESSIBILITY[room.accessibility] ?? `Access ${room.accessibility}`} · {room.cloningAllowed ? 'Cloning allowed' : 'No cloning'}
        </div>
        <div className="mt-2 flex flex-wrap gap-3 text-xs">
          <StatPill label="Cheers" value={room.cheerCount} />
          <StatPill label="Visits" value={room.visitCount} />
          <StatPill label="Visitors" value={room.visitorCount} />
          <StatPill label="Scenes" value={room.sceneCount} />
          <StatPill label="Blobs" value={room.blobCount} />
          <StatPill label="Hot" value={room.hotScore.toFixed(1)} />
        </div>
        {tags.length > 0 && (
          <div className="mt-2 flex flex-wrap gap-1">
            {tags.map(t => <span key={t} className="badge-neutral text-[10px]">#{t}</span>)}
          </div>
        )}
      </div>
      <div className="text-xs text-ink-400 sm:text-right space-y-1">
        <div>
          Owner: <Link to={`/players?focus=${room.owner.id}`} className="text-ink-200 hover:text-brand-200">@{room.owner.username || `#${room.owner.id}`}</Link>
        </div>
        <div>Created {new Date(room.createdAt).toLocaleDateString()}</div>
        <div>Updated {new Date(room.updatedAt).toLocaleDateString()}</div>
      </div>
    </div>
  );
}

function StatPill({ label, value }: { label: string; value: number | string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-md bg-ink-900 px-2 py-1 ring-1 ring-inset ring-ink-800">
      <span className="text-ink-400">{label}</span>
      <span className="text-ink-100 font-medium tabular-nums">{value}</span>
    </span>
  );
}

// ── General tab — name/desc/image/access/cloning/tags/hot ──────────────

function GeneralTab({ room, onSaved }: { room: RoomDetail; onSaved: () => void }) {
  const toast = useToast();
  const [description, setDescription] = useState(room.description);
  const [accessibility, setAccessibility] = useState(room.accessibility);
  const [cloningAllowed, setCloningAllowed] = useState(room.cloningAllowed);
  const [tagsCsv, setTagsCsv] = useState(room.tagsCsv);
  const [hotScore, setHotScore] = useState(String(room.hotScore));
  const [imageName, setImageName] = useState(room.imageName);
  const [busy, setBusy] = useState(false);

  // Re-sync local state if a refresh brings new server values in.
  useEffect(() => {
    setDescription(room.description);
    setAccessibility(room.accessibility);
    setCloningAllowed(room.cloningAllowed);
    setTagsCsv(room.tagsCsv);
    setHotScore(String(room.hotScore));
    setImageName(room.imageName);
  }, [room.id, room.updatedAt]);

  const save = async () => {
    setBusy(true);
    try {
      const hot = Number(hotScore);
      await api(`/rooms/${room.id}/props`, {
        method: 'POST',
        body: {
          Description: description,
          Accessibility: accessibility,
          CloningAllowed: cloningAllowed,
          TagsCsv: tagsCsv,
          HotScore: Number.isFinite(hot) ? hot : undefined,
          ImageName: imageName,
        },
      });
      toast.push('Saved', 'success');
      onSaved();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="card p-4 space-y-3 max-w-2xl">
      <label className="flex flex-col gap-1">
        <span className="label">Description</span>
        <textarea
          value={description}
          onChange={e => setDescription(e.target.value)}
          className="input min-h-[80px]"
          maxLength={2000}
        />
      </label>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <label className="flex flex-col gap-1">
          <span className="label">Accessibility</span>
          <select
            value={accessibility}
            onChange={e => setAccessibility(Number(e.target.value))}
            className="input"
          >
            <option value={0}>Private</option>
            <option value={1}>Public</option>
            <option value={2}>Friends only</option>
          </select>
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Hot score</span>
          <input
            type="number"
            step="0.1"
            value={hotScore}
            onChange={e => setHotScore(e.target.value)}
            className="input tabular-nums"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Tags (comma-separated)</span>
          <input
            value={tagsCsv}
            onChange={e => setTagsCsv(e.target.value)}
            placeholder="featured, recroomoriginal"
            className="input"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Image blob name</span>
          <input
            value={imageName}
            onChange={e => setImageName(e.target.value)}
            placeholder="img_p1_xxxxxxxxxxxx.jpg"
            className="input font-mono text-xs"
          />
        </label>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={cloningAllowed}
          onChange={e => setCloningAllowed(e.target.checked)}
        />
        <span>Cloning allowed</span>
      </label>
      <div className="flex gap-2 pt-2 border-t border-ink-800">
        <button onClick={save} disabled={busy} className="btn-primary text-xs">
          {busy ? 'Saving…' : 'Save changes'}
        </button>
      </div>
    </div>
  );
}

// ── Roles tab — owner + co-owners + mods + hosts ────────────────────────

function RolesTab({ room, onChanged }: { room: RoomDetail; onChanged: () => void }) {
  const toast = useToast();
  const [addingRole, setAddingRole] = useState<0 | 1 | 2 | null>(null);
  const [transferOpen, setTransferOpen] = useState(false);

  const byRole = useMemo(() => {
    const out: Record<0 | 1 | 2, RoomRoleGrant[]> = { 0: [], 1: [], 2: [] };
    for (const r of room.roles) out[r.role].push(r);
    return out;
  }, [room.roles]);

  const remove = async (g: RoomRoleGrant) => {
    try {
      await api(`/rooms/${room.id}/roles/${g.playerId}/${g.role}`, { method: 'DELETE' });
      toast.push(`Removed ${ROLE_LABEL[g.role]} grant for @${g.player.username}`, 'success');
      onChanged();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div className="space-y-4">
      <div className="card p-4">
        <div className="flex items-center justify-between gap-2">
          <div>
            <div className="label">Owner</div>
            <PlayerRow player={room.owner} />
          </div>
          <button onClick={() => setTransferOpen(true)} className="btn-secondary text-xs">Transfer ownership</button>
        </div>
      </div>

      {([0, 1, 2] as const).map(role => (
        <div key={role} className="card overflow-hidden">
          <div className="flex items-center justify-between gap-2 px-4 py-2.5 border-b border-ink-800">
            <h3 className="text-sm font-semibold text-ink-50">{ROLE_LABEL[role]}s</h3>
            <button onClick={() => setAddingRole(role)} className="btn-secondary text-xs">Add</button>
          </div>
          {byRole[role].length === 0
            ? <div className="px-4 py-4 text-xs text-ink-400">No {ROLE_LABEL[role].toLowerCase()} grants.</div>
            : <ul className="divide-y divide-ink-800">
                {byRole[role].map(g => (
                  <li key={g.id} className="flex items-center justify-between gap-2 px-4 py-2.5">
                    <PlayerRow player={g.player} />
                    <div className="flex items-center gap-2">
                      {!g.accepted && <span className="badge-neutral">Pending</span>}
                      <span className="text-xs text-ink-500">{new Date(g.grantedAt).toLocaleDateString()}</span>
                      <button onClick={() => remove(g)} className="btn-ghost text-xs text-danger"><Trash /> Revoke</button>
                    </div>
                  </li>
                ))}
              </ul>}
        </div>
      ))}

      {addingRole !== null && (
        <AddRoleModal
          roomId={room.id}
          role={addingRole}
          onClose={() => setAddingRole(null)}
          onAdded={() => { setAddingRole(null); onChanged(); }}
        />
      )}
      {transferOpen && (
        <TransferOwnerModal
          room={room}
          onClose={() => setTransferOpen(false)}
          onTransferred={() => { setTransferOpen(false); onChanged(); }}
        />
      )}
    </div>
  );
}

function PlayerRow({ player }: { player: { id: number; username: string; displayName: string; profileImageName: string | null } }) {
  const img = profileImageUrl(player.profileImageName, 64);
  return (
    <Link to={`/players?focus=${player.id}`} className="flex items-center gap-2 min-w-0 hover:text-brand-100">
      <div className="size-7 rounded-md overflow-hidden bg-ink-800 flex-shrink-0">
        {img && <img src={img} alt="" className="size-full object-cover" />}
      </div>
      <div className="min-w-0">
        <div className="text-sm text-ink-50 truncate">@{player.username || `#${player.id}`}</div>
        {player.displayName && player.displayName !== player.username &&
          <div className="text-xs text-ink-400 truncate">{player.displayName}</div>}
      </div>
    </Link>
  );
}

function AddRoleModal({ roomId, role, onClose, onAdded }: {
  roomId: number; role: 0 | 1 | 2; onClose: () => void; onAdded: () => void;
}) {
  const [playerIdStr, setPlayerIdStr] = useState('');
  const [accepted, setAccepted] = useState(true);
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const submit = async () => {
    const pid = Number(playerIdStr);
    if (!Number.isFinite(pid) || pid <= 0) {
      toast.push('Enter a numeric player id', 'error');
      return;
    }
    setBusy(true);
    try {
      await api(`/rooms/${roomId}/roles`, {
        method: 'POST',
        body: { PlayerId: pid, Role: role, Accepted: accepted },
      });
      toast.push(`Granted ${ROLE_LABEL[role]} to #${pid}`, 'success');
      onAdded();
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
      title={`Add ${ROLE_LABEL[role].toLowerCase()}`}
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={submit} className="btn-primary text-xs" disabled={busy}>
          {busy ? 'Adding…' : 'Add'}
        </button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <label className="flex flex-col gap-1">
          <span className="label">Player id</span>
          <input
            value={playerIdStr}
            onChange={e => setPlayerIdStr(e.target.value)}
            className="input font-mono"
            placeholder="1362428"
            autoFocus
          />
        </label>
        <label className="flex items-center gap-2 text-xs">
          <input type="checkbox" checked={accepted} onChange={e => setAccepted(e.target.checked)} />
          <span>Mark as accepted (otherwise shows as pending invite)</span>
        </label>
      </div>
    </Modal>
  );
}

function TransferOwnerModal({ room, onClose, onTransferred }: {
  room: RoomDetail; onClose: () => void; onTransferred: () => void;
}) {
  const [pid, setPid] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const ready = pid && confirm === room.name && !busy;

  const submit = async () => {
    if (!ready) return;
    setBusy(true);
    try {
      await api(`/rooms/${room.id}/owner`, {
        method: 'POST',
        body: { NewCreatorPlayerId: Number(pid) },
      });
      toast.push(`Ownership transferred to #${pid}`, 'success');
      onTransferred();
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
      title="Transfer ownership"
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={submit} className="btn-danger text-xs" disabled={!ready}>
          {busy ? 'Transferring…' : 'Transfer'}
        </button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <p>Reassign the room's creator. The previous owner (@{room.owner.username}) keeps a Co-owner grant automatically so they don't lose access.</p>
        <label className="flex flex-col gap-1">
          <span className="label">New owner player id</span>
          <input value={pid} onChange={e => setPid(e.target.value)} className="input font-mono" placeholder="1362428" />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Type the room name to confirm</span>
          <input value={confirm} onChange={e => setConfirm(e.target.value)} className="input font-mono" placeholder={room.name} />
        </label>
      </div>
    </Modal>
  );
}

// ── Instances tab — live photon matches in this room ────────────────────

function InstancesTab({ roomId }: { roomId: number }) {
  const toast = useToast();
  const { data, loading, error, refresh } = useApi<RoomInstance[]>(`/rooms/${roomId}/instances`);
  const [pullInto, setPullInto] = useState<RoomInstance | null>(null);
  const [closing, setClosing] = useState<RoomInstance | null>(null);

  const close = async (inst: RoomInstance) => {
    try {
      const res = await api<{ kicked: number[] }>(
        `/rooms/${roomId}/instances/${inst.roomInstanceId}/close`,
        { method: 'POST', body: { Reason: 'Instance closed by admin' } },
      );
      toast.push(`Closed instance — kicked ${res.kicked.length}`, 'success');
      refresh();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  return (
    <div className="card overflow-hidden">
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-ink-800">
        <div className="text-xs text-ink-400">{data ? `${data.length} active` : ''}</div>
        <button onClick={refresh} className="btn-secondary text-xs" disabled={loading}>
          <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
        </button>
      </div>
      {error && <div className="px-4 py-3 text-sm text-danger">{error}</div>}
      {data && data.length === 0 && <Empty title="No active instances" />}
      {data && data.length > 0 && (
        <ul className="divide-y divide-ink-800">
          {data.map(inst => (
            <li key={inst.roomInstanceId} className="px-4 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
                <div className="flex flex-wrap items-center gap-2 min-w-0">
                  <span className="text-sm font-medium text-ink-50">Instance #{inst.roomInstanceId}</span>
                  <span className="badge-neutral">SubRoom {inst.subRoomId}</span>
                  {inst.isPrivate && <span className="badge-banned">Private</span>}
                  <span className="text-xs text-ink-500 font-mono truncate">{inst.photonRegionId}/{inst.photonRoomId}</span>
                </div>
                <div className="flex items-center gap-1">
                  <button onClick={() => setPullInto(inst)} className="btn-secondary text-xs">Pull player in</button>
                  <button onClick={() => setClosing(inst)} className="btn-ghost text-xs text-danger">
                    <Trash /> Close
                  </button>
                </div>
              </div>
              <div className="flex flex-wrap gap-2">
                {inst.participants.map(p => (
                  <Link
                    key={p.id}
                    to={`/players?focus=${p.id}`}
                    className={`inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs ring-1 ring-inset ${
                      p.isMaster
                        ? 'bg-brand-500/15 text-brand-100 ring-brand-500/30'
                        : 'bg-ink-900 text-ink-200 ring-ink-800 hover:bg-ink-800'
                    }`}
                  >
                    {p.isMaster && <span className="text-[10px] uppercase font-semibold tracking-wider text-brand-300">Master</span>}
                    <span>@{p.username}</span>
                  </Link>
                ))}
                {inst.participants.length === 0 && <span className="text-xs text-ink-500">empty</span>}
              </div>
            </li>
          ))}
        </ul>
      )}

      {pullInto && (
        <PullPlayerModal
          roomId={roomId}
          instance={pullInto}
          onClose={() => setPullInto(null)}
          onPulled={() => { setPullInto(null); refresh(); }}
        />
      )}
      <Confirm
        open={closing !== null}
        onClose={() => setClosing(null)}
        title="Close instance"
        body={<>Kick all {closing?.participants.length ?? 0} player(s) out of instance <span className="font-mono text-ink-100">#{closing?.roomInstanceId}</span>? The Photon room dies once it's empty.</>}
        confirmLabel="Close instance"
        destructive
        onConfirm={() => { if (closing) close(closing); }}
      />
    </div>
  );
}

function PullPlayerModal({ roomId, instance, onClose, onPulled }: {
  roomId: number; instance: RoomInstance; onClose: () => void; onPulled: () => void;
}) {
  const [pidStr, setPidStr] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const submit = async () => {
    const pid = Number(pidStr);
    if (!Number.isFinite(pid) || pid <= 0) { toast.push('Enter a numeric player id', 'error'); return; }
    setBusy(true);
    try {
      await api(`/rooms/${roomId}/instances/${instance.roomInstanceId}/pull`, {
        method: 'POST',
        body: { PlayerId: pid },
      });
      toast.push(`Pulled #${pid} into instance`, 'success');
      onPulled();
    } catch (e) { toast.push((e as Error).message, 'error'); }
    finally { setBusy(false); }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Pull a player into instance #${instance.roomInstanceId}`}
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={submit} className="btn-primary text-xs" disabled={busy}>{busy ? 'Pulling…' : 'Pull in'}</button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <p className="text-xs text-ink-400">
          Re-points the target player's presence at this exact Photon shard ({instance.photonRegionId}/{instance.photonRoomId})
          and pushes a SubscriptionUpdateGameSession so their watch joins the same instance the existing participants are in.
        </p>
        <label className="flex flex-col gap-1">
          <span className="label">Player id</span>
          <input value={pidStr} onChange={e => setPidStr(e.target.value)} className="input font-mono" placeholder="1362428" autoFocus />
        </label>
      </div>
    </Modal>
  );
}

// ── Leaderboards tab — channels mapped to this room + orphan registry ──

interface RoomLeaderboardRow {
  channel: number;
  name: string;
  lowerIsBetter: boolean;
  valueFormat: string;
  entryCount: number;
  bestValue: number | null;
}

interface OrphanRow {
  channel: number;
  entryCount: number;
  lastSeen: string;
}

interface ChannelEntries {
  channel: number;
  name: string;
  lowerIsBetter: boolean;
  valueFormat: string;
  entries: Array<{
    rank: number;
    id: number;
    playerId: number;
    username: string | null;
    displayName: string | null;
    value: number;
    updatedAt: string;
  }>;
}

function formatChannelValue(v: number | null | undefined, fmt: string) {
  if (v === null || v === undefined) return '—';
  if (fmt === 'time-ms') {
    const total = v;
    const m = Math.floor(total / 60000);
    const s = Math.floor((total % 60000) / 1000);
    const ms = total % 1000;
    return `${m}:${s.toString().padStart(2, '0')}.${ms.toString().padStart(3, '0')}`;
  }
  return v.toLocaleString();
}

function LeaderboardsTab({ roomId }: { roomId: number }) {
  const toast = useToast();
  const lb = useApi<RoomLeaderboardRow[]>(`/rooms/${roomId}/leaderboards`);
  const orphans = useApi<OrphanRow[]>(`/leaderboards/orphans`);
  const [addOpen, setAddOpen] = useState(false);
  const [adoptChannel, setAdoptChannel] = useState<number | null>(null);
  const [expanded, setExpanded] = useState<number | null>(null);

  const refresh = () => { lb.refresh(); orphans.refresh(); };

  return (
    <div className="space-y-4">
      <div className="card overflow-hidden">
        <div className="flex items-center justify-between gap-2 px-4 py-2.5 border-b border-ink-800">
          <h3 className="text-sm font-semibold text-ink-50">Channels mapped to this room</h3>
          <button onClick={() => setAddOpen(true)} className="btn-secondary text-xs">Register channel</button>
        </div>
        {lb.error && <div className="px-4 py-3 text-sm text-danger">{lb.error}</div>}
        {lb.data && lb.data.length === 0 && (
          <Empty title="No leaderboards yet — register a channel id, or adopt one from the orphan list below" />
        )}
        {lb.data && lb.data.length > 0 && (
          <ul className="divide-y divide-ink-800">
            {lb.data.map(c => (
              <li key={c.channel}>
                <div className="flex items-center justify-between gap-2 px-4 py-2.5">
                  <button
                    onClick={() => setExpanded(expanded === c.channel ? null : c.channel)}
                    className="text-left min-w-0 flex-1 hover:text-brand-100"
                  >
                    <div className="text-sm text-ink-50 truncate">{c.name}</div>
                    <div className="text-xs text-ink-400 tabular-nums">
                      Channel #{c.channel} · {c.entryCount} {c.entryCount === 1 ? 'entry' : 'entries'} · {c.lowerIsBetter ? 'lower is better' : 'higher is better'}
                    </div>
                  </button>
                  <div className="flex items-center gap-3">
                    <div className="text-right">
                      <div className="text-[10px] uppercase text-ink-500">Best</div>
                      <div className="text-sm tabular-nums text-ink-100">{formatChannelValue(c.bestValue, c.valueFormat)}</div>
                    </div>
                    <button onClick={() => setExpanded(expanded === c.channel ? null : c.channel)} className="btn-ghost text-xs">
                      {expanded === c.channel ? 'Hide' : 'View'}
                    </button>
                    <button
                      onClick={async () => {
                        if (!confirm(`Unregister channel #${c.channel}? Scores stay, but it stops appearing under this room.`)) return;
                        try {
                          await api(`/leaderboards/meta/${c.channel}`, { method: 'DELETE' });
                          toast.push('Unregistered', 'success');
                          refresh();
                        } catch (e) { toast.push((e as Error).message, 'error'); }
                      }}
                      className="btn-ghost text-xs text-danger"
                    >
                      <Trash />
                    </button>
                  </div>
                </div>
                {expanded === c.channel && (
                  <ChannelEntriesPanel channel={c.channel} />
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="card overflow-hidden">
        <div className="flex items-center justify-between gap-2 px-4 py-2.5 border-b border-ink-800">
          <h3 className="text-sm font-semibold text-ink-50">Unmapped channels with score data</h3>
          <div className="text-xs text-ink-400">
            {orphans.data ? `${orphans.data.length} orphan${orphans.data.length === 1 ? '' : 's'}` : ''}
          </div>
        </div>
        <div className="px-4 py-2.5 text-xs text-ink-400 border-b border-ink-800">
          Play a stat-reporting game mode (Stunt Runner course, Paintball, etc.) and its channel id will appear here.
          Adopt the channel to attach it to this room and give it a name.
        </div>
        {orphans.data && orphans.data.length === 0 && <Empty title="No orphan channels — every channel with score rows is mapped" />}
        {orphans.data && orphans.data.length > 0 && (
          <ul className="divide-y divide-ink-800">
            {orphans.data.map(o => (
              <li key={o.channel} className="flex items-center justify-between gap-2 px-4 py-2.5">
                <div>
                  <div className="text-sm text-ink-50">Channel #{o.channel}</div>
                  <div className="text-xs text-ink-400 tabular-nums">{o.entryCount} entries · last update {new Date(o.lastSeen).toLocaleString()}</div>
                </div>
                <button onClick={() => setAdoptChannel(o.channel)} className="btn-secondary text-xs">Adopt for this room</button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {(addOpen || adoptChannel !== null) && (
        <RegisterChannelModal
          roomId={roomId}
          initialChannel={adoptChannel ?? undefined}
          onClose={() => { setAddOpen(false); setAdoptChannel(null); }}
          onSaved={() => { setAddOpen(false); setAdoptChannel(null); refresh(); }}
        />
      )}
    </div>
  );
}

function ChannelEntriesPanel({ channel }: { channel: number }) {
  const { data, loading, error } = useApi<ChannelEntries>(`/leaderboards/${channel}?take=25`);
  if (loading && !data) return <div className="px-6 py-3 text-xs text-ink-400 bg-ink-900/40">Loading…</div>;
  if (error) return <div className="px-6 py-3 text-xs text-danger bg-ink-900/40">{error}</div>;
  if (!data) return null;
  if (data.entries.length === 0) return <div className="px-6 py-3 text-xs text-ink-400 bg-ink-900/40">No entries yet.</div>;
  return (
    <div className="bg-ink-900/40 border-t border-ink-800">
      <table className="w-full text-xs">
        <thead className="text-[10px] uppercase tracking-wider text-ink-400">
          <tr>
            <th className="text-left font-medium px-4 py-1.5 w-10">#</th>
            <th className="text-left font-medium px-4 py-1.5">Player</th>
            <th className="text-right font-medium px-4 py-1.5">Value</th>
            <th className="text-right font-medium px-4 py-1.5">Updated</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-ink-800/60">
          {data.entries.map(e => (
            <tr key={e.id}>
              <td className="px-4 py-1.5 text-ink-300 tabular-nums">{e.rank}</td>
              <td className="px-4 py-1.5">
                <Link to={`/players?focus=${e.playerId}`} className="text-ink-100 hover:text-brand-200">@{e.username ?? `#${e.playerId}`}</Link>
              </td>
              <td className="px-4 py-1.5 text-right tabular-nums text-ink-100">{formatChannelValue(e.value, data.valueFormat)}</td>
              <td className="px-4 py-1.5 text-right text-ink-500">{new Date(e.updatedAt).toLocaleString()}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RegisterChannelModal({ roomId, initialChannel, onClose, onSaved }: {
  roomId: number; initialChannel?: number; onClose: () => void; onSaved: () => void;
}) {
  const toast = useToast();
  const [channel, setChannel] = useState(initialChannel?.toString() ?? '');
  const [name, setName] = useState('');
  const [lowerIsBetter, setLowerIsBetter] = useState(false);
  const [valueFormat, setValueFormat] = useState<'count' | 'time-ms' | 'score'>('count');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    const ch = Number(channel);
    if (!Number.isInteger(ch) || ch <= 0) { toast.push('Channel must be a positive integer', 'error'); return; }
    if (!name.trim()) { toast.push('Name required', 'error'); return; }
    setBusy(true);
    try {
      await api(`/leaderboards/meta`, {
        method: 'POST',
        body: { Channel: ch, RoomId: roomId, Name: name.trim(), LowerIsBetter: lowerIsBetter, ValueFormat: valueFormat },
      });
      toast.push(`Channel #${ch} mapped to this room`, 'success');
      onSaved();
    } catch (e) { toast.push((e as Error).message, 'error'); }
    finally { setBusy(false); }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={initialChannel !== undefined ? `Adopt channel #${initialChannel}` : 'Register leaderboard channel'}
      footer={<>
        <button onClick={onClose} className="btn-ghost text-xs" disabled={busy}>Cancel</button>
        <button onClick={submit} className="btn-primary text-xs" disabled={busy}>{busy ? 'Saving…' : 'Save'}</button>
      </>}
    >
      <div className="space-y-3 text-sm">
        <label className="flex flex-col gap-1">
          <span className="label">Channel id</span>
          <input
            value={channel}
            onChange={e => setChannel(e.target.value)}
            className="input font-mono"
            placeholder="7"
            disabled={initialChannel !== undefined}
            autoFocus={initialChannel === undefined}
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Display name</span>
          <input value={name} onChange={e => setName(e.target.value)} className="input" placeholder="Stunt Runner — Forest Loop" autoFocus={initialChannel !== undefined} />
        </label>
        <label className="flex flex-col gap-1">
          <span className="label">Value format</span>
          <select value={valueFormat} onChange={e => setValueFormat(e.target.value as 'count' | 'time-ms' | 'score')} className="input">
            <option value="count">Count (e.g. wins)</option>
            <option value="time-ms">Time in milliseconds</option>
            <option value="score">Score (points)</option>
          </select>
        </label>
        <label className="flex items-center gap-2 text-xs">
          <input type="checkbox" checked={lowerIsBetter} onChange={e => setLowerIsBetter(e.target.checked)} />
          <span>Lower value wins (race times, lap times, etc.)</span>
        </label>
      </div>
    </Modal>
  );
}

// ── Visitors tab — recent unique visitors w/ visit counts ───────────────

interface VisitorRow {
  playerId: number;
  username: string;
  displayName: string;
  visitCount: number;
  firstVisitAt: string;
  lastVisitAt: string;
}

function VisitorsTab({ roomId }: { roomId: number }) {
  const { data, loading, error, refresh } = useApi<VisitorRow[]>(`/rooms/${roomId}/visitors`);
  return (
    <div className="card overflow-hidden">
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-ink-800">
        <div className="text-xs text-ink-400">{data ? `${data.length} recent visitors` : ''}</div>
        <button onClick={refresh} className="btn-ghost text-xs" disabled={loading}>
          <RefreshCw className={loading ? 'animate-spin' : ''} /> Refresh
        </button>
      </div>
      {error && <div className="px-4 py-3 text-sm text-danger">{error}</div>}
      {data && data.length === 0 && <Empty title="No visitors yet" />}
      {data && data.length > 0 && (
        <div className="table-scroll"><table className="w-full text-sm min-w-[520px]">
          <thead className="text-[11px] uppercase tracking-wider text-ink-400 bg-ink-900/50 border-b border-ink-800">
            <tr>
              <th className="text-left font-medium px-4 py-2.5">Player</th>
              <th className="text-right font-medium px-4 py-2.5">Visits</th>
              <th className="text-right font-medium px-4 py-2.5">First</th>
              <th className="text-right font-medium px-4 py-2.5">Last</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-800">
            {data.map(v => (
              <tr key={v.playerId} className="table-row-hover">
                <td className="px-4 py-2 text-ink-50">
                  <Link to={`/players?focus=${v.playerId}`} className="hover:text-brand-200">@{v.username}</Link>
                </td>
                <td className="px-4 py-2 text-right tabular-nums">{v.visitCount}</td>
                <td className="px-4 py-2 text-right text-xs text-ink-400">{new Date(v.firstVisitAt).toLocaleDateString()}</td>
                <td className="px-4 py-2 text-right text-xs text-ink-400">{new Date(v.lastVisitAt).toLocaleDateString()}</td>
              </tr>
            ))}
          </tbody>
        </table></div>
      )}
    </div>
  );
}

// ── Danger zone — archive / purge ────────────────────────────────────────

function DangerTab({ room, onAfter }: { room: RoomDetail; onAfter: () => void }) {
  const toast = useToast();
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [purgeOpen, setPurgeOpen] = useState(false);
  const [confirm1, setConfirm1] = useState('');
  const [confirm2, setConfirm2] = useState('');
  const [busy, setBusy] = useState(false);

  const isCustom = !room.isDormRoom && room.owner.id !== 1;
  const purgeReady = confirm1 === room.name && confirm2 === room.name && !busy;

  const archive = async () => {
    try {
      await api(`/rooms/${room.id}`, { method: 'DELETE', body: { Reason: 'admin' } });
      toast.push(`Archived ${room.name}`, 'success');
      onAfter();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    }
  };

  const purge = async () => {
    if (!purgeReady) return;
    setBusy(true);
    try {
      const res = await api<{ deleted: number; blobs: number; scenes: number }>(
        `/rooms/${room.id}/purge`,
        { method: 'POST', body: { Reason: 'admin', ConfirmName1: confirm1, ConfirmName2: confirm2 } },
      );
      toast.push(`Purged ${room.name} (${res.blobs} blobs · ${res.scenes} scenes)`, 'success');
      onAfter();
    } catch (e) {
      toast.push((e as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3 max-w-2xl">
      <div className="card p-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-ink-50">Archive</h3>
            <p className="text-xs text-ink-400 mt-1">
              Soft delete — sets State=Archived. The room stops appearing in browse/search but stays restorable.
            </p>
          </div>
          <button onClick={() => setArchiveOpen(true)} className="btn-secondary text-xs text-danger">
            <Trash /> Archive
          </button>
        </div>
      </div>
      {isCustom && (
        <div className="card p-4 border-danger/40">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h3 className="text-sm font-semibold text-danger">Purge permanently</h3>
              <p className="text-xs text-ink-400 mt-1">
                Hard delete — drops the row, every scene, every per-room
                <code className="font-mono mx-1">.room</code> blob, and the thumbnail.
                The name frees up so a re-import works. Irreversible.
              </p>
              <div className="rounded-lg border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger mt-3">
                Type the room name <span className="font-mono text-ink-100">{room.name}</span> in BOTH inputs. Case-sensitive.
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 mt-3">
                <input value={confirm1} onChange={e => setConfirm1(e.target.value)} className="input font-mono" placeholder="confirm (1 of 2)" />
                <input value={confirm2} onChange={e => setConfirm2(e.target.value)} className="input font-mono" placeholder="confirm (2 of 2)" />
              </div>
            </div>
            <button onClick={() => setPurgeOpen(true)} disabled={!purgeReady} className="btn-danger text-xs">
              {busy ? 'Purging…' : 'Purge permanently'}
            </button>
          </div>
        </div>
      )}

      <Confirm
        open={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        title="Archive room"
        body={<>Set <span className="font-medium text-ink-50">{room.name}</span> to State=Archived?</>}
        confirmLabel="Archive"
        destructive
        onConfirm={archive}
      />
      <Confirm
        open={purgeOpen}
        onClose={() => setPurgeOpen(false)}
        title="Purge room permanently"
        body={<>This is irreversible. Continue?</>}
        confirmLabel="Yes, purge"
        destructive
        onConfirm={purge}
      />
    </div>
  );
}
