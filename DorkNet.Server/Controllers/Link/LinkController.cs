using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Binding;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Controllers.Link;

/// <summary>
/// The Link host (<c>GJDLNNLKDIJ.Link</c> = host ordinal <c>11</c>) share-code
/// service — <c>RecNet.Runtime/CKBKHENHCAN</c>. Every "share this" surface in
/// the 2023 client funnels through here:
/// <c>RecRoom.Sharing.ActionCode</c> subclasses (FriendCode, ReferralCode,
/// MeetupCode, ClubCode, PlayerEventCode, RoomCode, InfluencerCode, PhotoCode)
/// create an <b>action link</b> — a short code the player reads out or pastes,
/// which the receiving client redeems — while <c>LinkManager</c> stores the
/// larger opaque payload behind a share URL as a <b>data link</b>.
///
/// <para>The five routes and their verbs, read off the dispatch call in
/// <c>CKBKHENHCAN.txt</c> (the shape is
/// <c>(this, verb=rdx, host=r8, path=r9, body=stack)</c>, verb being a
/// <c>BestHTTP.HTTPMethods</c> ordinal — GET=0, POST=2, PUT=3):
/// <list type="bullet">
///   <item><c>GET actionlink/{code}</c> — <c>CGIEJDIHJBD</c> (:635), verb
///     <c>045 Move rdx, 0</c> (:793), host <c>050 LoadAddress r8, [rdx+11]</c>
///     (:798), literal <c>"actionlink/"</c> (:786).</item>
///   <item><c>POST actionlink</c> — <c>PCNPNDPAFLK</c> (:860), verb
///     <c>106 Move rdx, 2</c> (:1226), host <c>105 Move r8, 11</c> (:1225),
///     literal <c>"actionlink"</c> (:1224).</item>
///   <item><c>POST actionlink/{code}/consume</c> — <c>MKAKECJOKDG</c> (:1380),
///     verb <c>077 Move rdx, 2</c> (:1675), path built by
///     <c>String.Concat("actionlink/", WebUtility.UrlEncode(code), "/consume")</c>
///     (:1665-1669).</item>
///   <item><c>PUT datalink</c> — <c>ONPLPBLKACA</c> (:419), verb
///     <c>033 Move rdx, 3</c> (:560), literal <c>"datalink"</c> (:558).</item>
///   <item><c>GET datalink/{code}</c> — <c>KEHDDPCHPGK</c> (:194), verb
///     <c>045 Move rdx, 0</c> (:352), literal <c>"datalink/"</c> (:345).</item>
/// </list>
/// All five were unrouted, so every share/redeem flow 404'd.</para>
///
/// <para><b>Storage.</b> Codes live in <c>PlayerSettings</c> — the server's
/// existing general-purpose per-player key/value table, already used for
/// ban-appeal codes (<c>CompatibilityFeatureController</c>) and external
/// friend invites (<c>ExternalFriendInviteController</c>) — under
/// <c>actionlink:{CODE}</c> / <c>datalink:{CODE}</c> keys on the creator's
/// row. That keeps codes durable across restarts without a schema change; the
/// cost is the 1024-character <see cref="PlayerSettingEntity.Value"/> cap,
/// which is enforced explicitly below rather than left to trip a Postgres
/// <c>varchar(1024)</c> constraint at <c>SaveChanges</c>.</para>
/// </summary>
[ApiController]
public class LinkController(DorkNetDbContext db, ILogger<LinkController> logger) : ControllerBase
{
    private const string ActionPrefix = "actionlink:";
    private const string DataPrefix = "datalink:";

    /// <summary>Unambiguous uppercase alphabet — no O/0/I/1 — because action
    /// codes are read aloud and re-typed into
    /// <c>ActionCodeConsumptionModel</c>'s input field.</summary>
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary><c>PlayerSettingEntity.Value</c> is <c>[MaxLength(1024)]</c>.</summary>
    private const int ValueLimit = 1024;

