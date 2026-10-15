using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Writes whose client method returns the untyped promise (<c>LDGADANDBIO</c>)
/// but whose reply is still parsed — as <c>DJMHAFPGLLN</c>, the
/// <c>{Success, Error}</c> RecNetResult — by <c>BFDGEKKNAPB.PEFKBAEAOPD</c>
/// (ExpandRecNetResult, RecNet.Runtime/BFDGEKKNAPB.txt:353).
///
/// The failure modes are silent server-side: an empty 200 parses to null and
/// null-derefs, so the player sees "Failed to …" after the change was applied;
/// a body without <c>Success</c> parses as a failure whose <c>Error</c> is null,
/// and <c>Promise.Rejected(null)</c> throws "Error message cannot be null".
/// So the assertion that matters is the body, read the way the client's
/// formatter reads it (LMCCNLLHLCJ.txt: Success/success, Error/error).
/// </summary>
public sealed class RecNetResultEnvelopeTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RecNetResultEnvelopeTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Club_member_writes_answer_a_success_result_not_an_empty_body()
    {
        using var setup = Client("clubs");
        var owner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var joiner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var clubId = await SeedOpenClubAsync(owner.PlayerId);

        using var joinerClient = Client("clubs", joiner);
        // requesttojoin is verb 3 (PUT) through IKMMOCKDKAF.JNFEEDLCGHH, form-encoded.
        using var join = await joinerClient.PutAsync(
            $"/club/{clubId}/members/requesttojoin", new FormUrlEncodedContent([]));
        await AssertRecNetSuccessAsync(join, "requesttojoin");

        using var leave = await joinerClient.PostAsync(
            $"/club/{clubId}/members/leave", new FormUrlEncodedContent([]));
        await AssertRecNetSuccessAsync(leave, "leave");

        using var clearHome = await joinerClient.DeleteAsync("/club/home/me");
        await AssertRecNetSuccessAsync(clearHome, "DELETE club/home/me");

        using var ownerClient = Client("clubs", owner);
        using var delete = await ownerClient.DeleteAsync($"/club/{clubId}");
        await AssertRecNetSuccessAsync(delete, "DELETE club/{id}");
    }

    [Fact]
    public async Task Read_markers_and_privacy_toggle_answer_a_success_result()
    {
        using var setup = Client("api");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = Client("api", player);

        using var commentRead = await client.PutAsync("/comments/read/1/1", content: null);
        await AssertRecNetSuccessAsync(commentRead, "PUT comments/read");

        using var accounts = Client("accounts", player);
        using var privacy = await accounts.PutAsync(
            "/accountprivacysettings/recenthistoryvisibility",
            new FormUrlEncodedContent([new("isRecentHistoryVisible", "false")]));
        await AssertRecNetSuccessAsync(privacy, "PUT recenthistoryvisibility");
    }

    [Fact]
    public async Task A_refused_warning_carries_an_error_message()
    {
        using var setup = Client("api");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = Client("api", player);

        using var response = await client.PostAsync("/api/playerwarnings",
            new FormUrlEncodedContent([new("WarnedPlayerId", player.PlayerId.ToString())]));
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(ReadSuccess(root));
        // Without Error the client's Promise.Rejected(null) throws instead of
        // surfacing the refusal.
        Assert.False(string.IsNullOrEmpty(ReadError(root)));
    }

    [Fact]
    public async Task Login_token_is_a_bare_string_redeemed_once_for_its_own_account()
    {
        using var setup = Client("auth");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var auth = Client("auth", player);

        using var mint = await auth.PostAsync("/account/me/createlogintoken", content: null);
        var raw = await mint.Content.ReadAsStringAsync();
        Assert.True(mint.IsSuccessStatusCode, raw);
        // FGLDKEJLAKB<String> LGKLFFPACGL(): a JSON string, not an object.
        var token = JsonSerializer.Deserialize<string>(raw);
        Assert.False(string.IsNullOrEmpty(token));

        using var anon = Client("auth");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "login_token",
            ["account_id"] = player.PlayerId.ToString(),
            ["login_token"] = token!,
        };
        using var redeemed = await anon.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        var redeemedBody = await redeemed.Content.ReadAsStringAsync();
        Assert.True(redeemed.IsSuccessStatusCode, redeemedBody);
        Assert.Equal(player.PlayerId, await SubjectOfAsync(redeemedBody));

        using var again = await anon.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Device_code_poll_waits_for_remoteauth_approval_then_signs_in_the_approver()
    {
        using var anon = Client("auth");
        var approver = await GameClientSessionFactory.CreateAsync(anon, _factory.ApexDomain);

        using var start = await anon.PostAsync("/connect/deviceauthorization", content: null);
        var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement;
        var deviceCode = started.GetProperty("device_code").GetString()!;
        var userCode = started.GetProperty("user_code").GetString()!;

        var poll = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = deviceCode,
        };

        // Before approval the client must be told to keep polling: a 400
        // whose error is authorization_pending (JAEKFNKDNCP.txt:97-107).
        using (var pending = await anon.PostAsync("/connect/token", new FormUrlEncodedContent(poll)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, pending.StatusCode);
            var body = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("authorization_pending", body.GetProperty("error").GetString());
        }

        using var approverClient = Client("auth", approver);
        using (var bad = await approverClient.PostAsync("/account/me/remoteauth",
                   new FormUrlEncodedContent([new("code", "NOPE0000")])))
        {
            var root = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement;
            Assert.False(ReadSuccess(root));
            Assert.False(string.IsNullOrEmpty(ReadError(root)));
        }

        using var approve = await approverClient.PostAsync("/account/me/remoteauth",
            new FormUrlEncodedContent([new("code", userCode.ToLowerInvariant())]));
        await AssertRecNetSuccessAsync(approve, "remoteauth");

        using var granted = await anon.PostAsync("/connect/token", new FormUrlEncodedContent(poll));
        var grantedBody = await granted.Content.ReadAsStringAsync();
        Assert.True(granted.IsSuccessStatusCode, grantedBody);
        Assert.Equal(approver.PlayerId, await SubjectOfAsync(grantedBody));

        // Single use.
        using var reused = await anon.PostAsync("/connect/token", new FormUrlEncodedContent(poll));
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task Gameconfig_carries_the_play_menu_carousel_ordering_in_the_parsed_format()
    {
        using var client = Client("api");
        var rows = JsonDocument.Parse(await client.GetStringAsync("/api/gameconfigs/v1/all")).RootElement;
        var row = rows.EnumerateArray().FirstOrDefault(r =>
            string.Equals(Prop(r, "Key"), "PlayMenuCarouselOrdering_1", StringComparison.Ordinal));
        Assert.NotEqual(JsonValueKind.Undefined, row.ValueKind);

        // PlayMenuCarouselModel.TryGetDisplayPriorityMap: Split(';'), then each
        // entry Split('=') must give exactly 2 parts with an Int32 priority, or
        // the entry is logged as "Failed to parse priority mapping".
        var entries = Prop(row, "Value")!.Split(';');
        Assert.All(entries, e =>
        {
            var parts = e.Split('=');
            Assert.Equal(2, parts.Length);
            Assert.True(int.TryParse(parts[1].Trim(), out _), e);
        });
        Assert.Contains(entries, e => e.StartsWith("hotrooms=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_strings_download_answers_an_empty_table()
    {
        using var client = Client("strings");
        using var response = await client.GetAsync("/strings?locale=en-US&scopePrefix=client.&eTag=");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(body).RootElement.ValueKind);
    }

    /// <summary>PUT playerevents/v2/{id}/multiinstance sends the two broadcast
    /// permission fields as boxed enums, i.e. member names (BNDIAONDFFF.txt:947
    /// falls back to Object.ToString). Parsing them as ints stored 0 every time.</summary>
    [Theory]
    [InlineData("RoomOwners", 256)]
    [InlineData("All", int.MaxValue)]
    [InlineData("None", 0)]
    [InlineData("256", 256)]
    [InlineData("roomowners, None", 256)]
    public void Broadcast_permission_names_parse_to_their_values(string raw, int expected) =>
        Assert.Equal(expected,
            DorkNet.Server.Controllers.API.PlayerEvents.PlayerEventsController.ParseBroadcastPermissions(raw));

    [Fact]
    public void Unknown_broadcast_permission_name_keeps_the_stored_value() =>
        Assert.Null(DorkNet.Server.Controllers.API.PlayerEvents.PlayerEventsController
            .ParseBroadcastPermissions("SomethingElse"));

    private async Task<long> SeedOpenClubAsync(long ownerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var club = new ClubEntity
        {
            Name = $"Env{Guid.NewGuid():N}"[..16],
            CreatorPlayerId = ownerId,
            Joinability = 0, // Open
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return club.Id;
    }

    private async Task<long> SubjectOfAsync(string tokenBody)
    {
        var access = JsonDocument.Parse(tokenBody).RootElement.GetProperty("access_token").GetString();
        using var client = Client("auth");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var info = JsonDocument.Parse(await client.GetStringAsync("/connect/userinfo")).RootElement;
        return long.Parse(info.GetProperty("sub").GetString()!);
    }

    private static async Task AssertRecNetSuccessAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what} -> {(int)response.StatusCode}: {body}");
        Assert.False(string.IsNullOrWhiteSpace(body), $"{what} answered an empty body");
        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(ReadSuccess(root), $"{what} body has no Success=true: {body}");
    }

    // The client formatter registers exactly these spellings.
    private static bool ReadSuccess(JsonElement root) =>
        (root.TryGetProperty("Success", out var s) || root.TryGetProperty("success", out s))
        && s.ValueKind == JsonValueKind.True;

    private static string? ReadError(JsonElement root) =>
        root.TryGetProperty("Error", out var e) || root.TryGetProperty("error", out e)
            ? e.ValueKind == JsonValueKind.String ? e.GetString() : null
            : null;

    private static string? Prop(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) || row.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out v)
            ? v.GetString()
            : null;

    private HttpClient Client(string host, GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
