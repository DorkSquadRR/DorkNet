using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Data;

/// <summary>Idempotent startup pass that applies data transforms which
/// can't be derived from the entity model (i.e. don't belong in an EF
/// migration). Runs once on every server boot after
/// <c>db.Database.MigrateAsync()</c>; each upgrade is structured as
/// "read current state → apply if needed → log outcome", so re-running
/// on a fully-migrated DB is a no-op (just a string of "skipped, already
/// migrated" log lines).
///
/// <para>This file is the home for transforms that previously lived in
/// individual EF migrations (bucket C of the migration consolidation).
/// New entries go here when:
/// <list type="bullet">
///   <item>A seed row's <c>Name</c> changes between releases (existing
///   deployments need a UPDATE; fresh installs already see the new
///   name in seed data).</item>
///   <item>Existing rows need a content fix-up that the entity model
///   can't express (e.g. backfilling a computed column).</item>
/// </list>
/// Every upgrade method MUST be:
/// <list type="bullet">
///   <item>Idempotent — safe to call repeatedly</item>
///   <item>Self-checking — reads the current DB state first and only
///   modifies if the transform hasn't already been applied</item>
///   <item>Logged — emits one "applied" or "skipped, already migrated"
///   line so the boot log shows exactly which upgrades fired</item>
/// </list></para>
/// </summary>
public static class LegacyUpgrades
{
    public static async Task RunAsync(DorkNetDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        await RenameBloodMoonToCrescendoAsync(db, logger, ct);
        await CoercePhotonRegionAsync(db, config, logger, ct);
        // Future data transforms go here, one per release. Keep them
        // narrowly scoped — anything that can be expressed as a
        // schema change should be a migration instead.
    }

    /// <summary>Coerce every <see cref="Entities.PrivateInstanceEntity.PhotonRegion"/>
    /// row to the apex's current <c>Photon:CloudRegion</c>.
    ///
    /// <para>Background: older dorm rows still carry whatever region was
    /// in config when their dorm was first registered. Without this
    /// rewrite, an invitee whose <c>/goto/invite</c> resolves to a stale
    /// row gets sent to the old region while the inviter's most recent
    /// <c>/goto/room/DormRoom</c> puts them on the current region — two
    /// parallel Photon rooms, players can't see each other.
    /// <c>PrivateInstanceService.EnsureForDormAsync</c> now refreshes
    /// PhotonRegion on each owner /goto so going forward this drift is
    /// impossible; this is the one-shot cleanup for the pre-fix backlog.</para>
    ///
    /// <para>Originally ran as a RunPatchAsync UPDATE in the Postgres-only
    /// schema-patch block in Program.cs; consolidated here so SQLite
    /// dev DBs (which can drift the same way if you change Photon:CloudRegion
    /// between boots) get the same fix-up.</para>
    /// </summary>
    private static async Task CoercePhotonRegionAsync(
        DorkNetDbContext db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var region = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        var updated = await db.PrivateInstances
            .Where(p => p.PhotonRegion != region)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PhotonRegion, region), ct);
        if (updated == 0)
        {
            logger.LogInformation("[legacy-upgrade] CoercePhotonRegion: skipped (all PrivateInstances already on {Region})", region);
        }
        else
        {
            logger.LogInformation("[legacy-upgrade] CoercePhotonRegion: applied — {Count} PrivateInstances coerced to {Region}", updated, region);
        }
    }

    /// <summary>The Crimson Cauldron / Blood Moon seed room was renamed
    /// to Crescendo in May 2026 (and re-themed: new description, new
    /// image, no longer hidden from browse). Old deployments still have
    /// the BloodMoon row by primary key — rename it in place and clear
    /// the hidden flag. Fresh installs ship with the Crescendo seed
    /// directly and this upgrade no-ops.
    ///
    /// <para>Originally ran as
    /// <c>20260521155605_RenameBloodMoonToCrescendo</c>; consolidated
    /// here when the per-feature migrations were collapsed into a
    /// single Initial.</para>
    ///
    /// <para>Concurrency: <c>Rooms.Name</c> has a unique index
    /// (DorkNetDbContext.cs HasIndex(r=>r.Name).IsUnique()). Two
    /// replicas booting in parallel against a legacy DB could both see
    /// BloodMoon + no Crescendo and race to do the rename — the loser
    /// would hit a unique-constraint exception on SaveChangesAsync.
    /// We catch that, refresh, and re-evaluate so the race is
    /// observationally a single rename.</para>
    /// </summary>
    private static async Task RenameBloodMoonToCrescendoAsync(
        DorkNetDbContext db, ILogger logger, CancellationToken ct)
    {
        // Re-runnable loop: if a concurrent boot beats us to the rename,
        // catch the unique-constraint exception, drop the cached entity,
        // and re-read the DB state. After at most 2 iterations we're
        // either no-op (Crescendo now exists) or the only writer.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var crescendoExists = await db.Rooms.AnyAsync(r => r.Name == "Crescendo", ct);
            var bloodMoon = await db.Rooms.FirstOrDefaultAsync(r => r.Name == "BloodMoon", ct);

            if (bloodMoon is null && crescendoExists)
            {
                // Most common path on a freshly-deployed server: seed
                // shipped Crescendo, nothing to migrate.
                logger.LogInformation("[legacy-upgrade] RenameBloodMoonToCrescendo: skipped (already on Crescendo)");
                return;
            }

            if (bloodMoon is null && !crescendoExists)
            {
                // Neither exists — non-seeded test DB. No-op.
                logger.LogInformation("[legacy-upgrade] RenameBloodMoonToCrescendo: skipped (no BloodMoon and no Crescendo present)");
                return;
            }

            try
            {
                if (bloodMoon is not null && !crescendoExists)
                {
                    bloodMoon.Name = "Crescendo";
                    bloodMoon.Description = "Brave the haunted halls of Castle Dracula and survive the night.";
                    bloodMoon.ImageName = "by3mjs9jbozpdvu6g9aje7jgz.png";
                    bloodMoon.HiddenFromBrowse = false;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation(
                        "[legacy-upgrade] RenameBloodMoonToCrescendo: applied — renamed Rooms.Id={Id} BloodMoon → Crescendo",
                        bloodMoon.Id);
                    return;
                }

                if (bloodMoon is not null && crescendoExists)
                {
                    // Both exist — the rename ran on a different row at
                    // some point, and the original BloodMoon row is now
                    // a stale duplicate. Park it under CrescendoLegacy
                    // and hide from browse so /goto/name/... still
                    // resolves Crescendo to the right row.
                    bloodMoon.Name = "CrescendoLegacy";
                    bloodMoon.HiddenFromBrowse = true;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation(
                        "[legacy-upgrade] RenameBloodMoonToCrescendo: applied — Rooms.Id={Id} parked as CrescendoLegacy (Crescendo already existed on a different row)",
                        bloodMoon.Id);
                    return;
                }
            }
            catch (DbUpdateException ex) when (attempt == 1)
            {
                // Another replica won the rename race. Detach the
                // stale entity and loop — second iteration will see
                // Crescendo exists and exit cleanly.
                logger.LogInformation(
                    "[legacy-upgrade] RenameBloodMoonToCrescendo: lost race on attempt {Attempt}, retrying ({Message})",
                    attempt, ex.GetBaseException().Message);
                if (bloodMoon is not null) db.Entry(bloodMoon).State = EntityState.Detached;
            }
        }
    }
}
