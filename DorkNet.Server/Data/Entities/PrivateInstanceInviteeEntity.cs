namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Join row marking that <see cref="PlayerId"/> is on the invite list
/// for <see cref="PrivateInstanceId"/>. Created when the owner sends an
/// invite via <c>POST /invite</c> (handled in <c>MessagesController</c>);
/// checked on <c>POST /goto/instance/{id}</c> by
/// <c>PrivateInstanceService.CanJoin</c>.
///
/// Composite PK on (PrivateInstanceId, PlayerId) — naturally unique;
/// repeat-invite calls become idempotent no-ops.
/// </summary>
public class PrivateInstanceInviteeEntity
{
    public long PrivateInstanceId { get; set; }
    public long PlayerId { get; set; }
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The MessageEntity.Id of the latest invite for this
    /// (instance, player). Lets <c>GoToInvite</c> resolve the
    /// roomInstanceId even when the watch's accept flow has already
    /// raced through <c>POST /api/messages/v3/delete</c> and removed
    /// the underlying message row. Without this column the server
    /// returned ErrorCode 40 ("invite expired") for any successful
    /// accept where DELETE landed first — which was the common case
    /// because the 2020 watch fires DELETE and POST /goto/invite/{id}
    /// in parallel.</summary>
    public long? LatestInviteMessageId { get; set; }
}
