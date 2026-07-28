using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Binding;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Subscriptions;

/// <summary>
/// The creator-subscription mutation pair the 2023-03-21 client hits from a
/// profile card's Subscribe button. Both live at the bare two-segment route
/// <c>subscription/{accountId}</c> on the Clubs host (enum value 13); the
/// route literal is <c>"subscription/{0}"</c> formatted with a boxed
/// <c>System.Int32</c> account id at
/// <c>RecNet.Runtime/IKMMOCKDKAF.txt:6551 (POST, verb rdx=2)</c> and
/// <c>:6862 (DELETE, verb rdx=4)</c>. Callers are the account-model wrappers
/// <c>OBEEEEAMLJI.BDJCLAGIHBN/LPFOPPHGDHG(Int32 accountId, …)</c>
/// (<c>Assembly-CSharp/OBEEEEAMLJI.txt:644,793</c>) behind
/// <c>AGUI/StackedUI/SubscribeConditionalButton</c>, so the path segment is
/// always a player account id — never a club id.
///
/// The GET side of this surface already exists but lives with the club reads
/// (<c>ClubsController</c>: <c>subscription/details/{id}</c>,
/// <c>subscription/subscriberCount/{id}</c>, <c>subscription/mine/member</c>)
/// and <c>PlayerSubscriptionsController</c>
/// (<c>subscription/top/creators/today</c>). Only the two writes were missing,
/// which made every subscribe/unsubscribe 404 and surface the client's
/// <c>"Failed to subscribe to {0}"</c> / <c>"Failed to unsubscribe from {0}"</c>
/// toast (format literals at <c>IKMMOCKDKAF.txt:6663</c> and the DELETE's
/// matching continuation).
///
/// Neither response body is parsed: the client's continuation is a bare
/// <c>Func&lt;IPCJLCNIBEG&lt;Int64&gt;, …&gt;</c> whose only use is the
/// success/failure branch, after which it invalidates its subscription cache
/// and re-reads <c>subscription/mine/member</c> + <c>subscription/details</c>.
/// Any 2xx is therefore sufficient — but the reads it follows up with must
/// reflect the write, which is why these handlers touch BOTH subscription
/// tables (see <see cref="Subscribe"/>).
/// </summary>
[ApiController]
[Authorize]
public class SubscriptionsController(
    DorkNetDbContext db,
    ClubService clubs,
    ILogger<SubscriptionsController> logger) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>
    /// Body of the subscribe POST. The client appends exactly one
    /// form-urlencoded field via <c>BNDIAONDFFF.AFGEDDANEKP("roomId", …)</c>
    /// (<c>IKMMOCKDKAF.txt:6726 — Move rdx, "roomId"</c>), boxed from an
    /// <c>ObscuredLong</c> holding the room the player is standing in. The
    /// preceding compare against <c>-1</c> (<c>:6713</c>,
    /// <c>Compare rax, 18446744073709551615</c> → jump past the append) means
    /// the field is OMITTED entirely when the player is not in a room, so it
    /// must be optional. Unsubscribe sends no body at all.
    /// </summary>
    public sealed class SubscribeRequest
    {
        public long RoomId { get; set; } = -1;
    }

    /// <summary>
    /// POST <c>/subscription/{accountId}</c> — subscribe to a creator.
    ///
    /// Writes two rows, because the 2023 client's post-subscribe refresh reads
    /// two differently-sourced endpoints and both have to agree:
    ///   * <see cref="DorkNetDbContext.Subscriptions"/> (player→player) is what
    ///     <c>subscription/details/{id}</c>, <c>subscription/subscriberCount/{id}</c>,
    ///     <c>subscription/top/creators/today</c> and the PlayerState reputation
    ///     aggregate all count. This is the canonical row.
    ///   * <see cref="DorkNetDbContext.ClubSubscriptions"/> is what
    ///     <c>subscription/mine/member</c> and
    ///     <c>announcements/v2/subscription/mine/unread</c> read. A creator's
    ///     "creator club" is the club they own (same resolution
    ///     <c>subscription/details</c> uses for its <c>ClubId</c> field — oldest
    ///     id wins), so when the target owns one we mirror the subscription
    ///     there too. Without the mirror the subscribe succeeds but the
    ///     creator never appears in the client's own subscribed list and the
    ///     button flips straight back to "Subscribe" on the next cache refresh.
    /// Creators with no club get only the canonical row, which is all their
    /// profile card reads.
    ///
    /// Idempotent: re-subscribing is a 200 no-op rather than a unique-index
    /// violation (both tables carry a unique pair index).
    /// </summary>
    [HttpPost("/subscription/{accountId:long}")]
    public async Task<IActionResult> Subscribe(
        long accountId,
        [ModelBinder(typeof(FormOrJsonModelBinder))] SubscribeRequest req)
    {
        var me = Me;
        if (accountId == me) return BadRequest(new { Error = "cannot_subscribe_to_self" });

        var targetExists = await db.Players.AnyAsync(p => p.Id == accountId);
        if (!targetExists) return NotFound(new { Error = "account_not_found" });

        var changed = false;

        var existing = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.SubscriberPlayerId == me && s.TargetPlayerId == accountId);
        if (existing is null)
        {
            db.Subscriptions.Add(new SubscriptionEntity
            {
                SubscriberPlayerId = me,
                TargetPlayerId = accountId,
            });
            changed = true;
        }

        var creatorClubId = await CreatorClubIdAsync(accountId);
        if (creatorClubId != 0)
        {
            var existingClub = await db.ClubSubscriptions.FirstOrDefaultAsync(
                s => s.PlayerId == me && s.ClubId == creatorClubId);
            if (existingClub is null)
            {
                db.ClubSubscriptions.Add(new ClubSubscriptionEntity
                {
                    ClubId = creatorClubId,
                    PlayerId = me,
                });
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync();

        // roomId is pure attribution — "which room was this creator discovered
        // in". There is no column for it on SubscriptionEntity and this pass
        // adds no schema, so it is recorded in the log rather than silently
        // dropped; the subscription itself is fully persisted above.
        if (req.RoomId > 0)
        {
            logger.LogInformation(
                "Player {SubscriberId} subscribed to creator {AccountId} from room {RoomId}.",
                me, accountId, req.RoomId);
        }

        return Ok();
    }

    /// <summary>
    /// DELETE <c>/subscription/{accountId}</c> — unsubscribe. Same route
    /// literal, verb register <c>rdx=4</c> at
    /// <c>IKMMOCKDKAF.txt:6864</c>; the request carries no parameters
    /// (<c>AFGEDDANEKP</c> is never called on the builder before the send),
    /// so there is nothing to bind.
    ///
    /// Removes both rows written by <see cref="Subscribe"/> so the canonical
    /// counts and the <c>subscription/mine/member</c> list drop the creator
    /// together. Unsubscribing when not subscribed is a 200 no-op — the client
    /// treats any non-2xx as the "Failed to unsubscribe from {0}" toast, and a
    /// double-tap of an already-removed subscription is not a failure the
    /// player should see.
    /// </summary>
    [HttpDelete("/subscription/{accountId:long}")]
    public async Task<IActionResult> Unsubscribe(long accountId)
    {
        var me = Me;

        var row = await db.Subscriptions.FirstOrDefaultAsync(
            s => s.SubscriberPlayerId == me && s.TargetPlayerId == accountId);
        if (row is not null) db.Subscriptions.Remove(row);

        var creatorClubId = await CreatorClubIdAsync(accountId);
        if (creatorClubId != 0)
        {
            var clubRow = await db.ClubSubscriptions.FirstOrDefaultAsync(
                s => s.PlayerId == me && s.ClubId == creatorClubId);
            if (clubRow is not null) db.ClubSubscriptions.Remove(clubRow);
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// The club id <c>subscription/details/{accountId}</c> reports as the
    /// creator's <c>ClubId</c>: the oldest live club the account owns, or 0
    /// when they own none. Kept byte-identical to that handler's resolution so
    /// subscribing writes the row the details card points the player at.
    /// </summary>
    private async Task<long> CreatorClubIdAsync(long accountId)
    {
        var created = await clubs.CreatedByAsync(accountId);
        return created.Count > 0 ? created.Min(c => c.Id) : 0L;
    }
}
