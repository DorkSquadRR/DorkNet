namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Single-row table holding the dorm community board state. Replaces
/// <c>data/community_board.json</c> on disk + in-memory cache so admin
/// edits propagate across replicas without a file-watcher invalidation
/// dance.
///
/// Only one row exists, with <see cref="Id"/> = 1. Reading is a single
/// SELECT (cheap; the board is one document, not paginated). Writing
/// is a single UPDATE — no synchronization needed because there's only
/// ever one row to contend on, and admin edits are rare.
/// </summary>
public class CommunityBoardEntity
{
    /// <summary>Always 1. We model it as an int PK rather than a
    /// composite or singleton-table pattern because EF Core needs a key
    /// to track changes; pinning to id=1 keeps the upsert pattern
    /// simple.</summary>
    public int Id { get; set; } = 1;

    /// <summary>JSON-serialised <c>CommunityBoardState</c>. Stored as
    /// text rather than jsonb because we never query inside it — it's
    /// loaded as a single blob and round-tripped to the client.</summary>
    public string Json { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
