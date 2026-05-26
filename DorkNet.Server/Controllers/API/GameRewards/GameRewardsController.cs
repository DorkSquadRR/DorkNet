using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.GameRewards;

[ApiController]
[Authorize]
public class GameRewardsController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("api/gamerewards/v1/pending")]
    public async Task<IActionResult> Pending()
    {
        var pid = Me;
        var rows = await db.GameRewardSelections
            .Where(r => r.PlayerId == pid && r.SelectedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpPost("api/gamerewards/v1/request")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> RequestReward()
    {
        var req = await ReadRewardRequestAsync();
        var row = new GameRewardSelectionEntity
        {
            PlayerId = Me,
            RewardType = req.RewardType,
            GiftContext = req.GiftContext,
            Message = string.IsNullOrWhiteSpace(req.Message) ? "Choose a reward" : req.Message[..Math.Min(req.Message.Length, 256)],
        };
        db.GameRewardSelections.Add(row);
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

        row.SelectedAt = DateTime.UtcNow;
        row.SelectedGiftDropId = req.GiftDropId;
        await level.GrantCurrencyAsync(pid, 2, 25, $"gameReward:{row.Id}");
        await level.AwardXpAsync(pid, 25, $"gameReward:{row.Id}");
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Error = string.Empty });
    }

    private static object ToWire(GameRewardSelectionEntity row) => new
    {
        RewardSelectionId = row.Id,
        row.Message,
        row.GiftContext,
        row.RewardType,
        GiftDrop1 = GiftDrop(row.Id * 10 + 1, "Tokens", currency: 25),
        GiftDrop2 = GiftDrop(row.Id * 10 + 2, "XP", currency: 0),
        GiftDrop3 = GiftDrop(row.Id * 10 + 3, "Bonus Tokens", currency: 25),
        row.CreatedAt,
    };

    private static object GiftDrop(long id, string name, int currency) => new
    {
        GiftDropId = id,
        FriendlyName = name,
        Tooltip = string.Empty,
        ConsumableItemDesc = string.Empty,
        AvatarItemDesc = string.Empty,
        AvatarItemType = 0,
        EquipmentPrefabName = string.Empty,
        EquipmentModificationGuid = string.Empty,
        IsQuery = false,
        Unique = false,
        SubscribersOnly = false,
        Rarity = 0,
        CurrencyType = 2,
        Currency = currency,
        Context = 0,
        ItemSetId = 0,
        ItemSetFriendlyName = string.Empty,
    };

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
