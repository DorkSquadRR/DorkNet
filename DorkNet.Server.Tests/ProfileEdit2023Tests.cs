using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using DorkNet.Server.Controllers.Match;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Tests;

/// <summary>
/// In-game profile edits for the 2023-03-21 client. Every edit goes through
/// OPMAPIOEIFG.OBAGJBBIFGB → PUT "account/me/" + suffix with a single
/// urlencoded field whose name differs from the suffix (displayEmoji,
/// pronounFlags, identityFlags, imageName). None of these routes existed, so
/// every edit failed with "Failed to modify &lt;suffix&gt;", and no account
/// DTO emitted the fields, so nothing showed up for anyone. Online-status
/// visibility (PUT player/statusvisibility) was persisted but GET /player
/// always reported Public and never hid the player.
/// </summary>
public sealed class ProfileEdit2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public ProfileEdit2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Emoji_edit_persists_and_is_visible_to_other_players()
    {
        var (owner, viewer) = await TwoPlayersAsync();
        using var ownerClient = AccountsClient(owner);
        using var viewerClient = AccountsClient(viewer);

        await PutFormOkAsync(ownerClient, "/account/me/emoji", "displayEmoji", "🤪");

        var me = await GetJsonAsync(ownerClient, "/account/me");
        Assert.Equal("🤪", me.GetProperty("displayEmoji").GetString());

        var seen = await GetJsonAsync(viewerClient, $"/account/{owner.PlayerId}");
        Assert.Equal("🤪", seen.GetProperty("displayEmoji").GetString());

        // Empty clears it.
        await PutFormOkAsync(ownerClient, "/account/me/emoji", "displayEmoji", "");
        me = await GetJsonAsync(ownerClient, "/account/me");
        Assert.Equal("", me.GetProperty("displayEmoji").GetString());
    }

    [Fact]
    public async Task Emoji_edit_rejects_non_whitelisted_and_canonicalizes_variation_selector()
    {
        var (owner, _) = await TwoPlayersAsync();
        using var client = AccountsClient(owner);

        using (var bad = await PutFormAsync(client, "/account/me/emoji", "displayEmoji", "not-an-emoji"))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // "✌" without U+FE0F resolves to the list's "✌️".
        await PutFormOkAsync(client, "/account/me/emoji", "displayEmoji", "✌");
        var me = await GetJsonAsync(client, "/account/me");
        Assert.Equal("✌️", me.GetProperty("displayEmoji").GetString());
    }

    [Fact]
    public async Task Pronouns_identity_flags_and_banner_round_trip_on_every_account_read()
    {
        var (owner, viewer) = await TwoPlayersAsync();
        using var ownerClient = AccountsClient(owner);
        using var viewerClient = AccountsClient(viewer);

        await PutFormOkAsync(ownerClient, "/account/me/personalpronouns", "pronounFlags", "5");
        await PutFormOkAsync(ownerClient, "/account/me/identityflags", "identityFlags", "3");
        await PutFormOkAsync(ownerClient, "/account/me/bannerimage", "imageName", "banner-test.jpg");

        void AssertProfile(JsonElement a)
        {
            Assert.Equal(5, a.GetProperty("personalPronouns").GetInt32());
            Assert.Equal(3, a.GetProperty("identityFlags").GetInt32());
            Assert.Equal("banner-test.jpg", a.GetProperty("bannerImage").GetString());
        }

        AssertProfile(await GetJsonAsync(ownerClient, "/account/me"));
        AssertProfile(await GetJsonAsync(viewerClient, $"/account/{owner.PlayerId}"));

        var bulk = await GetJsonAsync(viewerClient, $"/account/bulk?id={owner.PlayerId}");
        AssertProfile(bulk.EnumerateArray().Single(a => a.GetProperty("accountId").GetInt64() == owner.PlayerId));
    }

    [Fact]
    public async Task Flag_edits_reject_non_numeric_values()
    {
        var (owner, _) = await TwoPlayersAsync();
        using var client = AccountsClient(owner);

        using (var r = await PutFormAsync(client, "/account/me/personalpronouns", "pronounFlags", "HeHim"))
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using (var r = await PutFormAsync(client, "/account/me/identityflags", "identityFlags", "-1"))
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Self_account_reports_username_changes_remaining()
    {
        var (owner, _) = await TwoPlayersAsync();
        using var client = AccountsClient(owner);
        var me = await GetJsonAsync(client, "/account/me");
        // Absent/0 makes the settings UI say no username changes are left.
        Assert.True(me.GetProperty("availableUsernameChanges").GetInt32() > 0);
    }

    [Fact]
    public async Task Bio_edit_round_trips_through_public_bio_lookup()
    {
        var (owner, viewer) = await TwoPlayersAsync();
        using var ownerClient = AccountsClient(owner);
        using var viewerClient = AccountsClient(viewer);

        await PutFormOkAsync(ownerClient, "/account/me/bio", "bio", "hello from the profile");
        var bio = await GetJsonAsync(viewerClient, $"/account/{owner.PlayerId}/bio");
        Assert.Equal("hello from the profile", bio.GetProperty("bio").GetString());
    }

    [Fact]
    public async Task Status_visibility_is_reported_to_self_and_hides_appear_offline_player()
    {
        var (owner, viewer) = await TwoPlayersAsync();
        PutOnline(owner.PlayerId);

        using var ownerMatch = MatchClient(owner);
        using var viewerMatch = MatchClient(viewer);

        // Baseline: Public → viewer sees owner online.
        var before = await PresenceOfAsync(viewerMatch, owner.PlayerId);
        Assert.True(before.GetProperty("isOnline").GetBoolean());

        await PutFormOkAsync(ownerMatch, "/player/statusvisibility", "statusVisibility", "3");

        // Self keeps its own choice (the dialog reads the cached self presence).
        var self = await PresenceOfAsync(ownerMatch, owner.PlayerId);
        Assert.Equal(3, self.GetProperty("statusVisibility").GetInt32());
        Assert.True(self.GetProperty("isOnline").GetBoolean());

        // Others see an offline player with no room.
        var other = await PresenceOfAsync(viewerMatch, owner.PlayerId);
        Assert.False(other.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("roomInstance").ValueKind);
    }

    [Fact]
    public async Task Friends_only_visibility_shows_friends_and_hides_strangers()
    {
        var (owner, friend) = await TwoPlayersAsync();
        using var setup = AccountsClient();
        var stranger = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        PutOnline(owner.PlayerId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Relationships.Add(new RelationshipEntity
            {
                RequesterId = owner.PlayerId,
                TargetId = friend.PlayerId,
                Status = RelationshipStatus.Friend,
            });
            await db.SaveChangesAsync();
        }

        using var ownerMatch = MatchClient(owner);
        await PutFormOkAsync(ownerMatch, "/player/statusvisibility", "statusVisibility", "1");

        using var friendMatch = MatchClient(friend);
        using var strangerMatch = MatchClient(stranger);
        Assert.True((await PresenceOfAsync(friendMatch, owner.PlayerId)).GetProperty("isOnline").GetBoolean());
        Assert.False((await PresenceOfAsync(strangerMatch, owner.PlayerId)).GetProperty("isOnline").GetBoolean());
        Assert.Equal(1, (await PresenceOfAsync(strangerMatch, owner.PlayerId)).GetProperty("statusVisibility").GetInt32());
    }

    [Fact]
    public void Presence_pref_key_matches_status_visibility_writer()
    {
        // MatchPlayerController writes the pref; PlayerPresenceController reads it.
        var field = typeof(MatchPlayerController).GetField(
            "PrefStatusVisibility",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(PlayerPresenceController.PrefStatusVisibility, (string)field!.GetRawConstantValue()!);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private void PutOnline(long playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var presence = scope.ServiceProvider.GetRequiredService<PlayerPresenceService>();
        presence.SetRoom(playerId, new RoomInstanceDto
        {
            RoomInstanceId = 900_000 + playerId,
            RoomId = 1,
            SubRoomId = 0,
            PhotonRoomId = $"^profiletest{playerId}",
            PhotonRegionId = "us",
            MaxCapacity = 8,
        });
        presence.MarkActive(playerId);
    }

    private static async Task<JsonElement> PresenceOfAsync(HttpClient client, long playerId)
    {
        var list = await GetJsonAsync(client, $"/player?{playerId}");
        return list.EnumerateArray().Single(p => p.GetProperty("playerId").GetInt64() == playerId);
    }

    private async Task<(GameClientSession, GameClientSession)> TwoPlayersAsync()
    {
        using var setup = AccountsClient();
        var a = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var b = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        return (a, b);
    }

    private static async Task<HttpResponseMessage> PutFormAsync(HttpClient client, string path, string field, string value)
        => await client.PutAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { [field] = value }));

    private static async Task PutFormOkAsync(HttpClient client, string path, string field, string value)
    {
        using var response = await PutFormAsync(client, path, field, value);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"PUT {path} → {(int)response.StatusCode}: {body}");
    }

    private HttpClient AccountsClient(GameClientSession? session = null) => Client("accounts", session);

    private HttpClient MatchClient(GameClientSession session) => Client("match", session);

    private HttpClient Client(string host, GameClientSession? session)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} → {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
