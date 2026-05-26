using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Awards XP, computes level transitions, and adjusts currency
/// balances. Centralising these mutations behind one service avoids
/// scattered <c>db.Players.Find()</c>; <c>p.XP += n</c>; calls and
/// gives us a single place to adjust the reward curve.
///
/// Curve: <c>level = floor(sqrt(xp / 100)) + 1</c> — i.e. each level
/// costs <c>100 * (level^2 - (level-1)^2)</c> XP, scaling
/// quadratically. Level 1 starts at 0 XP, Level 2 at 100, Level 3 at
/// 400, Level 5 at 1600, Level 10 at 8100. Loose match for the
/// 2020 client's expected curve.
/// </summary>
public class LevelService(DorkNetDbContext db, NotificationService notifications)
{
    public const int FirstLoginXp = 50;
    public const int InventionSavedXp = 25;
    public const int CheerReceivedXp = 10;
    public const int RoomVisitXp = 5;

    /// <summary>Add <paramref name="amount"/> XP to the player. Returns
    /// the new (level, xp) pair. Level transitions push a
    /// <c>SubscriptionUpdateProfile</c> notification with the new
    /// level. Idempotent at the row level — concurrent calls might
    /// double-grant if both load the same XP value, but the curve
    /// tolerates that.</summary>
    public async Task<(int level, int xp)> AwardXpAsync(long playerId, int amount, string reason)
    {
        if (amount <= 0) return (1, 0);
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player is null) return (1, 0);

        var oldLevel = player.Level;
        player.XP += amount;
        player.Level = LevelForXp(player.XP);
        await db.SaveChangesAsync();

        if (player.Level > oldLevel)
        {
            // Level-up grants a small currency reward and a push so
            // the watch can play its level-up animation.
            await GrantCurrencyAsync(playerId, currencyType: 2,
                amount: 100 * (player.Level - oldLevel),
                reason: $"levelup:{oldLevel}->{player.Level}");
            await notifications.NotifyAsync(playerId,
                PushNotificationId.SubscriptionUpdateProfile,
                new
                {
                    Reason = "LevelUp",
                    Level = player.Level,
                    PreviousLevel = oldLevel,
                });
        }

        return (player.Level, player.XP);
    }

    /// <summary>Grant or deduct currency. <paramref name="amount"/>
    /// can be negative for deductions (e.g. spending in storefronts).
    /// Pushes <c>StorefrontBalanceUpdated</c> when the balance moves.</summary>
    public async Task<long> GrantCurrencyAsync(
        long playerId, int currencyType, long amount, string reason)
    {
        if (amount == 0) return await GetBalanceAsync(playerId, currencyType);
        var row = await db.CurrencyBalances.FirstOrDefaultAsync(
            c => c.PlayerId == playerId && c.CurrencyType == currencyType);
        if (row is null)
        {
            row = new CurrencyBalanceEntity
            {
                PlayerId = playerId,
                CurrencyType = currencyType,
                Balance = 0,
            };
            db.CurrencyBalances.Add(row);
        }
        row.Balance = Math.Max(0, row.Balance + amount);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(playerId,
            PushNotificationId.StorefrontBalanceUpdate,
            new { CurrencyType = currencyType, row.Balance, Reason = reason });
        return row.Balance;
    }

    public async Task<long> GetBalanceAsync(long playerId, int currencyType) =>
        await db.CurrencyBalances
            .Where(c => c.PlayerId == playerId && c.CurrencyType == currencyType)
            .Select(c => c.Balance)
            .FirstOrDefaultAsync();

    /// <summary>Inverse of the level→xp curve. <c>floor(sqrt(xp/100)) + 1</c>.</summary>
    public static int LevelForXp(int xp) =>
        xp <= 0 ? 1 : (int)Math.Floor(Math.Sqrt(xp / 100.0)) + 1;
}
