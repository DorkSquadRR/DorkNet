using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.GameRewards;

/// <summary>
/// api.rec.net/api/gamerewards/v1/* — the post-activity "choose 1 of 3"
/// item reward the client's <c>RewardManager</c> shows after a Stunt
/// Runner run (RewardType <c>PostGameActivity=2</c>) or an RRO quest
/// (<c>PostQuestActivity=3</c>). The client POSTs <c>/request</c> with a
/// rewardType + giftContext (no score/rank on the wire — the rank scales
/// the separate <b>currency</b> reward, which arrives via
/// <c>storefronts/v2/balance</c> as a rank-derived multiplier that
/// <see cref="Store.StorefrontsController.ModifyBalance"/> honours), then
/// polls <c>/pending</c> and picks one via <c>/select</c>.
///
/// Each of GiftDrop1/2/3 is a real store item built through
/// <see cref="StoreService.BuildGiftDrop"/> — the SAME wire-verified
/// EFFIEFEFHHB shape the storefront uses (notably AvatarItemId is an
/// Int32), so the strict 2023 reader doesn't reject the selection. On
/// <c>/select</c> the chosen item is granted to inventory for free.
/// </summary>
[ApiController]
[Authorize]
public class GameRewardsController(DorkNetDbContext db, LevelService level, StoreService store) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    private const int OfferCount = 3;

    [HttpGet("api/gamerewards/v1/pending")]
    public async Task<IActionResult> Pending()
    {
        var pid = Me;
        var rows = await db.GameRewardSelections
            .Where(r => r.PlayerId == pid && r.SelectedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();
        var items = await LoadOfferedItemsAsync(rows);
        return Ok(rows.Select(r => ToWire(r, items)));
    }

    [HttpPost("api/gamerewards/v1/request")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> RequestReward()
    {
        var req = await ReadRewardRequestAsync();
        var pid = Me;
        var row = new GameRewardSelectionEntity
        {
            PlayerId = pid,
            RewardType = req.RewardType,
            GiftContext = req.GiftContext,
            Message = string.IsNullOrWhiteSpace(req.Message)
                ? "Choose a reward"
                : req.Message[..Math.Min(req.Message.Length, 256)],
        };
        db.GameRewardSelections.Add(row);
        await db.SaveChangesAsync();

        // Pick the three offered items now that the row has an id (used as
        // the deterministic seed) and persist them so /pending is stable
        // and /select grants exactly what was shown.
        var offered = await store.PickRewardItemsAsync(OfferCount, unchecked((int)row.Id));
        if (offered.Count > 0) row.Offer1ItemId = offered[0].Id;
        if (offered.Count > 1) row.Offer2ItemId = offered[1].Id;
        if (offered.Count > 2) row.Offer3ItemId = offered[2].Id;
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Error = string.Empty });
    }

    [HttpPost("api/gamerewards/v1/select")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> SelectReward()
    {
        var req = await ReadRewardSelectAsync();
        var pid = Me;
        var row = await db.GameRewardSelections
            .FirstOrDefaultAsync(r => r.Id == req.RewardSelectionId && r.PlayerId == pid && r.SelectedAt == null);
        if (row is null) return Ok(new { Success = false, Error = "reward_not_found" });

        // Map the client's chosen GiftDropId back to one of the offered
        // store items. The GiftDropId is the item's PurchasableItemId.
        var offeredIds = new[] { row.Offer1ItemId, row.Offer2ItemId, row.Offer3ItemId }
            .Where(id => id > 0)
            .ToList();
        var items = await db.StoreItems.Where(i => offeredIds.Contains(i.Id)).ToListAsync();
        var chosen = items.FirstOrDefault(i => StoreService.PurchasableItemIdFor(i) == req.GiftDropId)
                     ?? items.FirstOrDefault();

        row.SelectedAt = DateTime.UtcNow;
        row.SelectedGiftDropId = req.GiftDropId;
        if (chosen is not null)
        {
            await store.GrantItemAsync(pid, chosen.Id);
            row.GrantedItemId = chosen.Id;
        }
        // Small token + XP kicker on top of the item, matching the
        // post-activity "you also banked a little" feel.
        await level.GrantCurrencyAsync(pid, 2, 25, $"gameReward:{row.Id}");
        await level.AwardXpAsync(pid, 25, $"gameReward:{row.Id}");
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Error = string.Empty });
    }

    private async Task<Dictionary<long, StoreItemEntity>> LoadOfferedItemsAsync(
        IEnumerable<GameRewardSelectionEntity> rows)
    {
        var ids = rows
            .SelectMany(r => new[] { r.Offer1ItemId, r.Offer2ItemId, r.Offer3ItemId })
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return new();
        return await db.StoreItems
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);
    }

    private static object ToWire(GameRewardSelectionEntity row, IReadOnlyDictionary<long, StoreItemEntity> items) => new
    {
        RewardSelectionId = row.Id,
        row.Message,
        row.GiftContext,
        row.RewardType,
        GiftDrop1 = OfferDrop(row.Offer1ItemId, items),
        GiftDrop2 = OfferDrop(row.Offer2ItemId, items),
        GiftDrop3 = OfferDrop(row.Offer3ItemId, items),
        row.CreatedAt,
    };

    private static object? OfferDrop(long itemId, IReadOnlyDictionary<long, StoreItemEntity> items)
    {
        if (itemId <= 0 || !items.TryGetValue(itemId, out var item)) return null;
        return StoreService.BuildGiftDrop(item, StoreService.PurchasableItemIdFor(item));
    }

    private async Task<RewardRequest> ReadRewardRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new RewardRequest
            {
                RewardType = int.TryParse(form["rewardType"].FirstOrDefault(), out var rt) ? rt : 0,
                GiftContext = int.TryParse(form["giftContext"].FirstOrDefault(), out var gc) ? gc : 0,
                Message = form["Message"].FirstOrDefault() ?? form["message"].FirstOrDefault(),
            };
        }
        try
        {
            return await JsonSerializer.DeserializeAsync<RewardRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RewardRequest();
        }
        catch (JsonException) { return new RewardRequest(); }
    }

    private async Task<RewardSelect> ReadRewardSelectAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new RewardSelect
            {
                RewardSelectionId = long.TryParse(form["rewardSelectionId"].FirstOrDefault(), out var id) ? id : 0,
                GiftDropId = int.TryParse(form["giftDropId"].FirstOrDefault(), out var giftDropId) ? giftDropId : 0,
            };
        }
        try
        {
            return await JsonSerializer.DeserializeAsync<RewardSelect>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RewardSelect();
        }
        catch (JsonException) { return new RewardSelect(); }
    }

    private sealed class RewardRequest
    {
        public int RewardType { get; set; }
        public int GiftContext { get; set; }
        public string? Message { get; set; }
    }

    private sealed class RewardSelect
    {
        public long RewardSelectionId { get; set; }
        public int GiftDropId { get; set; }
    }
}
