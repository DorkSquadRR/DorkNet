namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One row per (PlaylistId, PlayerId) tracking the player's cheer
/// + favorite flags on a playlist. Drives <c>/playlists/{id}/interactionby/me</c>
/// (read), the four interactionby/me mutation endpoints (cheer /
/// uncheer / favorite / unfavorite toggles), and the
/// <c>playlists/cheeredby/me</c> + <c>playlists/favoritedby/me</c>
/// lists.
///
/// Counter columns on <see cref="PlaylistEntity"/> (CheerCount /
/// FavoriteCount) are kept in sync by <see cref="DorkNet.Server.Services.PlaylistService"/>
/// on each toggle so the hot ranking + tile badges read from the
/// row without an aggregate join.
/// </summary>
public class PlaylistInteractionEntity
{
    public long Id { get; set; }
    public long PlaylistId { get; set; }
    public long PlayerId { get; set; }
    public bool Cheered { get; set; } = false;
    public bool Favorited { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
