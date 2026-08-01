using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.ProgressionEvents;

[ApiController]
public class ProgressionEventsController(
    DorkNetDbContext db,
    ServerSettingsService serverSettings,
    IConfiguration config) : ControllerBase
{
    /// <summary>CurrencyType enum value the rest of the server uses for
    /// RecCenter tokens (mirrors <c>ProgressionController.RecCenterTokens</c>).</summary>
    private const int RecCenterTokens = 2;

    [HttpGet("api/progressionEvents")]
    [Authorize]
    public async Task<IActionResult> Progress()
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me && o.Key.StartsWith("progressionEvent:"))
            .OrderByDescending(o => o.ClearedAt)
            .Take(100)
            .Select(o => new
            {
                EventKey = o.Key,
                o.IsCompleted,
                o.ClearedAt,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("api/progressionEvents/event/{eventId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Event(long eventId)
    {
        var activeId = CurrentProgressionEventId();
        if (eventId != activeId)
            return NotFound();

        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        var (start, end) = CurrentWeekWindow();
        // The boost fields must agree with api/progressionEvents/{id}/xpboosts:
        // ProgressionEventsPurchasableXpBoostModel.Set() takes the guid the
        // event advertises here and looks it up in the xpboosts list, and
        // ProgressionEventsModel.GetPurchasableXpBoostId() gates the whole
        // boost sheet on PurchasableXpBoostId being non-null. Deriving both
        // from one catalogue keeps them from drifting.
        var boosts = BoostCatalogue(activeId);
        var headline = boosts.Count > 0 ? boosts[0] : null;
        return Ok(new
        {
            ProgressionEventId = activeId,
            Name = "Weekly Progression",
            Rewards = BuildRewards(weekly.Reward, activeId),
            KeepsakeRoomLists = Array.Empty<object>(),
            StartTime = start,
            EndTime = end,
            CollectionEndTime = end,
            UsesBoost = headline is not null,
            BoostDailyGameplayMinutesLimit = 0,
            // ProgressionEventDTO.BoostXpMultiplier is a float at +0x50
            // (dump.cs:1198957) — emit a real number, not an integer token.
            BoostXpMultiplier = (double)(headline?.XpMultiplier ?? 1),
            PurchasableXpBoostId = headline?.Id,
            ActiveExperiment = string.Empty,
            ChallengesIconImageName = string.Empty,
            RewardsPipImageName = string.Empty,
            EventInfoImageName = string.Empty,
        });
    }

    [HttpGet("api/progressionEvents/active")]
    [AllowAnonymous]
    public IActionResult Active() => Ok(CurrentProgressionEventId());

    /// <summary>
    /// The current player's progress record for a given progression event.
    /// The 2023 client fetches this during InitialRoomLoad
    /// (<c>api/progressionEvents/record/{id}</c>, where <c>{id}</c> is the
    /// <c>yyyyMMdd</c> event id). A 404 here faults the room-load coroutine
    /// with a NullReferenceException and traps the client in a dorm
    /// matchmaking loop, so we always return a non-null record.
    /// </summary>
    [HttpGet("api/progressionEvents/record/{progressionEventId:long}")]
    [Authorize]
    public async Task<IActionResult> Record(long progressionEventId)
    {
        var me = this.RequireCurrentPlayerId();

        // Count the reward-claim sentinels this player owns FOR THIS EVENT
        // (written by Collect below). The previous query counted every
        // `progressionEvent:` row, which also matches the xpBoost purchase
        // sentinels StorefrontsBuyController writes — a boost purchase
        // silently advanced the claimed-reward pip.
        var claimPrefix = RewardClaimPrefix(progressionEventId);
        var collected = await db.ObjectiveProgress
            .CountAsync(o => o.PlayerId == me
                && o.IsCompleted
                && o.Key.StartsWith(claimPrefix));

        return Ok(new
        {
            ProgressionEventId = progressionEventId,
            Xp = 0,
            ClaimedRewardIndex = collected > 0 ? collected - 1 : -1,
            PurchasedXpBoostCount = 0,
            DailyBoostGameplayMinutes = 0,
            XpBoostExpiresAt = (DateTime?)null,
        });
    }

    // ── Reward collection ────────────────────────────────────────────────

    /// <summary>
    /// POST <c>api/progressionEvents/collect/{eventId}/{rewardIndex}</c> —
    /// claim one reward chest off the progression track.
    ///
    /// Binary evidence: <c>RecNet.Runtime/CDBFONFHJDO.txt:1208</c>
    /// (<c>GKANPKEGBGE(Int64 eventId, Int32 rewardIndex)</c>) formats
    /// <c>"{0}/collect/{1}/{2}"</c> against <c>"api/progressionEvents"</c>
    /// (ISIL 041/043) and moves <c>2</c> = <c>HTTPMethods.Post</c> into the
    /// verb argument at ISIL 055. No request body is passed — the two route
    /// segments are the whole request.
    ///
    /// The return type is <c>FGLDKEJLAKB&lt;FFFIMAGLKEG/FHMABOHAEED&gt;</c>,
    /// and <c>FHMABOHAEED</c> is the client's GiftPackage DTO — the very
    /// same object <c>GiftManager.RunConsumeGift</c> /
    /// <c>GiftManager.DequeueGift</c> take
    /// (<c>Assembly-CSharp/GiftManager.txt:3226,3395</c>), whose wire keys are
    /// Id / FromPlayerId / ConsumableItemDesc / AvatarItemType /
    /// AvatarItemDesc / EquipmentPrefabName / EquipmentModificationGuid /
    /// CurrencyType / Currency / Xp / GiftContext / GiftRarity / Message /
    /// Platform / PlatformsToSpawnOn / BalanceType
    /// (<c>RecNet.Runtime/FKANCKLCCDI.txt:1051-1390</c>).
    ///
    /// That means the client will POST the returned <c>Id</c> back to
    /// <c>api/avatar/v2/gifts/consume</c>, so this MUST persist a real
    /// <see cref="GiftPackageEntity"/> rather than synthesise a wire blob —
    /// an unpersisted Id 404s on consume and the player loses the reward.
    /// The payload itself comes from the admin-configured weekly reward
    /// (<see cref="WeeklyChallengeReward"/>), i.e. exactly what
    /// <see cref="Event"/> advertises on the track, so the chest hands over
    /// what the UI promised. XP / tokens / level ride on the package and are
    /// granted by the existing consume path, not here — granting in both
    /// places would double-pay.
    ///
    /// Idempotent: the claim sentinel is an <see cref="ObjectiveProgressEntity"/>
    /// whose Key embeds the created gift id, so a retried or duplicated POST
    /// returns the original package instead of minting a second one.
    /// No <c>GiftPackageReceived</c> push is sent (unlike
    /// <c>avatar/v2/gifts/generate</c>): the RRUI reward flow already spawns
    /// the box from this response, and a push would spawn a second one.
    /// </summary>
    [HttpPost("api/progressionEvents/collect/{eventId:long}/{rewardIndex:int}")]
    [Authorize]
    public async Task<IActionResult> Collect(long eventId, int rewardIndex)
    {
        var me = this.RequireCurrentPlayerId();

        // Only the live event's track is claimable — the client only ever
        // renders the active event, and letting an arbitrary id through
        // would let a replayed request re-roll last week's chest.
        var activeId = CurrentProgressionEventId();
        if (eventId != activeId) return NotFound();

        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        var rewards = BuildRewards(weekly.Reward, activeId);
        if (rewardIndex < 0 || rewardIndex >= rewards.Count) return NotFound();

        // Already claimed? The sentinel key carries the gift id so we can
        // hand back the same package (the client may retry after a dropped
        // response, and it needs a consumable Id either way).
        var sentinelPrefix = $"{RewardClaimPrefix(eventId)}{rewardIndex}:gift:";
        var sentinel = await db.ObjectiveProgress
            .FirstOrDefaultAsync(o => o.PlayerId == me && o.Key.StartsWith(sentinelPrefix));
        if (sentinel is not null)
        {
            if (long.TryParse(sentinel.Key[sentinelPrefix.Length..], out var priorId))
            {
                var prior = await db.GiftPackages
                    .FirstOrDefaultAsync(g => g.Id == priorId && g.RecipientPlayerId == me);
                if (prior is not null)
                    return Ok(global::DorkNet.Server.Controllers.API.Avatar.V2.AvatarGiftsController.ToWire(prior));
            }
            // Sentinel exists but its package was purged — drop the stale
            // marker and fall through to re-issue, otherwise the player is
            // permanently locked out of a reward they never received.
            db.ObjectiveProgress.Remove(sentinel);
            await db.SaveChangesAsync();
        }

        var reward = weekly.Reward;
        var gift = new GiftPackageEntity
        {
            RecipientPlayerId = me,
            FromPlayerId = null,
            // The weekly reward config carries a raw avatar desc, which is
            // always an outfit item (AvatarItemType 0); hair dyes are
            // configured through ConsumableItemDesc instead. Leave the type
            // null when there is no avatar payload so the client doesn't
            // render an empty outfit tile.
            AvatarItemType = string.IsNullOrWhiteSpace(reward.AvatarItemDesc) ? (int?)null : 0,
            AvatarItemDescOrHairDyeDesc = reward.AvatarItemDesc,
            ConsumableItemDesc = reward.ConsumableItemDesc,
            EquipmentPrefabName = reward.EquipmentPrefabName,
            EquipmentModificationGuid = reward.EquipmentModificationGuid,
            CurrencyType = reward.Tokens > 0 ? RecCenterTokens : 0,
            Currency = reward.Tokens,
            Xp = reward.Xp,
            Level = reward.Level,
            GiftContext = reward.GiftContext,
            GiftRarity = reward.GiftRarity,
            Message = $"Progression event reward {rewardIndex + 1}",
            Platform = -1,
            PackageVariant = "Standard",
            PackageMaterial = string.Empty,
            IsValid = true,
            SupportsCurrentPlatform = true,
        };
        // The sentinel key needs the generated gift id, so this takes two
        // saves — but they must commit together: a crash between them would
        // leave a gift with no sentinel, and the next Collect would mint the
        // reward a second time.
        await using var tx = await db.Database.BeginTransactionAsync();
        db.GiftPackages.Add(gift);
        await db.SaveChangesAsync();

        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = me,
            Key = $"{sentinelPrefix}{gift.Id}",
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(global::DorkNet.Server.Controllers.API.Avatar.V2.AvatarGiftsController.ToWire(gift));
    }

    // ── Purchasable XP boosts ────────────────────────────────────────────

    /// <summary>
    /// GET <c>api/progressionEvents/{eventId}/xpboosts</c> — the boost
    /// offers the client's purchase sheet renders.
    ///
    /// Binary evidence: <c>RecNet.Runtime/CDBFONFHJDO.txt:1465</c>
    /// (<c>HKMBEFABIBD(Int64 eventId)</c>) formats
    /// <c>"{0}/{1}/xpboosts"</c> against <c>"api/progressionEvents"</c>
    /// (ISIL 026/028) and moves <c>0</c> = <c>HTTPMethods.Get</c> into the
    /// verb argument at ISIL 034. The declared return type is
    /// <c>List&lt;ProgressionEventPurchasableXpBoostDTO&gt;</c>, i.e. a bare
    /// JSON array — no paged container.
    ///
    /// Element keys and their order come from the generated Utf8Json
    /// formatter <c>RecNet.Runtime/OFGIPBHIAGF.txt:605-698</c>:
    /// ProgressionEventPurchasableXpBoostId (Guid), Cost (Int32),
    /// XpMultiplier (Int32 — <c>get_XpMultiplierFromBoost</c> reads the DTO's
    /// +0x24 int field, NOT a float), XpCap (Int32), LookbackDurationTicks
    /// (Int64), CooldownDurationTicks (Int64).
    ///
    /// The boost id must be STABLE across calls: the client caches it from
    /// here, passes it to <c>previewEarnedXp</c>, and posts it as
    /// <c>purchasableXpBoostId</c> to
    /// <c>api/storefronts/v1/buyProgressionEventXpBoost</c> (which keys its
    /// purchase sentinel on it). See <see cref="OfferId"/>.
    /// </summary>
    [HttpGet("api/progressionEvents/{eventId:long}/xpboosts")]
    [AllowAnonymous]
    public IActionResult XpBoosts(long eventId) =>
        Ok(BoostCatalogue(eventId).Select(b => new
        {
            ProgressionEventPurchasableXpBoostId = b.Id,
            b.Cost,
            b.XpMultiplier,
            b.XpCap,
            b.LookbackDurationTicks,
            b.CooldownDurationTicks,
        }));

    /// <summary>
    /// GET <c>api/progressionEvents/{eventId}/xpboosts/{boostId}/previewEarnedXp</c>
    /// — how much XP buying this boost right now would hand the caller.
    ///
    /// Binary evidence: <c>RecNet.Runtime/CDBFONFHJDO.txt:1612</c>
    /// (<c>GBNFGMOGLNG(Int64 eventId, Guid boostId)</c>) formats
    /// <c>"{0}/{1}/xpboosts/{2}/previewEarnedXp"</c> against
    /// <c>"api/progressionEvents"</c> (ISIL 036/038) and moves <c>0</c> =
    /// <c>HTTPMethods.Get</c> into the verb argument at ISIL 049. The return
    /// type is <c>FGLDKEJLAKB&lt;System.Int32&gt;</c> — a BARE integer body,
    /// not an object; the value lands in
    /// <c>ProgressionEventsPurchasableXpBoostModel.get_XpEarnedFromBoost</c>
    /// and is echoed back as the <c>expectedXp</c> form field of
    /// <c>buyProgressionEventXpBoost</c>, which grants exactly that much.
    ///
    /// Cooldown is real state: <c>buyProgressionEventXpBoost</c> stamps
    /// <c>progressionEvent:{eventId}:xpBoost:{guid:N}</c> in ObjectiveProgress
    /// on every purchase, so a boost still inside its
    /// <c>CooldownDurationTicks</c> window previews as 0 and the client's
    /// <c>get_XPBoostIsAvailableForPurchase</c> keeps the button disabled.
    ///
    /// Fidelity note: live Rec Room multiplied the XP the player actually
    /// earned during <c>LookbackDurationTicks</c>. DorkNet has no timestamped
    /// XP ledger (<see cref="LevelService.AwardXpAsync"/> mutates
    /// <c>PlayerEntity.XP</c> in place), so a boost here pays its configured
    /// <c>XpCap</c> — the cap is the payout, and <c>XpMultiplier</c> is the
    /// figure the sheet renders as "Nx".
    /// </summary>
    [HttpGet("api/progressionEvents/{eventId:long}/xpboosts/{boostId:guid}/previewEarnedXp")]
    [Authorize]
    public async Task<IActionResult> PreviewEarnedXp(long eventId, Guid boostId)
    {
        var me = this.RequireCurrentPlayerId();

        var offer = BoostCatalogue(eventId).FirstOrDefault(b => b.Id == boostId);
        if (offer is null) return Ok(0);

        // Same key StorefrontsBuyController.BuyProgressionEventXpBoost writes.
        var purchaseKey = $"progressionEvent:{eventId}:xpBoost:{boostId:N}";
        var lastPurchase = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me && o.Key == purchaseKey)
            .Select(o => o.ClearedAt)
            .FirstOrDefaultAsync();

        if (offer.CooldownDurationTicks > 0
            && lastPurchase is DateTime at
            && DateTime.UtcNow - at < TimeSpan.FromTicks(offer.CooldownDurationTicks))
        {
            return Ok(0);
        }

        return Ok(offer.XpCap);
    }

    // ── Shared shape helpers ─────────────────────────────────────────────

    /// <summary>One pip on the progression track. Field types are taken from
    /// <c>ProgressionEventRewardDTO</c> (dump.cs:1199338): ProgressionEventRewardId
    /// is Int64 at +0x10 but <b>GiftDropId is Int32</b> at +0x18 (the audit doc
    /// says Int64 — the binary disagrees), then string ImageName, Int32 Xp,
    /// Int32 RewardIndex, bool IsBonus.</summary>
    public sealed record EventReward(
        long ProgressionEventRewardId,
        int GiftDropId,
        string ImageName,
        int Xp,
        int RewardIndex,
        bool IsBonus);

    /// <summary>The progression track's reward list. One source of truth for
    /// <see cref="Event"/> (which renders the pips) and <see cref="Collect"/>
    /// (which bounds-checks the claimed index), so the two can't drift into a
    /// state where the client can see a chest the server refuses to open.</summary>
    private static IReadOnlyList<EventReward> BuildRewards(WeeklyChallengeReward reward, long activeId) =>
    [
        new EventReward(
            ProgressionEventRewardId: activeId * 1000L,
            // yyyyMMdd fits Int32 comfortably, so the event id doubles as a
            // stable placeholder drop id when no reward drop is configured.
            GiftDropId: reward.GiftDropId != 0 ? reward.GiftDropId : (int)activeId,
            ImageName: string.Empty,
            Xp: reward.Xp,
            RewardIndex: 0,
            IsBonus: false),
    ];

    private static string RewardClaimPrefix(long eventId) => $"progressionEvent:{eventId}:reward:";

    // ── XP-boost catalogue ───────────────────────────────────────────────

    public sealed record XpBoostOffer(
        Guid Id,
        int Cost,
        int XpMultiplier,
        int XpCap,
        long LookbackDurationTicks,
        long CooldownDurationTicks);

    /// <summary>The boosts this deployment sells for a given event.
    ///
    /// Configured under <c>ProgressionEvents:XpBoosts</c> as an array of
    /// <c>{Cost, XpMultiplier, XpCap, LookbackMinutes, CooldownMinutes}</c>;
    /// with nothing configured we fall back to a single built-in offer (same
    /// pattern as <see cref="ServerSettingsService.DefaultWeeklyChallenges"/>
    /// and the built-in NUX checklist). Set
    /// <c>ProgressionEvents:EnableXpBoosts</c> to false to sell none — the
    /// event then reports <c>UsesBoost=false</c> and the sheet stays shut.</summary>
    private IReadOnlyList<XpBoostOffer> BoostCatalogue(long eventId)
    {
        if (!config.GetValue("ProgressionEvents:EnableXpBoosts", true))
            return [];

        var configured = config.GetSection("ProgressionEvents:XpBoosts").GetChildren().ToList();
        if (configured.Count == 0)
        {
            return
            [
                new XpBoostOffer(
                    Id: OfferId(eventId, 0),
                    Cost: 250,
                    XpMultiplier: 2,
                    XpCap: 500,
                    LookbackDurationTicks: TimeSpan.FromHours(1).Ticks,
                    CooldownDurationTicks: TimeSpan.FromHours(24).Ticks),
            ];
        }

        var offers = new List<XpBoostOffer>(configured.Count);
        for (var i = 0; i < configured.Count; i++)
        {
            var section = configured[i];
            var cap = Math.Max(0, section.GetValue("XpCap", 500));
            offers.Add(new XpBoostOffer(
                Id: OfferId(eventId, i),
                Cost: Math.Max(0, section.GetValue("Cost", 250)),
                XpMultiplier: Math.Max(1, section.GetValue("XpMultiplier", 2)),
                XpCap: cap,
                LookbackDurationTicks: TimeSpan.FromMinutes(
                    Math.Max(0, section.GetValue("LookbackMinutes", 60))).Ticks,
                CooldownDurationTicks: TimeSpan.FromMinutes(
                    Math.Max(0, section.GetValue("CooldownMinutes", 1440))).Ticks));
        }
        return offers;
    }

    /// <summary>Deterministic per-(event, slot) boost id. The client caches
    /// the guid from <see cref="XpBoosts"/> and replays it to
    /// <see cref="PreviewEarnedXp"/> and to
    /// <c>storefronts/v1/buyProgressionEventXpBoost</c>, whose purchase /
    /// cooldown sentinel is keyed on it — a freshly generated guid per request
    /// would make every preview miss its own catalogue entry and reset the
    /// cooldown on every call. Derived rather than stored because the boost
    /// catalogue itself lives in configuration, not the database.</summary>
    private static Guid OfferId(long eventId, int index) =>
        new(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"dorknet:progressionEventXpBoost:{eventId}:{index}"))
            .AsSpan(0, 16));

    private static long CurrentProgressionEventId()
    {
        var (start, _) = CurrentWeekWindow();
        return long.Parse(start.ToString("yyyyMMdd"));
    }

    private static (DateTime Start, DateTime End) CurrentWeekWindow()
    {
        var start = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
        return (start, start.AddDays(7));
    }
}
