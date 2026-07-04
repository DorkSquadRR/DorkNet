using DorkNet.Models.GameSessions;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Cross-replica matchmaking session registry. Each session is a Photon
/// match with a player count; <see cref="JoinOrCreateAsync"/> finds an
/// existing non-full session in the requested room+region or creates
/// a new one.
///
/// Pre-PR-3 used a process-local <c>ConcurrentDictionary</c> + atomic
/// counter. With horizontal scale that was unsafe two ways:
/// <list type="bullet">
/// <item>Each replica's counter started at 1, so two replicas would
///     hand out colliding session ids — clients on different replicas
///     could end up with the same SessionId pointing at different
///     Photon rooms.</item>
/// <item>"Find a non-full session" only saw replica-local sessions, so
///     two players matchmaking via different replicas got their own
///     fresh session each and were isolated.</item>
/// </list>
///
/// Both are fixed by moving to Postgres: bigserial allocates from a
/// shared sequence; the SELECT sees every replica's sessions.
/// Service is now <b>scoped</b> because it holds a DbContext.
/// </summary>
public class GameSessionService(DorkNetDbContext db)
{
    // Dorm room is always available
    private const string DormRoomId = "00000000-0000-0000-0000-000000000001";
    private const string DormActivityId = "76d98498-60a1-430c-ab76-b54a29b7a163";

    public async Task<GameSession> JoinOrCreateAsync(string? roomId, string? activityId, string region)
    {
        var effectiveRoom = roomId ?? DormRoomId;

        // Find an existing non-full session for this room+region. ORDER BY
        // PlayerCount DESC + LIMIT 1 packs joiners into the fullest matching
        // session first, so we don't fragment players across half-empty
        // rooms when the catalog is light.
        var match = await db.GameSessions
            .Where(s => s.RoomId == effectiveRoom
                     && s.Region == region
                     && s.PlayerCount < s.MaxCapacity)
            .OrderByDescending(s => s.PlayerCount)
            .FirstOrDefaultAsync();

        if (match is not null)
        {
            match.PlayerCount += 1;
            await db.SaveChangesAsync();
            return ToDto(match);
        }

        var session = new GameSessionEntity
        {
            RoomId = effectiveRoom,
            ActivityLevelId = activityId ?? DormActivityId,
            Region = region,
            // PhotonRoomName needs the eventual server-allocated Id baked
            // in, so we save once with a placeholder, then patch and save
            // again. Two SaveChangesAsync calls is cheap at our QPS.
            PhotonRoomName = "pending",
            MaxCapacity = 8,
            PlayerCount = 1,
            CreatedAt = DateTime.UtcNow,
        };
        db.GameSessions.Add(session);
        await db.SaveChangesAsync();
        session.PhotonRoomName = $"recroom_{session.Id}_{Guid.NewGuid():N}";
        await db.SaveChangesAsync();
        return ToDto(session);
    }

    public async Task<GameSession?> GetByIdAsync(long id)
    {
        var s = await db.GameSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        return s is null ? null : ToDto(s);
    }

    public async Task PlayerLeftAsync(long sessionId)
    {
        var s = await db.GameSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (s is null) return;
        s.PlayerCount = Math.Max(0, s.PlayerCount - 1);
        if (s.PlayerCount == 0)
        {
            db.GameSessions.Remove(s);
        }
        await db.SaveChangesAsync();
    }

    private static GameSession ToDto(GameSessionEntity s) => new()
    {
        GameSessionId = s.Id,
        RoomId = s.RoomId,
        ActivityLevelId = s.ActivityLevelId,
        RegionId = s.Region,
        PhotonRoomName = s.PhotonRoomName,
        Private = false,
        MaxCapacity = s.MaxCapacity,
        PlayerCount = s.PlayerCount,
        IsFull = s.PlayerCount >= s.MaxCapacity,
        GameInProgress = s.PlayerCount > 0,
    };
}
