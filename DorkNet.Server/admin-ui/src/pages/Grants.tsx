import { PageHeader } from '../components/PageHeader';

// Maker-pen-anywhere needs server-side integration with CdnController
// (substitute the room blob for flagged players so they get the
// all-permissions PersistedRoomData) AND a PlayerEntity.HasGlobalMakerPen
// column with an EF migration. Wiring is non-trivial enough to keep this
// page as a clearly-labeled placeholder until that lands — saves shipping
// a button that does nothing.
export function Grants() {
  return (
    <div>
      <PageHeader
        title="Grant maker pen"
        blurb="Flag players who should receive maker-pen permissions in every room they load."
      />
      <div className="card !p-6 max-w-2xl space-y-3 text-sm text-ink-300">
        <div className="rounded-lg border border-warn/30 bg-warn/10 px-3 py-2 text-xs text-warn">
          <strong className="text-warn">Not yet wired up.</strong> The endpoint and CDN integration land in a follow-up commit.
        </div>
        <p>
          The plan: add <span className="font-mono text-xs text-ink-100">HasGlobalMakerPen</span> to
          <span className="font-mono text-xs text-ink-100"> PlayerEntity</span>, intercept room blob
          fetches in <span className="font-mono text-xs text-ink-100">CdnController</span> for flagged players,
          and substitute the all-permissions default blob that
          <span className="font-mono text-xs text-ink-100"> RoomDataBlobService</span> already produces.
          Result: the player loads any room with full Maker Pen / invention spawn permissions, regardless of the
          room's owner-defined roles.
        </p>
        <p className="text-xs text-ink-400">
          Until then, use the Player → Grants tab to bestow per-item inventory grants (Maker Pen is granted via
          inventory in some flows).
        </p>
      </div>
    </div>
  );
}