    /// <summary><c>RecRoom.Sharing.CBKKGBIJHAL</c> (2023.06.21 dump.cs:1248232):
    /// Unknown=-1, Friend=0, Referral=1, Meetup=2, Club=3, PlayerEvent=4,
    /// Room=5, Influencer=6, Photo=7. Confirmed live at the consume call sites
    /// — RoomCode passes <c>030 Move r8, 5</c>
    /// (RoomCode_NestedType_IBLIEDEHMPI.txt:175) and ClubCode passes
    /// <c>029 Move r8, 3</c> (ClubCode_NestedType_EAGCMJOEANN.txt:85).</summary>
    private const int CodeTypeReferral = 1;

    // ── actionlink ────────────────────────────────────────────────────────────

    /// <summary>POST <c>actionlink</c> — mint a share code.
    ///
    /// <para>Form fields are added one at a time by
    /// <c>BNDIAONDFFF.AFGEDDANEKP</c>: <c>data</c> (:1234), <c>validHours</c>
    /// (:1249), <c>maxCount</c> (:1261, boxed <c>Nullable&lt;Int32&gt;</c> —
    /// omitted when null), <c>codeType</c> (:1273) and <c>extraDataId</c>
    /// (:1287, boxed <c>Nullable&lt;Int64&gt;</c>). The explicit
    /// <c>[ModelBinder]</c> is mandatory: <c>[ApiController]</c> would
    /// otherwise re-infer <c>[FromBody]</c> and 415 the form post.</para>
    ///
    /// <para>The issuing method is
    /// <c>FGLDKEJLAKB&lt;System.String&gt; PCNPNDPAFLK(...)</c> (:860), so the
    /// body is a <b>bare JSON string</b> — the code itself. <c>Ok(string)</c>
    /// would go out as unquoted <c>text/plain</c> via
    /// <c>StringOutputFormatter</c>, which the client's String formatter
    /// cannot read, hence the explicit <c>Content(JsonSerializer.Serialize(…))</c>
    /// (same pattern as the ban-appeal code handler).</para></summary>
    [HttpPost("/actionlink")]
    [Authorize]
    public async Task<IActionResult> ActionLinkCreate(
        [ModelBinder(typeof(FormOrJsonModelBinder))] ActionLinkCreateRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        var data = req.Data ?? string.Empty;

        var payload = new ActionLinkPayload
        {
            Data = data,
            ExpiresAt = req.ValidHours > 0
                ? (DateTime?)DateTime.UtcNow.AddHours(req.ValidHours)
                : null,
            MaxCount = req.MaxCount is > 0 ? req.MaxCount : (int?)null,
            Uses = 0,
            CodeType = req.CodeType,
            ExtraDataId = req.ExtraDataId,
        };

        var encoded = JsonSerializer.Serialize(payload);
        if (encoded.Length > ValueLimit)
        {
            logger.LogWarning(
                "[link] actionlink payload too large player={PlayerId} codeType={CodeType} bytes={Length} limit={Limit}",
                me, req.CodeType, encoded.Length, ValueLimit);
            return BadRequest(new { Error = "Action link data is too large to store." });
        }

        var code = await ReserveCodeAsync(ActionPrefix, 8);
        if (code is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = me,
            Key = ActionPrefix + code,
            Value = encoded,
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[link] actionlink created code={Code} player={PlayerId} codeType={CodeType} validHours={ValidHours} maxCount={MaxCount}",
            code, me, req.CodeType, req.ValidHours, req.MaxCount);
        return Content(JsonSerializer.Serialize(code), "application/json");
    }

