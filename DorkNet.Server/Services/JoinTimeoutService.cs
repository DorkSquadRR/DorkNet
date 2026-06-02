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

    public void MarkPending(long playerId, RoomInstanceDto? room)
    {
        if (room is null) return;

        if (IsDorm(room))
        {
            ClearForPlayer(playerId, "dorm");
            return;
        }

        ClearForPlayer(playerId, "replaced");

        var pending = new PendingJoin(
            room.RoomInstanceId,
            room.RoomId,
            room.Name ?? string.Empty,
            new CancellationTokenSource());

        _pending[playerId] = pending;
        logger.LogInformation(
            "[join-watchdog] pending player={PlayerId} room={RoomId}/{RoomName} instance={InstanceId} timeout={TimeoutSeconds}s",
            playerId, room.RoomId, room.Name, room.RoomInstanceId, Timeout.TotalSeconds);

        _ = WatchAsync(playerId, pending);
    }

    public void MarkCompleted(long playerId, long instanceId, string outcome)
    {
        if (!_pending.TryGetValue(playerId, out var pending) || pending.InstanceId != instanceId)
            return;

        if (!RemovePending(playerId, pending))
            return;

        pending.Cancellation.Cancel();
        logger.LogInformation(
            "[join-watchdog] completed player={PlayerId} instance={InstanceId} outcome={Outcome}",
            playerId, instanceId, outcome);
    }

    public async Task MarkFailedAsync(long playerId, long instanceId, string outcome)
    {
        MarkCompleted(playerId, instanceId, outcome);

        var current = presence.GetRoom(playerId);
        if (current is null || current.RoomInstanceId != instanceId)
        {
            logger.LogInformation(
                "[join-watchdog] failure ignored player={PlayerId} instance={InstanceId} current={CurrentInstance}",
                playerId, instanceId, current?.RoomInstanceId);
            return;
        }

        presence.Clear(playerId);
        await notifications.KickPlayerAsync(playerId, "Room join failed. Returning to dorm.");
        logger.LogWarning(
            "[join-watchdog] kicked failed join player={PlayerId} room={RoomId}/{RoomName} instance={InstanceId} outcome={Outcome}",
            playerId, current.RoomId, current.Name, instanceId, outcome);
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
        if (current is null || current.RoomInstanceId != pending.InstanceId)
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
            playerId, pending.RoomId, pending.RoomName, pending.InstanceId);
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
        long InstanceId,
        long RoomId,
        string RoomName,
        CancellationTokenSource Cancellation);
}
