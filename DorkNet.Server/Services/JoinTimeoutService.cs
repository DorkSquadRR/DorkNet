using System.Collections.Concurrent;
using DorkNet.Server.Controllers.Match;

namespace DorkNet.Server.Services;

/// <summary>
/// Tracks pending /goto joins so a failed Photon join cannot leave the
/// 2020 watch stuck forever on the "Joining room" overlay.
/// </summary>
public sealed class JoinTimeoutService(
    PlayerPresenceService presence,
    NotificationService notifications,
    ILogger<JoinTimeoutService> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<long, PendingJoin> _pending = new();

    public void MarkPending(long playerId, RoomInstanceDto? room, bool deferPresenceCommit = false)
    {
        if (room is null) return;

        if (IsDorm(room) && !deferPresenceCommit)
        {
            ClearForPlayer(playerId, "dorm");
            return;
        }

        ClearForPlayer(playerId, "replaced");

        var pending = new PendingJoin(
            room,
            deferPresenceCommit,
            new CancellationTokenSource());

        _pending[playerId] = pending;
        logger.LogInformation(
            "[join-watchdog] pending player={PlayerId} room={RoomId}/{RoomName} instance={InstanceId} timeout={TimeoutSeconds}s",
            playerId, room.RoomId, room.Name, room.RoomInstanceId, Timeout.TotalSeconds);

        _ = WatchAsync(playerId, pending);
    }

    public (RoomInstanceDto TargetRoom, bool DeferPresenceCommit)? GetPending(long playerId)
    {
        if (!_pending.TryGetValue(playerId, out var pending)) return null;
        return (pending.TargetRoom, pending.DeferPresenceCommit);
    }

    public (RoomInstanceDto TargetRoom, bool DeferPresenceCommit)? MarkCompleted(long playerId, long instanceId, string outcome)
    {
        if (!_pending.TryGetValue(playerId, out var pending) || pending.InstanceId != instanceId)
            return null;

        if (!RemovePending(playerId, pending))
            return null;

        pending.Cancellation.Cancel();
        logger.LogInformation(
            "[join-watchdog] completed player={PlayerId} instance={InstanceId} outcome={Outcome}",
            playerId, instanceId, outcome);
        return (pending.TargetRoom, pending.DeferPresenceCommit);
    }

    public Task MarkFailedAsync(long playerId, long instanceId, string outcome)
    {
        var completed = MarkCompleted(playerId, instanceId, outcome);

        var current = presence.GetRoom(playerId);
        var failedRoom = completed.HasValue ? completed.Value.TargetRoom : current;
        if (failedRoom is null || (!completed.HasValue && current?.RoomInstanceId != instanceId))
        {
            logger.LogInformation(
                "[join-watchdog] failure ignored player={PlayerId} instance={InstanceId} current={CurrentInstance}",
                playerId, instanceId, current?.RoomInstanceId);
            return Task.CompletedTask;
        }

        presence.Clear(playerId);
        logger.LogWarning(
            "[join-watchdog] cleared failed join player={PlayerId} room={RoomId}/{RoomName} instance={InstanceId} outcome={Outcome}",
            playerId, failedRoom.RoomId, failedRoom.Name, instanceId, outcome);
        return Task.CompletedTask;
    }

    private async Task WatchAsync(long playerId, PendingJoin pending)
    {
        try
        {
            await Task.Delay(Timeout, pending.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[join-watchdog] delay failed player={PlayerId}", playerId);
            return;
        }

        if (!RemovePending(playerId, pending))
            return;

        var current = presence.GetRoom(playerId);
        if (!pending.DeferPresenceCommit && (current is null || current.RoomInstanceId != pending.InstanceId))
        {
            logger.LogInformation(
                "[join-watchdog] timeout skipped player={PlayerId} pending={PendingInstance} current={CurrentInstance}",
                playerId, pending.InstanceId, current?.RoomInstanceId);
            return;
        }

        presence.Clear(playerId);
        await notifications.KickPlayerAsync(playerId, "Room join timed out. Returning to dorm.");
        logger.LogWarning(
            "[join-watchdog] timed out player={PlayerId} room={RoomId}/{RoomName} instance={InstanceId}",
            playerId, pending.TargetRoom.RoomId, pending.TargetRoom.Name, pending.InstanceId);
    }

    private void ClearForPlayer(long playerId, string reason)
    {
        while (_pending.TryGetValue(playerId, out var pending))
        {
            if (!RemovePending(playerId, pending))
                continue;

            pending.Cancellation.Cancel();
            logger.LogDebug(
                "[join-watchdog] cleared player={PlayerId} instance={InstanceId} reason={Reason}",
                playerId, pending.InstanceId, reason);
            return;
        }
    }

    private bool RemovePending(long playerId, PendingJoin pending) =>
        ((ICollection<KeyValuePair<long, PendingJoin>>)_pending)
            .Remove(new KeyValuePair<long, PendingJoin>(playerId, pending));

    private static bool IsDorm(RoomInstanceDto room) =>
        room.RoomId == 1 ||
        string.Equals(room.Name, "DormRoom", StringComparison.OrdinalIgnoreCase) ||
        (room.PhotonRoomId?.StartsWith("^dormroom_", StringComparison.OrdinalIgnoreCase) ?? false);

    private sealed record PendingJoin(
        RoomInstanceDto TargetRoom,
        bool DeferPresenceCommit,
        CancellationTokenSource Cancellation)
    {
        public long InstanceId => TargetRoom.RoomInstanceId;
    }
}