    /// <summary>GET <c>actionlink/{code}</c> — peek at a code without spending
    /// a use.
    ///
    /// <para>Anonymous on purpose: <c>ActionCode.ModifyPreAuthLaunchTarget</c>
    /// resolves an inbound deep link during <c>BootSequence</c>, i.e. before
    /// the player has a token.</para>
    ///
    /// <para>Return type <c>FGLDKEJLAKB&lt;NBAFGFFIOLK&gt;</c> (:635); the
    /// generated formatter <c>DPIECEMDKPL</c> registers exactly three members —
    /// <c>CreatorPlayerId</c> (:267, Int32), <c>Data</c> (:294, String),
    /// <c>IsValid</c> (:310, Boolean) — each also in camelCase (:278/:302/:318)
    /// and lowercase (:286/:326), so the server's camelCase anonymous-object
    /// serialization binds. An unknown code answers 200 with
    /// <c>IsValid:false</c> rather than 404 so the client always gets a body it
    /// can parse instead of faulting the promise.</para></summary>
    [HttpGet("/actionlink/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> ActionLinkGet(string code)
    {
        var row = await FindAsync(ActionPrefix, code);
        var payload = Decode(row);
        if (row is null || payload is null) return Ok(ActionLinkResult(0, string.Empty, false));

        var valid = IsUsable(payload);
        return Ok(ActionLinkResult((int)row.PlayerId, valid ? payload.Data : string.Empty, valid));
    }

    /// <summary>POST <c>actionlink/{code}/consume</c> — redeem a code.
    ///
    /// <para>Form fields: <c>id</c> (:1684 — the code again), <c>validHours</c>
    /// (:1692, <c>Nullable&lt;Int32&gt;</c>), <c>codeType</c> (:1720),
    /// <c>newPlayer</c> (:1732) and <c>newInstall</c> (:1745). Response shape
    /// is the same <c>NBAFGFFIOLK</c> as the GET (:1380).</para>
    ///
    /// <para><c>validHours</c> here is the creating <c>ActionCode</c>'s
    /// <c>Configuration.AutoRenewHours</c> — the call sites load it immediately
    /// before dispatch (<c>043 Call Configuration.get_AutoRenewHours</c>,
    /// RoomCode_NestedType_IBLIEDEHMPI.txt:168; ClubCode_NestedType_EAGCMJOEANN.txt:78)
    /// and it is <c>null</c> for codes whose <c>AutoRenew</c> is off. So a
    /// present value means "this code renews on use": the expiry is pushed out
    /// that many hours instead of the code burning down.</para>
    ///
    /// <para><c>codeType</c> is checked against the stored one — a Room code
    /// (5) redeemed through the Club (3) flow would otherwise hand the club
    /// handler a room payload to parse.</para>
    ///
    /// <para>Referral codes are the one type with no other server touchpoint:
    /// <c>ReferralCode</c> has no HTTP routes of its own, and
    /// <c>IncentivizedReferralsController</c> counts
    /// <c>referral:credited:{inviterId}:{inviteeId}</c> rows that nothing else
    /// writes. Redeeming one here as a new player is what credits the
    /// inviter.</para>
    ///
    /// <para>Anonymous like the GET — the consume can fire from the pre-auth
    /// launch-target path; the caller's id is only needed for referral
    /// crediting.</para></summary>
    [HttpPost("/actionlink/{code}/consume")]
    [AllowAnonymous]
    public async Task<IActionResult> ActionLinkConsume(
        string code,
        [ModelBinder(typeof(FormOrJsonModelBinder))] ActionLinkConsumeRequest req)
    {
        // The client sends the code in the path AND as the `id` field; prefer
        // the path, fall back to the field.
        var wanted = string.IsNullOrWhiteSpace(code) ? req.Id : code;
        var row = await FindAsync(ActionPrefix, wanted);
        var payload = Decode(row);
        if (row is null || payload is null)
        {
            logger.LogInformation("[link] actionlink consume miss code={Code}", Normalize(wanted));
            return Ok(ActionLinkResult(0, string.Empty, false));
        }

        if (!IsUsable(payload) || payload.CodeType != req.CodeType)
        {
            logger.LogInformation(
                "[link] actionlink consume rejected code={Code} storedType={StoredType} askedType={AskedType} uses={Uses}/{MaxCount} expires={Expires}",
                Normalize(wanted), payload.CodeType, req.CodeType, payload.Uses, payload.MaxCount, payload.ExpiresAt);
            return Ok(ActionLinkResult((int)row.PlayerId, string.Empty, false));
        }

        payload.Uses++;
        // AutoRenewHours present ⇒ the code renews rather than ages out.
        // Clamped: this route is anonymous and the value is client-supplied,
        // so an unbounded renewal would let anyone holding the code extend
        // its life indefinitely past what the creator chose.
        if (req.ValidHours is > 0)
            payload.ExpiresAt = DateTime.UtcNow.AddHours(Math.Min(req.ValidHours.Value, 720));

        var encoded = JsonSerializer.Serialize(payload);
        if (encoded.Length <= ValueLimit) row.Value = encoded;

        var consumer = this.CurrentPlayerId();
        await CreditReferralAsync(payload, row.PlayerId, consumer, req.NewPlayer);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[link] actionlink consumed code={Code} creator={Creator} consumer={Consumer} type={CodeType} uses={Uses} newPlayer={NewPlayer} newInstall={NewInstall}",
            Normalize(wanted), row.PlayerId, consumer ?? 0, payload.CodeType, payload.Uses, req.NewPlayer, req.NewInstall);
        return Ok(ActionLinkResult((int)row.PlayerId, payload.Data, true));
    }

    // ── datalink ──────────────────────────────────────────────────────────────

    /// <summary>PUT <c>datalink</c> — stash the payload behind a share URL.
    ///
    /// <para>Single form field <c>data</c> (:569, added right after the
    /// <c>033 Move rdx, 3</c> PUT dispatch at :560). Return type is
    /// <c>FGLDKEJLAKB&lt;System.String&gt;</c> (:419) — a bare JSON string
    /// holding the generated code, so the same explicit
    /// <c>Content(JsonSerializer.Serialize(…), "application/json")</c> as the
    /// action-link create.</para>
    ///
    /// <para>Codes are 10 characters here rather than 8: nobody types a
    /// datalink code, it only ever travels inside a URL, so the extra entropy
    /// is free.</para></summary>
    [HttpPut("/datalink")]
    [Authorize]
    public async Task<IActionResult> DataLinkCreate(
        [ModelBinder(typeof(FormOrJsonModelBinder))] DataLinkCreateRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        var data = req.Data ?? string.Empty;
        if (data.Length > ValueLimit)
        {
            logger.LogWarning(
                "[link] datalink payload too large player={PlayerId} bytes={Length} limit={Limit}",
                me, data.Length, ValueLimit);
            return BadRequest(new { Error = "Data link payload is too large to store." });
        }

        var code = await ReserveCodeAsync(DataPrefix, 10);
        if (code is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = me,
            Key = DataPrefix + code,
            Value = data,
        });
        await db.SaveChangesAsync();

        logger.LogInformation("[link] datalink created code={Code} player={PlayerId} bytes={Length}",
            code, me, data.Length);
        return Content(JsonSerializer.Serialize(code), "application/json");
    }

