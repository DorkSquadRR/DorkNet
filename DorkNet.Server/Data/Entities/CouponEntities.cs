using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Admin-issued promo coupon. Players redeem by code via
/// <c>POST /api/coupons/v1/redeem/{code}</c>. Each redemption
/// inserts a <see cref="CouponRedemptionEntity"/> row (one per
/// (coupon, player) pair so we can enforce one-redeem-per-player).
/// </summary>
public class CouponEntity
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Description { get; set; } = string.Empty;

    /// <summary>RewardType: 0=Currency, 1=ItemSlug, 2=Subscription.</summary>
    public int RewardType { get; set; }

    /// <summary>Currency type if RewardType=0; otherwise unused.</summary>
    public int RewardCurrencyType { get; set; }

    public long RewardAmount { get; set; }

    /// <summary>Item slug if RewardType=1.</summary>
    [MaxLength(64)]
    public string RewardItemSlug { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int MaxRedemptions { get; set; } = 0;
    public int RedemptionCount { get; set; } = 0;

    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CouponRedemptionEntity
{
    public long Id { get; set; }
    public long CouponId { get; set; }
    public long PlayerId { get; set; }
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
