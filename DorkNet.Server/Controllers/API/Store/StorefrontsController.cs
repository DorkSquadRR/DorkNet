using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Store;

/// <summary>
/// api.rec.net/api/storefronts/v* — real implementation backed by the
/// <see cref="StoreService"/> catalog (StoreItems table). Replaces the
/// previous AllEndpointsController stubs that returned empty lists,
/// which made the watch's Shop tab look broken.
///
/// Wire shape: every endpoint returns either a list of items (for
/// catalog endpoints) or a StorefrontDTO with a StoreItems array.
/// </summary>
[ApiController]
public class StorefrontsController(
    StoreService store,
    DorkNetDbContext db,
    IConfiguration config,
    LevelService level,
    DomainConfig domain) : ControllerBase
{
    /// <summary>GET api/storefronts/v3/all — full active catalog. The
    /// watch's Shop tab uses this to populate every category.</summary>
    [HttpGet("api/storefronts/v1/all")]
    [HttpGet("api/storefronts/v2/all")]
    [HttpGet("api/storefronts/v3/all")]
    [AllowAnonymous]
    public async Task<IActionResult> All()
    {
        var items = await store.GetAllActiveAsync();
        return Ok(items.Select(i => StoreService.ToItemDto(i, domain.Apex)).ToArray());
    }

    /// <summary>GET api/storefronts/v3/skus — alias for the full
    /// catalog. Some 2020 client paths call this instead of /all
    /// during the watch's "browse all SKUs" flow.</summary>
    [HttpGet("api/storefronts/v3/skus")]
    [AllowAnonymous]
    public async Task<IActionResult> Skus()
    {
        var items = await store.GetAllActiveAsync();
        return Ok(items.Select(i => StoreService.ToItemDto(i, domain.Apex)).ToArray());
    }

    /// <summary>GET api/storefronts/v3/season — items currently in the
    /// season-pass storefront. Empty for now since we don't run
    /// seasons; returning an empty list rather than the catch-all so
    /// the deserialiser sees the right shape.</summary>
    [HttpGet("api/storefronts/v3/season")]
    [AllowAnonymous]
    public async Task<IActionResult> Season()
    {
        var items = await store.GetActiveByStorefrontAsync("season:1");
        return Ok(items.Select(i => StoreService.ToItemDto(i, domain.Apex)).ToArray());
    }

    /// <summary>GET api/storefronts/v3/giftdrop — list of available
    /// gift-drop storefront ids. The watch then queries each one via
    /// /giftdropstore/{id} below to render the rotating shelf.</summary>
    [HttpGet("api/storefronts/v3/giftdrop")]
    [AllowAnonymous]
    public IActionResult GiftDropList() =>
        Ok(StoreService.GetStorefrontDefinitions()
            .Where(s => s.StorefrontType is > 0)
            .Select(s => s.StorefrontType!.Value)
            .Distinct()
            .OrderBy(i => i)
            .ToArray());

    /// <summary>GET api/storefronts/v3/giftdropstore/{id} — one
    /// storefront shelf. The <c>{id}</c> is a value from the
    /// <c>RecNet.StorefrontTypes</c> enum (verified against
    /// Cpp2IL_CS/.../RecNet/StorefrontTypes.cs):
    ///   None=0, LaserTag=1, RecCenter=2, <b>Watch=3</b>,
    ///   Quest_*=100..103, RecRoyale=200, Cafe=300, Paintball=400..406,
    ///   Bowling=500, StuntRunner=600, DormMirror=700.
    /// The Watch storefront (id=3) is the in-game Shop's Featured /
    /// Clothing / Other grid. That grid assumes every gift drop has a
    /// renderable AvatarItemDesc, so only wardrobe-backed rows are
    /// surfaced there. Other storefronts use the canonical
    /// <c>giftdrop:{id}</c> key plus the shared <c>rro</c> and
    /// <c>all</c> admin keys.
    /// </summary>
    [HttpGet("api/storefronts/v3/giftdropstore/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GiftDropStore(int id)
    {
        var (key, items) = id switch
        {
            3 => ($"watch", await store.GetRenderableGiftDropItemsAsync()),
            _ => (StoreService.StorefrontKeyForType(id), await store.GetRoomStorefrontItemsAsync(id)),
        };
        return Ok(StoreService.ToStorefrontDto(key, items, domain.Apex, id));
    }

    /// <summary>GET api/storefronts/v1/balance/{type} — currency
    /// balance for the caller. Wire shape is a list of
    /// {CurrencyType, Balance, BalanceType, Platform} entries. The
    /// watch displays the Balance value next to item prices.</summary>
    [HttpGet("api/storefronts/v1/balance/{currencyType:int}")]
    [HttpGet("api/storefronts/v2/balance/{currencyType:int}")]
    [HttpGet("api/storefronts/v3/balance/{currencyType:int}")]
    [HttpGet("api/storefronts/v4/balance/{currencyType:int}")]
    [HttpGet("api/storefronts/v4/balance")]
    [Authorize]
    public async Task<IActionResult> Balance(int currencyType = 2,
        [FromServices] LevelService level = null!)
    {
        var pid = this.RequireCurrentPlayerId();
        var balance = await level.GetBalanceAsync(pid, currencyType);
        return Ok(new[]
        {
            new
            {
                CurrencyType = currencyType,
                BalanceType = 0,
                Balance = balance,
                Platform = 0,
            },
        });
    }

    /// <summary>GET <c>api/storefronts/v{1-4}/balanceAddType/{type}/{tier}</c>
    /// — purchase-tier metadata for a currency. The watch uses this
    /// to populate the "add tokens" tier list. Empty for a private
    /// server with no IAP.</summary>
    [HttpGet("api/storefronts/v1/balanceAddType/{currencyType:int}/{tierId:int}")]
    [HttpGet("api/storefronts/v2/balanceAddType/{currencyType:int}/{tierId:int}")]
    [HttpGet("api/storefronts/v3/balanceAddType/{currencyType:int}/{tierId:int}")]
    [HttpGet("api/storefronts/v4/balanceAddType/{currencyType:int}/{tierId:int}")]
    public IActionResult BalanceAddType(int currencyType, int tierId)
        => Ok(BalanceAddConfig(currencyType, tierId));

    /// <summary>GET <c>api/storefronts/v2/balance</c> — every currency
    /// balance for the caller in a single response. The watch's
    /// wallet UI uses this to render all four currency chips at once.
    /// CurrencyType per <c>CurrencyType.cs</c>: 0=Token, 1=Cheer,
    /// 2=Coin, 3=Premium.</summary>
    [HttpGet("api/storefronts/v2/balance")]
    [Authorize]
    public async Task<IActionResult> AllBalances()
    {
        var pid = this.RequireCurrentPlayerId();
        var coin = await level.GetBalanceAsync(pid, 2);
        var token = await level.GetBalanceAsync(pid, 0);
        return Ok(new[]
        {
            new { CurrencyType = 2, BalanceType = 0, Balance = coin,  Platform = 0 },
            new { CurrencyType = 0, BalanceType = 0, Balance = token, Platform = 0 },
            new { CurrencyType = 1, BalanceType = 0, Balance = 0L,    Platform = 0 },
            new { CurrencyType = 3, BalanceType = 0, Balance = 0L,    Platform = 0 },
        });
    }

    /// <summary>GET <c>api/storefronts/v1/current</c> — single active
    /// storefront the watch's "today's deals" panel renders. Returns
    /// the <c>main</c> storefront's items as a single PurchasableItem
    /// list (bounded to keep older watches snappy).</summary>
    [HttpGet("api/storefronts/v1/current")]
    public async Task<IActionResult> Current()
    {
        if (!config.GetValue("Store:EnableWatchGiftDrops", true))
            return Ok(StoreService.ToStorefrontDto("Main", Array.Empty<StoreItemEntity>(), domain.Apex));

        IQueryable<StoreItemEntity> query = db.StoreItems
            .Where(s => s.IsActive && s.Storefront == "main")
            .OrderByDescending(s => s.UpdatedAt);
        var max = config.GetValue("Store:MaxCurrentStorefrontItems", 96);
        if (max > 0) query = query.Take(max);
        var rows = await query.ToListAsync();
        return Ok(StoreService.ToStorefrontDto("Main", rows, domain.Apex));
    }

    /// <summary>GET <c>api/storefronts/v1/season/{seasonId}</c> —
    /// season-pass tier list, sourced from StoreItems tagged
    /// <c>Storefront = "season:{id}"</c>.</summary>
    [HttpGet("api/storefronts/v1/season/{seasonId:int}")]
    public async Task<IActionResult> Season(int seasonId)
    {
        var key = $"season:{seasonId}";
        var tiers = await db.StoreItems
            .Where(s => s.IsActive && s.Storefront == key)
            .OrderBy(s => s.Price)
            .Select(s => new
            {
                TierId = s.Id,
                s.DisplayName,
                s.Description,
                s.Price,
                CurrencyType = s.CurrencyType,
                s.ImageName,
            })
            .ToListAsync();
        return Ok(new
        {
            SeasonId = seasonId,
            SeasonType = seasonId,
            Active = tiers.Count > 0,
            StartsAt = DateTime.UtcNow.AddDays(-30),
            EndsAt = DateTime.UtcNow.AddDays(30),
            Tiers = tiers,
        });
    }

    /// <summary>GET <c>api/storefronts/v1/objectives</c> — season
    /// objective progress for the caller, keyed
    /// <c>season:{n}</c>.</summary>
    [HttpGet("api/storefronts/v1/objectives")]
    public async Task<IActionResult> Objectives()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me) return Ok(Array.Empty<object>());
        var rows = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me && o.Key.StartsWith("season:"))
            .Select(o => new { Key = o.Key, IsCompleted = o.IsCompleted, ClearedAt = o.ClearedAt })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>POST <c>api/storefronts/v1/objectives</c> —
    /// <c>Storefronts.CompleteObjectives</c> uploads completion
    /// records and expects a <c>BalanceUpdateResponseDTO</c> back.
    /// We ship the wrapper with the required Balance/CurrencyType/
    /// BalanceType trio so <c>BalanceResponseDTO.Deserialize</c>
    /// doesn't throw, plus an empty <c>BalanceUpdates</c>.</summary>
    [HttpPost("api/storefronts/v1/objectives")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    [Authorize]
    public async Task<IActionResult> CompleteObjectives()
    {
        var pid = this.RequireCurrentPlayerId();
        var records = await ReadStorefrontObjectiveRecordsAsync();
        var total = 0;
        foreach (var record in records ?? new())
        {
            if (record.CompletionPercentage <= 0f) continue;
            var rewardKey = $"storefront-objective:{record.ObjectiveType}:{DateTime.UtcNow:yyyyMMdd}";
            if (await MarkRewardOnceAsync(pid, rewardKey))
            {
                var award = record.ObjectiveType switch
                {
                    10 or 11 or 12 => 25,
                    13 => 150,
                    14 => 50,
                    15 => 75,
                    4000 or 4001 => 50,
                    _ => Math.Max(5, (int)Math.Round(25 * Math.Clamp(record.CompletionPercentage, 0f, 1f))),
                };
                total += award;
            }
        }

        if (total > 0)
        {
            await level.AwardXpAsync(pid, total, "storefront-objectives");
            await level.GrantCurrencyAsync(pid, 2, total, "storefront-objectives");
        }
        var balance = await level.GetBalanceAsync(pid, 2);
        return Ok(BalanceUpdateResponse(balance, 2, new[]
        {
            RewardModification(100, total, total, currentCount: total > 0 ? 1 : 0),
        }));
    }

    public sealed class StorefrontObjectiveCompletionRecord
    {
        public int ObjectiveType { get; set; }
        public float CompletionPercentage { get; set; }
        public long? RoomId { get; set; }
    }

    public sealed class GrantBalanceRewardDto
    {
        public int CurrencyType { get; set; } = 2;
        public List<GrantBalanceRequest> BalanceAdds { get; set; } = new();
    }

    public sealed class GrantBalanceRequest
    {
        public float Multiplier { get; set; } = 1f;
        public int BalanceAddType { get; set; } = 1;
    }

    [HttpPost("api/storefronts/v2/balance")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    [Authorize]
    public async Task<IActionResult> ModifyBalance()
    {
        var body = await ReadGrantBalanceRewardAsync();
        var pid = this.RequireCurrentPlayerId();
        var currencyType = body.CurrencyType == 0 ? 2 : body.CurrencyType;
        var modifications = new List<object>();
        var total = 0;

        foreach (var add in body.BalanceAdds ?? new())
        {
            var config = BalanceAddConfigValues(currencyType, add.BalanceAddType);
            var multiplier = Math.Clamp(add.Multiplier <= 0 ? 1f : add.Multiplier, 0f, config.MaxPartialMultiplier);
            var baseAward = config.IgnorePartialMultiplier
                ? config.BaseAward
                : (int)Math.Round(config.BaseAward * multiplier);
            baseAward = Math.Max(0, baseAward);
            if (baseAward == 0) continue;
            total += baseAward;
            modifications.Add(RewardModification(add.BalanceAddType, baseAward, baseAward, currentCount: 1));
        }

        if (total > 0)
        {
            await level.GrantCurrencyAsync(pid, currencyType, total, "storefront-balance-reward");
            await level.AwardXpAsync(pid, Math.Max(10, total / 2), "storefront-balance-reward");
        }

        var balance = await level.GetBalanceAsync(pid, currencyType);
        return Ok(BalanceUpdateResponse(balance, currencyType, modifications));
    }

    public sealed class BalanceAddTypeRequest { public long Amount { get; set; } }

    /// <summary>POST <c>api/storefronts/v1/balanceAddType/{type}/{playerId}</c>
    /// — admin-only currency credit. Grants via
    /// <see cref="LevelService.GrantCurrencyAsync"/>.</summary>
    [HttpPost("api/storefronts/v1/balanceAddType/{type:int}/{playerId:long}")]
    [Authorize]
    public async Task<IActionResult> BalanceAddTypeAdmin(int type, long playerId, [FromBody] BalanceAddTypeRequest? body)
    {
        var me = this.RequireCurrentPlayerId();
        var isAdmin = await db.Players.Where(p => p.Id == me).Select(p => p.IsAdmin).FirstOrDefaultAsync();
        if (!isAdmin) return Forbid();

        var amount = body?.Amount ?? 0L;
        if (amount == 0) return Ok(new { Added = 0L });
        var newBalance = await level.GrantCurrencyAsync(playerId, type, amount, $"balanceAddType:{me}");
        return Ok(new
        {
            CurrencyType = type,
            Balance = newBalance,
            BalanceType = 0,
            Platform = 0,
        });
    }

    private static object BalanceAddConfig(int currencyType, int balanceAddType)
    {
        var cfg = BalanceAddConfigValues(currencyType, balanceAddType);
        return new
        {
            CurrencyType = currencyType,
            BalanceAddType = balanceAddType,
            cfg.BaseAward,
            BonusAwardMin = 0,
            BonusAwardMax = 0,
            RateLimitType = 0,
            IgnorePartialMultiplier = cfg.IgnorePartialMultiplier,
            MaxPartialMultiplier = (int)Math.Ceiling(cfg.MaxPartialMultiplier),
            RateLimit = 0,
            BalanceInGiftBox = false,
        };
    }

    private static (int BaseAward, float MaxPartialMultiplier, bool IgnorePartialMultiplier)
        BalanceAddConfigValues(int currencyType, int balanceAddType) => balanceAddType switch
        {
            1 => (100, 10f, false),     // DirectBalanceWithMultiplier: Stunt Runner / banked loot.
            10 => (25, 1f, true),       // NUXChallenge.
            11 => (100, 1f, true),      // AllNUXChallenges.
            100 => (25, 1f, true),      // DailyChallenge.
            101 => (150, 1f, true),     // AllDailyChallenges.
            200 => (50, 1f, true),      // FinishActivity.
            1000 => (100, 1f, true),    // WonGame.
            1001 => (25, 1f, true),     // LostGame.
            _ => (25, 1f, true),
        };

    private async Task<GrantBalanceRewardDto> ReadGrantBalanceRewardAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var dto = new GrantBalanceRewardDto
            {
                CurrencyType = int.TryParse(form["CurrencyType"], out var ct) ? ct : 2,
            };
            if (int.TryParse(form["BalanceAddType"], out var bat) || int.TryParse(form["balanceAddType"], out bat))
            {
                dto.BalanceAdds.Add(new GrantBalanceRequest
                {
                    BalanceAddType = bat,
                    Multiplier = float.TryParse(form["Multiplier"], out var mult) ? mult : 1f,
                });
            }
            return dto;
        }

        try
        {
            var dto = await JsonSerializer.DeserializeAsync<GrantBalanceRewardDto>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return dto ?? new GrantBalanceRewardDto();
        }
        catch
        {
            return new GrantBalanceRewardDto();
        }
    }

    private async Task<List<StorefrontObjectiveCompletionRecord>> ReadStorefrontObjectiveRecordsAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (!int.TryParse(form["objectiveType"], out var objectiveType)
                && !int.TryParse(form["ObjectiveType"], out objectiveType))
            {
                return new();
            }
            return new()
            {
                new StorefrontObjectiveCompletionRecord
                {
                    ObjectiveType = objectiveType,
                    CompletionPercentage = float.TryParse(form["completionPercentage"], out var pct)
                        || float.TryParse(form["CompletionPercentage"], out pct)
                        ? pct
                        : 1f,
                    RoomId = long.TryParse(form["roomId"], out var roomId)
                        || long.TryParse(form["RoomId"], out roomId)
                        ? roomId
                        : null,
                },
            };
        }

        try
        {
            var rows = await JsonSerializer.DeserializeAsync<List<StorefrontObjectiveCompletionRecord>>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return rows ?? new();
        }
        catch
        {
            return new();
        }
    }

    private async Task<bool> MarkRewardOnceAsync(long playerId, string rewardKey)
    {
        var key = $"reward:{rewardKey}";
        if (await db.ObjectiveProgress.AnyAsync(o => o.PlayerId == playerId && o.Key == key && o.IsCompleted))
            return false;
        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = playerId,
            Key = key,
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return true;
    }

    private static object RewardModification(int balanceAddType, int baseAward, int total, int currentCount) => new
    {
        BalanceAddType = balanceAddType,
        BaseAward = baseAward,
        BonusAward = 0,
        RateLimit = 0,
        CurrentCount = currentCount,
        Total = total,
        BalanceType = 0,
        BalanceInGiftBox = false,
    };

    private static object BalanceUpdateResponse(long balance, int currencyType, IEnumerable<object> modifications) => new
    {
        Balance = balance,
        CurrencyType = currencyType,
        BalanceType = 0,
        BalanceUpdates = new[]
        {
            new
            {
                UpdateResponse = 0,
                Data = modifications.Where(m => m is not null).ToArray(),
            },
        },
    };
}