    /// <summary>GET <c>datalink/{code}</c> — resolve a share URL back to its
    /// payload.
    ///
    /// <para>Return type <c>FGLDKEJLAKB&lt;LBBKBMPAEIO&gt;</c> (:194) whose
    /// generated formatter <c>IKBPJACLIHH</c> registers a single member,
    /// <c>Data</c> (:139) — plus the <c>data</c> (:150) alias the server's
    /// camelCase serializer emits. Anonymous, like the action-link read: this
    /// is resolved from a cold-boot deep link. An unknown code returns an empty
    /// <c>Data</c> rather than 404 so the strict reader still gets an
    /// object.</para></summary>
    [HttpGet("/datalink/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> DataLinkGet(string code)
    {
        var row = await FindAsync(DataPrefix, code);
        return Ok(new { Data = row?.Value ?? string.Empty });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Codes are stored uppercase so a player who types their share
    /// code in lowercase still redeems it.</summary>
    private static string Normalize(string? code)
        => (code ?? string.Empty).Trim().ToUpperInvariant();

    private Task<PlayerSettingEntity?> FindAsync(string prefix, string? code)
    {
        var normalized = Normalize(code);
        if (normalized.Length == 0) return Task.FromResult<PlayerSettingEntity?>(null);
        var key = prefix + normalized;
        return db.PlayerSettings.FirstOrDefaultAsync(s => s.Key == key);
    }

    /// <summary>Generates a code that is free across every player's rows (the
    /// unique index is on <c>(PlayerId, Key)</c>, but a code has to be globally
    /// unique because lookups are by key alone). Retries a handful of times
    /// before giving up rather than colliding on insert.</summary>
    private async Task<string?> ReserveCodeAsync(string prefix, int length)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var code = NewCode(length);
            var key = prefix + code;
            if (!await db.PlayerSettings.AnyAsync(s => s.Key == key)) return code;
        }
        logger.LogError("[link] exhausted code-generation attempts for prefix={Prefix}", prefix);
        return null;
    }

    private static string NewCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    private ActionLinkPayload? Decode(PlayerSettingEntity? row)
    {
        if (row is null) return null;
        try
        {
            return JsonSerializer.Deserialize<ActionLinkPayload>(row.Value);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[link] unreadable action-link row id={RowId} key={Key}", row.Id, row.Key);
            return null;
        }
    }

    private static bool IsUsable(ActionLinkPayload payload)
    {
        if (payload.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow) return false;
        if (payload.MaxCount is { } max && payload.Uses >= max) return false;
        return true;
    }

    /// <summary>Records "<c>consumer</c> was referred by <c>creator</c>" in the
    /// shape <c>IncentivizedReferralsController</c> already reads
    /// (<c>referral:credited:{inviterId}:{inviteeId}</c>, counted by key prefix
    /// regardless of which row owns it — stored on the invitee, matching the
    /// <c>externalinvite:redeemed:*</c> convention). Only fires for referral
    /// codes redeemed by a signed-in, brand-new player who is not the
    /// creator.</summary>
    private async Task CreditReferralAsync(
        ActionLinkPayload payload, long creatorId, long? consumerId, bool newPlayer)
    {
        if (payload.CodeType != CodeTypeReferral) return;
        if (!newPlayer) return;
        if (consumerId is not { } consumer || consumer == creatorId) return;

        var key = $"referral:credited:{creatorId}:{consumer}";
        if (await db.PlayerSettings.AnyAsync(s => s.PlayerId == consumer && s.Key == key)) return;

        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = consumer,
            Key = key,
            Value = DateTime.UtcNow.ToString("O"),
        });
        logger.LogInformation("[link] referral credited inviter={Inviter} invitee={Invitee}", creatorId, consumer);
    }

    private static object ActionLinkResult(int creatorPlayerId, string data, bool isValid) => new
    {
        CreatorPlayerId = creatorPlayerId,
        Data = data,
        IsValid = isValid,
    };

    /// <summary>Everything the client told us at create time, packed into the
    /// single <c>PlayerSettings.Value</c> string. Keys are one character each
    /// to leave as much of the 1024-character budget as possible for
    /// <see cref="Data"/>.</summary>
    private sealed class ActionLinkPayload
    {
        [JsonPropertyName("d")] public string Data { get; set; } = string.Empty;
        [JsonPropertyName("e")] public DateTime? ExpiresAt { get; set; }
        [JsonPropertyName("m")] public int? MaxCount { get; set; }
        [JsonPropertyName("u")] public int Uses { get; set; }
        [JsonPropertyName("t")] public int CodeType { get; set; }
        [JsonPropertyName("x")] public long? ExtraDataId { get; set; }
    }
}

