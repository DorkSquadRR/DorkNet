using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.Health;

/// <summary>
/// GET <c>/healthz</c> — liveness/readiness probe for Coolify rolling deploys.
///
/// Returns 503 until BOTH conditions hold:
/// 1. <see cref="MigrationsComplete"/> flag has been set by Program.cs after
///    <c>db.Database.Migrate()</c> returns (so a new replica that's still
///    running migrations doesn't get traffic shifted to it before its schema
///    is current).
/// 2. <c>db.Database.CanConnectAsync()</c> succeeds (catches transient DB
///    blips and prevents traffic during a Postgres restart).
///
/// Coolify's rolling-deploy strategy waits for the new container's healthz
/// to return 200 before draining the old one. Combined with a brief
/// (~5–10s) `start-first` overlap, this is the closest thing to zero
/// downtime we get with single-region hosting.
///
/// HostFilteringOptions is configured (via DomainConfig) to accept
/// <c>localhost</c> + <c>127.0.0.1</c> alongside the apex pool so the
/// Coolify internal healthcheck (which hits <c>localhost:8080/healthz</c>
/// from inside the container) reaches this handler — earlier
/// per-controller <c>[Host(...)]</c> filters would have 404ed it.
///
/// Distinct from <c>/v1/ping</c> on the ns subdomain — that returns a
/// timestamped payload the watch's keep-alive loop reads and is part of
/// the wire contract. We deliberately don't change it.
/// </summary>
[ApiController]
public class HealthController(DorkNetDbContext db) : ControllerBase
{
    /// <summary>Set to true by Program.cs after the EF Core
    /// <c>Migrate()</c> call returns successfully. Volatile because
    /// it's read from request threads concurrently with the startup
    /// thread's write. Process-local — every replica sets its own
    /// flag once its own startup migrations finish.</summary>
    public static volatile bool MigrationsComplete = false;

    // Order is more-negative than CdnController.Serve's wildcard
    // (Order = -100). Without this the catch-all matched <c>/healthz</c>
    // first on the <c>localhost</c> host (no subdomain) and returned
    // 404 → Docker's HEALTHCHECK <c>curl --fail</c> exited non-zero →
    // Coolify rolled the new container back.
    [HttpGet("/healthz", Order = -1000)]
    public async Task<IActionResult> Healthz()
    {
        if (!MigrationsComplete)
            return StatusCode(503, new { status = "starting", reason = "migrations_pending" });

        // CanConnectAsync uses a cheap "SELECT 1"-equivalent — fast enough
        // to run on every probe (~1ms for a warm pool). Cheaper than the
        // alternative of failing only on actual query timeouts.
        bool dbOk;
        try { dbOk = await db.Database.CanConnectAsync(); }
        catch { dbOk = false; }

        if (!dbOk)
            return StatusCode(503, new { status = "unhealthy", reason = "db_unreachable" });

        return Ok(new { status = "ok" });
    }
}
