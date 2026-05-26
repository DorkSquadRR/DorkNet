using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.ProgressionApi;

/// <summary>
/// api.rec.net/api/{challenge,checklist,objectives}/* — daily
/// challenges, onboarding checklist, and the full objectives surface
/// (myprogress, cleargroup, updateobjective, completegroup). All
/// concepts share the same persistence model:
/// <see cref="ObjectiveProgressEntity"/> rows keyed by a prefix:
/// <c>challenge:{id}</c>, <c>checklist:{id}</c>, <c>group:{id}</c>,
/// <c>obj:{group}:{index}</c>.
/// </summary>
[ApiController]
public class ProgressionController(
    DorkNetDbContext db,
    LevelService level,
    ServerSettingsService serverSettings) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();
    private long? MeOrNull => this.CurrentPlayerId();
    private const int RecCenterTokens = 2;

    // ── Challenges ───────────────────────────────────────────────────────

    /// <summary>GET <c>api/challenge/v{1,2}/getCurrent</c> — returns
    /// the active daily-challenge map (NOT a flat challenge object).
    /// Verified against
    /// <c>Cpp2IL_ISIL/.../RecNet/Challenges.txt:105-184</c>
    /// (<c>GetCurrentChallenges</c> hits this URL and parses as
    /// <c>ChallengeMap</c>). Wire shape per
    /// <c>RecNet/ChallengeMap.txt:540-616</c>:
    ///
    ///   <c>ChallengeMapId</c> (int, required), <c>CompletedRequired</c>
    ///   (bool, required), <c>StartAt</c>/<c>EndAt</c>/<c>ServerTime</c>
    ///   (DateTime, required), <c>Challenges</c> (List&lt;RecNetChallenge&gt;,
    ///   required), <c>Gift</c> (ChallengeGift, required),
    ///   <c>ChallengeThemeString</c> (string, optional).
    ///
    /// Each <c>RecNetChallenge</c> requires only <c>ChallengeId</c>;
    /// <c>Name</c>/<c>Config</c>/<c>Description</c>/<c>Tooltip</c>/<c>Complete</c>
    /// are optional. Each <c>ChallengeGift</c> requires <c>GiftDropId</c>,
    /// <c>Xp</c>, <c>Level</c>, <c>GiftContext</c>, <c>GiftRarity</c>.
    ///
    /// Previous flat-challenge response crashed
    /// <c>ChallengeMap.Deserialize</c> at <c>Util.GetKey("ChallengeMapId")</c>.</summary>
    [HttpGet("api/challenge/v1/getCurrent")]
    [HttpGet("api/challenge/v2/getCurrent")]
    public async Task<IActionResult> CurrentChallenge()
    {
        var pid = MeOrNull;
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(-(int)now.DayOfWeek);
        var mapId = CurrentChallengeMapId();

        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        var challengeDefs = weekly.Challenges;
        var keys = challengeDefs.Select(c => ChallengeProgressKey(mapId, c.Index)).ToArray();
        var rows = pid is long me
            ? await db.ObjectiveProgress
                .Where(o => o.PlayerId == me && keys.Contains(o.Key))
                .ToDictionaryAsync(o => o.Key, o => o.IsCompleted)
            : new Dictionary<string, bool>();

        bool Done(int idx) => rows.TryGetValue(keys[idx], out var v) && v;

        return Ok(new
        {
            ChallengeMapId = mapId,
            CompletedRequired = weekly.CompletedRequired,
            StartAt = start,
            EndAt = start.AddDays(7),
            ServerTime = now,
            // 2020.12 ChallengeMap.Deserialize (FNEEJMCEPOL.PPGFHEDFBEA) reads
            // FallbackGiftName as a required key. Missing → KeyNotFoundException
            // → "Received malformed RecNet response" → blocks the weekly-challenge
            // promise chain at boot.
            FallbackGiftName = string.Empty,
            ChallengeThemeString = "Weekly",
            Challenges = challengeDefs.Select(c => new
            {
                ChallengeId = ChallengeId(mapId, c.Index),
                c.Name,
                c.Config,
                c.Description,
                c.Tooltip,
                Complete = Done(c.Index),
            }),
            Gift = new
            {
                GiftDropId = weekly.Reward.GiftDropId != 0 ? weekly.Reward.GiftDropId : mapId,
                weekly.Reward.AvatarItemDesc,
                weekly.Reward.ConsumableItemDesc,
                weekly.Reward.EquipmentPrefabName,
                weekly.Reward.EquipmentModificationGuid,
                weekly.Reward.Xp,
                weekly.Reward.Level,
                weekly.Reward.GiftContext,
                weekly.Reward.GiftRarity,
            },
        });
    }

    public sealed class UpdateChallengeRequest
    {
        public int Id { get; set; }
        public int ChallengeMapId { get; set; }
        public int ChallengeId { get; set; }
        public int Progress { get; set; }
        public string? Config { get; set; }
        public bool? Complete { get; set; }
    }

    [HttpPost("api/challenge/v1/updateProgress")]
    [HttpPost("api/challenge/v2/updateProgress")]
    [Authorize]
    public async Task<IActionResult> UpdateChallengeProgress([FromForm] UpdateChallengeRequest formReq)
    {
        var pid = Me;
        var req = await ReadChallengeRequestAsync(formReq);
        var mapId = req.ChallengeMapId != 0 ? req.ChallengeMapId : CurrentChallengeMapId();
        var challengeId = req.ChallengeId != 0 ? req.ChallengeId : req.Id;
        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        var challengeCount = Math.Max(1, weekly.Challenges.Count);
        var index = ChallengeIndex(mapId, challengeId, challengeCount);

        var key = ChallengeProgressKey(mapId, index);
        var row = await db.ObjectiveProgress.FirstOrDefaultAsync(o => o.PlayerId == pid && o.Key == key);
        if (row is null)
        {
            row = new ObjectiveProgressEntity { PlayerId = pid, Key = key };
            db.ObjectiveProgress.Add(row);
        }
        var shouldComplete = req.Complete ?? true;
        if (shouldComplete && !row.IsCompleted)
        {
            row.IsCompleted = true;
            row.ClearedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        var weeklyRewarded = false;
        var allKeys = weekly.Challenges
            .Select(c => c.Index)
            .Select(i => ChallengeProgressKey(mapId, i))
            .ToArray();
        var completeCount = await db.ObjectiveProgress
            .CountAsync(o => o.PlayerId == pid && allKeys.Contains(o.Key) && o.IsCompleted);
        if (completeCount >= allKeys.Length)
        {
            weeklyRewarded = await GrantRewardOnceAsync(
                pid,
                $"weekly-challenge:{mapId}",
                xp: weekly.Reward.Xp,
                tokens: weekly.Reward.Tokens,
                reason: $"weekly_challenge:{mapId}");
        }

        return Ok(new
        {
            ChallengeMapId = mapId,
            ChallengeId = challengeId,
            Config = req.Config ?? string.Empty,
            Complete = row.IsCompleted,
            Progress = Math.Max(req.Progress, row.IsCompleted ? 10 : 0),
            IsCompleted = row.IsCompleted,
            ClearedAt = row.ClearedAt,
            Rewarded = weeklyRewarded,
        });
    }

    // ── Checklist ────────────────────────────────────────────────────────

    /// <summary>GET <c>api/checklist/v{1,2}/current</c> — returns
    /// a flat <c>List&lt;ChecklistObjective&gt;</c>, NOT a wrapper
    /// object. Wire shape per
    /// <c>Cpp2IL_CS/.../RecNet/Checklist.cs</c>:
    /// <c>Order(int), Objective(ObjectiveType int), Count(int),
    /// CreditAmount(int)</c>. All four keys are required
    /// (<c>Util.GetKey</c>, not <c>OrDefault</c>).
    ///
    /// Completion state is tracked separately by the client via
    /// <see cref="ObjectiveProgress"/> rows it cross-references — we
    /// don't put a per-item "done" flag on the wire; the client
    /// matches by Order. Returning an empty list ([]) is also valid
    /// per the deserialiser (List wrapper accepts empty).</summary>
    [HttpGet("api/checklist/v1/current")]
    [HttpGet("api/checklist/v2/current")]
    public IActionResult CurrentChecklist()
    {
        // Default NUX checklist for a brand-new account. Each row
        // maps an in-game step to an ObjectiveType enum value
        // (decompiled from ProgressionManager.cs).
        return Ok(new[]
        {
            new { Order = 0, Objective = 38 /* SaveOutfitSlot */,    Count = 1, CreditAmount = 25 },
            new { Order = 1, Objective = 32 /* VisitACustomRoom */,   Count = 1, CreditAmount = 25 },
            new { Order = 2, Objective = 2  /* AddAFriend */,          Count = 1, CreditAmount = 25 },
            new { Order = 3, Objective = 30 /* GoToRecCenter */,       Count = 1, CreditAmount = 25 },
            new { Order = 4, Objective = 6  /* CheerAPlayer */,        Count = 1, CreditAmount = 25 },
        });
    }

    public sealed class CompleteChecklistRequest
    {
        public int Id { get; set; }
        public int ItemIndex { get; set; }
    }

    [HttpPost("api/checklist/v1/complete")]
    [HttpPost("api/checklist/v2/complete")]
    [Authorize]
    public async Task<IActionResult> CompleteChecklist([FromBody] CompleteChecklistRequest req)
    {
        var pid = Me;
        var id = req.ItemIndex != 0 ? req.ItemIndex : req.Id;
        var key = $"checklist:{id}";
        var row = await db.ObjectiveProgress.FirstOrDefaultAsync(o => o.PlayerId == pid && o.Key == key);
        if (row is null)
        {
            row = new ObjectiveProgressEntity { PlayerId = pid, Key = key };
            db.ObjectiveProgress.Add(row);
        }
        row.IsCompleted = true;
        row.ClearedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var rewarded = await GrantRewardOnceAsync(
            pid,
            $"checklist:{id}",
            xp: 25,
            tokens: 25,
            reason: $"checklist:{id}");
        var balance = await level.GetBalanceAsync(pid, RecCenterTokens);
        return Ok(BalanceUpdateResponse(balance, RecCenterTokens, rewarded ? 25 : 0, 303));
    }

    // ── Objectives bulk-complete (real persistence) ──────────────────────

    /// <summary>POST <c>api/objectives/v1/completegroup</c> — replaces
    /// the previous shape-stub in MissingEndpointsController. Now
    /// upserts a real <see cref="ObjectiveProgressEntity"/> row keyed
    /// by <c>group:{id}</c>.</summary>
    public sealed class UpdateObjectiveRequest
    {
        public int Group { get; set; }
        public int Index { get; set; }
        public float Progress { get; set; }
        public bool IsCompleted { get; set; }
    }

    /// <summary>POST <c>api/objectives/v1/updateobjective</c> — per-
    /// objective progress update. Was previously caught by the
    /// namespace wildcard returning <c>[]</c>; now persists to
    /// <see cref="ObjectiveProgressEntity"/> keyed
    /// <c>obj:{group}:{index}</c> so progress survives sessions.
    /// Wire shape from <c>RecNet.Objectives.UpdateObjectiveProgress</c>.</summary>
    [HttpPost("api/objectives/v1/updateobjective")]
    [Authorize]
    public async Task<IActionResult> UpdateObjective([FromBody] UpdateObjectiveRequest req)
    {
        var pid = Me;
        var key = $"obj:{req.Group}:{req.Index}";
        var row = await db.ObjectiveProgress.FirstOrDefaultAsync(o => o.PlayerId == pid && o.Key == key);
        if (row is null)
        {
            row = new ObjectiveProgressEntity { PlayerId = pid, Key = key };
            db.ObjectiveProgress.Add(row);
        }
        if (req.IsCompleted || req.Progress >= 1f)
        {
            row.IsCompleted = true;
            row.ClearedAt ??= DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        var rewardKey = $"obj:{req.Group}:{req.Index}";
        var isRewarded = await IsRewardedAsync(pid, rewardKey);
        return Ok(new
        {
            // 2020.12 NMEKJOABIFC.PPGFHEDFBEA: HasClaimedReward (not IsRewarded).
            req.Index,
            req.Group,
            Progress = row.IsCompleted ? 1f : req.Progress,
            VisualProgress = row.IsCompleted ? 1f : req.Progress,
            IsCompleted = row.IsCompleted,
            HasClaimedReward = isRewarded,
        });
    }

    [HttpPost("api/objectives/v1/completegroup")]
    [Authorize]
    public async Task<IActionResult> CompleteGroup([FromBody] CompleteGroupBody body)
    {
        var pid = Me;
        var key = $"group:{body.Group}";
        var row = await db.ObjectiveProgress.FirstOrDefaultAsync(o => o.PlayerId == pid && o.Key == key);
        if (row is null)
        {
            row = new ObjectiveProgressEntity { PlayerId = pid, Key = key };
            db.ObjectiveProgress.Add(row);
        }
        row.IsCompleted = true;
        row.ClearedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var rewarded = await GrantObjectiveGroupRewardAsync(pid, body.Group);
        return Ok(new
        {
            Group = body.Group,
            IsCompleted = true,
            ClearedAt = row.ClearedAt!.Value.ToString("o"),
            RequiresCompleteOnServer = false,
            IsRewarded = true,
            Rewarded = rewarded,
        });
    }

    public sealed class CompleteGroupBody { public int Group { get; set; } }

    /// <summary>GET <c>api/objectives/v1/myprogress</c> +
    /// <c>api/players/v2/objectives</c> — the watch's daily/weekly
    /// objectives checklist. Returns
    /// <c>{Objectives: [...], ObjectiveGroups: [...]}</c> per
    /// <c>Objectives.MyProgress.Deserialize</c> (RVA 0x14510A0).
    /// Empty lists are acceptable; both keys must be present.</summary>
    [HttpGet("api/objectives/v1/myprogress")]
    [HttpGet("api/players/v2/objectives")]
    public async Task<IActionResult> MyObjectiveProgress()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me)
            return Ok(new { Objectives = Array.Empty<object>(), ObjectiveGroups = Array.Empty<object>() });

        var rows = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me)
            .ToListAsync();

        var rewardKeys = rows
            .Where(o => o.Key.StartsWith("reward:"))
            .Select(o => o.Key["reward:".Length..])
            .ToHashSet(StringComparer.Ordinal);

        var objectives = rows
            .Where(o => o.Key.StartsWith("obj:"))
            .Select(o =>
            {
                var (grp, idx) = ParseObjectiveKey(o.Key);
                // 2020.12 NMEKJOABIFC.PPGFHEDFBEA reads Index, Group, Progress,
                // VisualProgress, IsCompleted, HasClaimedReward (NOT IsRewarded).
                // Missing HasClaimedReward → KeyNotFoundException → "Couldn't
                // download progress data" → BootSequence kick to LogoutScene.
                return new
                {
                    Index = idx,
                    Group = grp,
                    Progress = o.IsCompleted ? 1f : 0f,
                    VisualProgress = o.IsCompleted ? 1f : 0f,
                    IsCompleted = o.IsCompleted,
                    HasClaimedReward = rewardKeys.Contains(o.Key),
                };
            });

        var groups = rows
            .Where(o => o.Key.StartsWith("group:"))
            .Select(o => new
            {
                Group = int.TryParse(o.Key["group:".Length..], out var g) ? g : 0,
                IsCompleted = o.IsCompleted,
                ClearedAt = o.ClearedAt ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RequiresCompleteOnServer = false,
                IsRewarded = rewardKeys.Contains(o.Key),
            });

        return Ok(new { Objectives = objectives, ObjectiveGroups = groups });
    }

    /// <summary>POST <c>api/objectives/v1/cleargroup</c> — flips a
    /// group's <c>IsCompleted</c> flag, used by the checklist's
    /// "claim group reward" button. Response is a single
    /// <c>ObjectiveGroupProgress</c> with Group/IsCompleted/ClearedAt
    /// required keys; <c>RequiresCompleteOnServer</c> is optional.</summary>
    [HttpPost("api/objectives/v1/cleargroup")]
    public async Task<IActionResult> ClearGroup([FromForm(Name = "group")] int? group)
    {
        var pid = this.CurrentPlayerId();
        var key = $"group:{group ?? 0}";
        var now = DateTime.UtcNow;

        if (pid is long me)
        {
            var row = await db.ObjectiveProgress
                .FirstOrDefaultAsync(o => o.PlayerId == me && o.Key == key);
            if (row is null)
            {
                row = new ObjectiveProgressEntity { PlayerId = me, Key = key };
                db.ObjectiveProgress.Add(row);
            }
            row.IsCompleted = true;
            row.ClearedAt = now;
            await db.SaveChangesAsync();
            await GrantObjectiveGroupRewardAsync(me, group ?? 0);
        }

        return Ok(new
        {
            Group = group ?? 0,
            IsCompleted = true,
            ClearedAt = now.ToString("o"),
            RequiresCompleteOnServer = false,
            IsRewarded = true,
        });
    }

    /// <summary>POST <c>api/players/v1/objectives</c> +
    /// <c>api/players/v2/objectives</c> — batched ProgressionManager
    /// completion records (the watch's
    /// <c>ACJLCBNBJDK.EIFPPIPOIHB(List&lt;PIHHFPDKFAG&gt;)</c>; body is a
    /// JSON array of <c>{objectiveType: int, additionalXp: int,
    /// inParty: bool}</c>). Grants XP + RecCenter tokens via
    /// <see cref="RewardForObjectiveType"/> and dedupes per UTC day
    /// through <see cref="GrantRewardOnceAsync"/>, so a re-submission of
    /// the same objective on the same day is a no-op. Returns the
    /// granted XP / currency + post-grant balance so the watch UI can
    /// refresh without a follow-up GET.</summary>
    [HttpPost("api/players/v1/objectives")]
    [HttpPost("api/players/v2/objectives")]
    [Authorize]
    public async Task<IActionResult> CompletePlayerObjectives([FromBody] List<PlayerObjectiveCompletionRecord>? records)
    {
        var pid = Me;
        var grantedXp = 0;
        var grantedTokens = 0;
        foreach (var record in records ?? new())
        {
            var (xp, tokens, reason) = RewardForObjectiveType(record.ObjectiveType, record.AdditionalXp);
            if (xp <= 0 && tokens <= 0) continue;
            if (await GrantRewardOnceAsync(pid, $"{reason}:{DateTime.UtcNow:yyyyMMdd}", xp, tokens, reason))
            {
                grantedXp += xp;
                grantedTokens += tokens;
            }
        }

        var balance = await level.GetBalanceAsync(pid, RecCenterTokens);
        return Ok(new
        {
            GrantedXp = grantedXp,
            GrantedCurrency = grantedTokens,
            CurrencyType = RecCenterTokens,
            Balance = balance,
            BalanceType = 0,
        });
    }

    public sealed class PlayerObjectiveCompletionRecord
    {
        public int ObjectiveType { get; set; }
        public bool InParty { get; set; }
        public int AdditionalXp { get; set; }
    }

    private static (int Group, int Index) ParseObjectiveKey(string key)
    {
        const string prefix = "obj:";
        if (!key.StartsWith(prefix)) return (-1, -1);
        var rest = key[prefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0) return (-1, -1);
        var grp = int.TryParse(rest[..colon], out var g) ? g : -1;
        var idx = int.TryParse(rest[(colon + 1)..], out var i) ? i : -1;
        return (grp, idx);
    }

    private static int CurrentChallengeMapId() =>
        (int)((DateTime.UtcNow - new DateTime(2020, 1, 1)).Days / 7 % 52 + 1);

    private static string ChallengeProgressKey(int mapId, int index) => $"challenge:{mapId}:{index}";

    private static int ChallengeId(int mapId, int index) => mapId * 10 + index;

    private static int ChallengeIndex(int mapId, int challengeId, int challengeCount)
    {
        if (challengeId >= mapId * 10 && challengeId < mapId * 10 + 10)
            return Math.Clamp(challengeId - mapId * 10, 0, challengeCount - 1);

        return Math.Clamp(Math.Abs(challengeId % 10), 0, challengeCount - 1);
    }

    private async Task<UpdateChallengeRequest> ReadChallengeRequestAsync(UpdateChallengeRequest formReq)
    {
        if (Request.HasFormContentType || formReq.Id != 0 || formReq.ChallengeId != 0 || formReq.ChallengeMapId != 0)
            return formReq;

        try
        {
            var body = await System.Text.Json.JsonSerializer.DeserializeAsync<UpdateChallengeRequest>(
                Request.Body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return body ?? formReq;
        }
        catch
        {
            return formReq;
        }
    }

    private async Task<bool> GrantObjectiveGroupRewardAsync(long playerId, int group)
    {
        var (xp, tokens, name) = group switch
        {
            0 => (150, 150, "daily-objectives"),
            1 => (100, 100, "new-player-punch-card"),
            2 => (75, 75, "orientation-checklist"),
            _ => (50, 50, $"objective-group-{group}"),
        };
        return await GrantRewardOnceAsync(playerId, $"group:{group}", xp, tokens, name);
    }

    private static (int Xp, int Tokens, string Reason) RewardForObjectiveType(int objectiveType, int additionalXp)
    {
        var xp = Math.Max(additionalXp, 0);
        return objectiveType switch
        {
            10 or 11 or 12 => (Math.Max(xp, 25), 25, $"daily-objective:{objectiveType}"),
            13 => (Math.Max(xp, 150), 150, "all-daily-objectives"),
            14 => (Math.Max(xp, 50), 50, "complete-any-daily"),
            15 => (Math.Max(xp, 75), 75, "complete-any-weekly"),
            25 => (Math.Max(xp, 25), 25, "nux-punchcard-objective"),
            26 => (Math.Max(xp, 100), 100, "nux-all-punchcard-objectives"),
            _ => (xp, 0, $"objective:{objectiveType}"),
        };
    }

    private async Task<bool> IsRewardedAsync(long playerId, string rewardKey) =>
        await db.ObjectiveProgress.AnyAsync(o =>
            o.PlayerId == playerId && o.Key == $"reward:{rewardKey}" && o.IsCompleted);

    private async Task<bool> GrantRewardOnceAsync(long playerId, string rewardKey, int xp, int tokens, string reason)
    {
        var sentinelKey = $"reward:{rewardKey}";
        var exists = await db.ObjectiveProgress.AnyAsync(o =>
            o.PlayerId == playerId && o.Key == sentinelKey && o.IsCompleted);
        if (exists) return false;

        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = playerId,
            Key = sentinelKey,
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        if (xp > 0) await level.AwardXpAsync(playerId, xp, reason);
        if (tokens > 0) await level.GrantCurrencyAsync(playerId, RecCenterTokens, tokens, reason);
        return true;
    }

    private static object BalanceUpdateResponse(long balance, int currencyType, int delta, int balanceAddType) => new
    {
        Balance = balance,
        CurrencyType = currencyType,
        BalanceType = 0,
        BalanceUpdates = new[]
        {
            new
            {
                UpdateResponse = 0,
                Data = delta <= 0
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new
                        {
                            BalanceAddType = balanceAddType,
                            BaseAward = delta,
                            BonusAward = 0,
                            RateLimit = 0,
                            CurrentCount = 1,
                            Total = delta,
                            BalanceType = 0,
                            BalanceInGiftBox = false,
                        },
                    },
            },
        },
    };
}