/// <summary>Form fields of <c>POST actionlink</c> — CKBKHENHCAN.txt:1234
/// (<c>data</c>), :1249 (<c>validHours</c>), :1261 (<c>maxCount</c>), :1273
/// (<c>codeType</c>), :1287 (<c>extraDataId</c>).</summary>
public sealed class ActionLinkCreateRequest
{
    public string? Data { get; set; }
    public int ValidHours { get; set; }
    public int? MaxCount { get; set; }
    public int CodeType { get; set; }
    public long? ExtraDataId { get; set; }
}

/// <summary>Form fields of <c>POST actionlink/{code}/consume</c> —
/// CKBKHENHCAN.txt:1684 (<c>id</c>), :1692 (<c>validHours</c>), :1720
/// (<c>codeType</c>), :1732 (<c>newPlayer</c>), :1745
/// (<c>newInstall</c>).</summary>
public sealed class ActionLinkConsumeRequest
{
    public string? Id { get; set; }
    public int? ValidHours { get; set; }
    public int CodeType { get; set; }
    public bool NewPlayer { get; set; }
    public bool NewInstall { get; set; }
}

/// <summary>Sole form field of <c>PUT datalink</c> — CKBKHENHCAN.txt:569.</summary>
public sealed class DataLinkCreateRequest
{
    public string? Data { get; set; }
}
