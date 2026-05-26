namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Asymmetric "follow" relationship: <see cref="SubscriberPlayerId"/>
/// follows <see cref="TargetPlayerId"/> for activity updates.
/// Distinct from <see cref="RelationshipEntity"/>'s mutual friend
/// model — subscriptions are one-way and don't require acceptance.
/// </summary>
public class SubscriptionEntity
{
    public long Id { get; set; }
    public long SubscriberPlayerId { get; set; }
    public long TargetPlayerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
