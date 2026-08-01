using Microsoft.Extensions.Options;
using DorkNet.Server.Controllers.API.TestCaseManagement;
using DorkNet.Server.Data;

namespace DorkNet.Server.Services;

/// <summary>
/// Keeps linked test cases in step with their GitHub issues without anyone
/// having to press anything.
///
/// <para>A closed issue means the bug is fixed, so its test case is due for
/// another run; a reopened one means it came back. Waiting for a human to
/// notice that defeats the point of linking them, so this sweeps on an
/// interval. The reconcile logic itself lives in
/// <see cref="TestCaseIssuesController.ReconcileAsync"/> and is shared with the
/// manual endpoint, so the automatic and manual paths cannot drift.</para>
///
/// <para>Does nothing at all when GitHub is unconfigured — a server without a
/// token is a normal deployment, not a broken one, and it should not log a
/// failure every interval forever.</para>
/// </summary>
public sealed class TestCaseIssueReconciler(
    IServiceScopeFactory scopeFactory,
    IGitHubIssues github,
    IConfiguration config,
    ILogger<TestCaseIssueReconciler> log) : BackgroundService
{
    /// <summary>How often to sweep. Issue state changes are not urgent — a QA
    /// pass is a human-scale activity — and each sweep costs one API call per
    /// linked case against a rate limit shared with everything else using the
    /// token, so this is deliberately slow. Override with
    /// <c>GitHub:ReconcileIntervalMinutes</c>; 0 or less disables the sweep
    /// while leaving the manual endpoint working.</summary>
    private const int DefaultIntervalMinutes = 15;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!github.IsConfigured)
        {
            log.LogInformation(
                "[testcase-issue] GitHub not configured; the reconciler is idle");
            return;
        }

        var minutes = config.GetValue<int?>("GitHub:ReconcileIntervalMinutes")
                      ?? DefaultIntervalMinutes;
        if (minutes <= 0)
        {
            log.LogInformation(
                "[testcase-issue] reconcile interval is {Minutes}; sweeps disabled", minutes);
            return;
        }

        log.LogInformation(
            "[testcase-issue] reconciling against {Repo} every {Minutes} minute(s)",
            github.Repository, minutes);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
                var changes = await TestCaseIssuesController.ReconcileAsync(
                    db, github, log, stoppingToken);
                if (changes.Count > 0)
                    log.LogInformation("[testcase-issue] sweep updated {Count} case(s)", changes.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad sweep must not take the loop down — GitHub being
                // unreachable for a minute is not a reason to stop
                // reconciling for the lifetime of the process.
                log.LogWarning(ex, "[testcase-issue] reconcile sweep failed; retrying next interval");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
