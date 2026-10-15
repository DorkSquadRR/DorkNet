using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Controllers.Match;
using DorkNet.Server.Services;

namespace DorkNet.Server.Tests;

/// <summary>
/// Presence must follow the player through every transition, for the player
/// themselves (the heartbeat) and for everyone else (bulk /player and the
/// PresenceUpdate push).
///
/// The heartbeat side is decided by the client's compare
/// (RecNet.Runtime/RecNet/Matchmaking.txt:18415-18467): a cached instance with
/// a negative id — the offline room — is in sync ONLY with roomInstance null,
/// and anything else makes the client cancel what it is doing and chase the
/// server's room. matchmake/none is how the client tells us it went to that
/// offline room, so after it the heartbeat must stay null.
/// </summary>
public sealed class PresenceTransitionTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public PresenceTransitionTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Leaving_to_the_offline_room_makes_the_heartbeat_report_no_room()
    {
        using var client = await SignedInMatchClientAsync();

        // Enter the dorm for real: matchmake, then report the join.
        var dormInstance = await MatchmakeDormAsync(client);
        await ReportJoinAsync(client, dormInstance);
        var before = await HeartbeatAsync(client);
        Assert.Equal(dormInstance, FindInstanceId(RoomInstanceOf(before)));

        using var none = await client.PostAsync("/matchmake/none", Form());
        Assert.True(none.IsSuccessStatusCode, $"POST /matchmake/none -> {(int)none.StatusCode}");

        // Twice: the first beat must not re-seed a dorm the second one returns.
        for (var i = 0; i < 2; i++)
        {
            var beat = await HeartbeatAsync(client);
            var room = RoomInstanceOf(beat);
            Assert.True(room.ValueKind == JsonValueKind.Null,
                $"""
                 After matchmake/none the client holds its offline room (instance -1)
                 and is only in sync with a null roomInstance. The heartbeat answered
                 with a room, which makes the client cancel the offline load and
                 chase it:

                 {beat.GetRawText()}
                 """);
        }
    }

    [Fact]
    public async Task Leaving_while_a_join_is_pending_abandons_the_join()
    {
        using var client = await SignedInMatchClientAsync();

        // The client gave up on this dorm join (e.g. a Photon timeout) and fell
        // back to the offline room before reporting a result.
        await MatchmakeDormAsync(client);
        using var none = await client.PostAsync("/matchmake/none", Form());
        Assert.True(none.IsSuccessStatusCode);

        var beat = await HeartbeatAsync(client);
        Assert.True(RoomInstanceOf(beat).ValueKind == JsonValueKind.Null,
            $"heartbeat still echoed the abandoned join target: {beat.GetRawText()}");
    }

    [Fact]
    public async Task Matchmaking_again_after_leaving_restores_the_room()
    {
        using var client = await SignedInMatchClientAsync();

        using var none = await client.PostAsync("/matchmake/none", Form());
        Assert.True(none.IsSuccessStatusCode);
        Assert.True(RoomInstanceOf(await HeartbeatAsync(client)).ValueKind == JsonValueKind.Null);

        var dormInstance = await MatchmakeDormAsync(client);
        await ReportJoinAsync(client, dormInstance);

        var beat = await HeartbeatAsync(client);
        Assert.Equal(dormInstance, FindInstanceId(RoomInstanceOf(beat)));
    }

    [Fact]
    public async Task Friends_see_a_player_in_the_offline_room_as_online_with_no_room()
    {
        using var subject = await SignedInMatchClientAsync();
        using var viewer = await SignedInMatchClientAsync();
        var subjectId = await PlayerIdOfAsync(subject);

        var dormInstance = await MatchmakeDormAsync(subject);
        await ReportJoinAsync(subject, dormInstance);
        using var none = await subject.PostAsync("/matchmake/none", Form());
        Assert.True(none.IsSuccessStatusCode);

        var row = await BulkPresenceAsync(viewer, subjectId);
        Assert.True(row.GetProperty("isOnline").GetBoolean(),
            $"a player sitting in the offline room is online: {row.GetRawText()}");
        Assert.Equal(JsonValueKind.Null, row.GetProperty("roomInstance").ValueKind);
    }

    [Fact]
    public async Task Logging_out_shows_the_player_offline_and_roomless()
    {
        using var subject = await SignedInMatchClientAsync();
        using var viewer = await SignedInMatchClientAsync();
        var subjectId = await PlayerIdOfAsync(subject);

        var dormInstance = await MatchmakeDormAsync(subject);
        await ReportJoinAsync(subject, dormInstance);
        Assert.True((await BulkPresenceAsync(viewer, subjectId)).GetProperty("isOnline").GetBoolean());

        using var logout = await subject.PostAsync("/player/logout", Form());
        Assert.True(logout.IsSuccessStatusCode);

        var row = await BulkPresenceAsync(viewer, subjectId);
        Assert.False(row.GetProperty("isOnline").GetBoolean(), row.GetRawText());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("roomInstance").ValueKind);
    }

    [Fact]
    public async Task Bulk_presence_reports_the_stored_status_visibility()
    {
        using var subject = await SignedInMatchClientAsync();
        using var viewer = await SignedInMatchClientAsync();
        var subjectId = await PlayerIdOfAsync(subject);

        using var put = await subject.PutAsync("/player/statusvisibility",
            Form(("statusVisibility", "2")));
        Assert.True(put.IsSuccessStatusCode, $"PUT statusvisibility -> {(int)put.StatusCode}");

        var row = await BulkPresenceAsync(viewer, subjectId);
        Assert.Equal(2, row.GetProperty("statusVisibility").GetInt32());

        // And the player's own heartbeat agrees.
        var beat = await HeartbeatAsync(subject);
        Assert.Equal(2, beat.GetProperty("statusVisibility").GetInt32());
    }

    [Fact]
    public void Presence_push_carries_online_state_independently_of_the_room()
    {
        var room = new RoomInstanceDto
        {
            RoomInstanceId = 42,
            RoomId = 7,
            Name = "DormRoom",
            PhotonRoomId = "^dormroom_p1",
        };

        // Online, no room: the offline-room state. Used to be sent as OFFLINE
        // because isOnline was derived from the room.
        var onlineNoRoom = Msg(NotificationService.BuildPresenceUpdatePayload(5, null, isOnline: true));
        Assert.True(onlineNoRoom.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, onlineNoRoom.GetProperty("roomInstance").ValueKind);

        // Offline never leaks a room, even if one is still cached.
        var offline = Msg(NotificationService.BuildPresenceUpdatePayload(5, room, isOnline: false));
        Assert.False(offline.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, offline.GetProperty("roomInstance").ValueKind);

        // Online in a room: redacted join coordinates, visibility passed through,
        // appVersion a STRING.
        var inRoom = Msg(NotificationService.BuildPresenceUpdatePayload(5, room, isOnline: true, statusVisibility: 1));
        Assert.True(inRoom.GetProperty("isOnline").GetBoolean());
        Assert.Equal(42, inRoom.GetProperty("roomInstance").GetProperty("roomInstanceId").GetInt64());
        Assert.Equal(string.Empty, inRoom.GetProperty("roomInstance").GetProperty("photonRoomId").GetString());
        Assert.Equal(1, inRoom.GetProperty("statusVisibility").GetInt32());
        Assert.Equal(JsonValueKind.String, inRoom.GetProperty("appVersion").ValueKind);

        static JsonElement Msg(string payload)
        {
            var root = JsonDocument.Parse(payload).RootElement;
            // String-keyed channel: the client registers "PresenceUpdate".
            Assert.Equal("PresenceUpdate", root.GetProperty("Id").GetString());
            return root.GetProperty("Msg");
        }
    }

    [Fact]
    public void Presence_pushes_honour_the_players_status_visibility()
    {
        // Live PresenceUpdate frames must hide a player exactly as GET /player
        // does, or "appear offline" leaks every room change to friends.
        long[] friends = [10, 11, 12];
        var favorited = new HashSet<long> { 11 };

        var pub = PresenceBroadcastService.SplitRecipients(friends, PresenceVisibility.Public, favorited);
        Assert.Equal(friends, pub.VisibleTo);
        Assert.Empty(pub.HiddenFrom);

        // Every recipient is already a friend, so FriendsOnly hides nobody.
        var friendsOnly = PresenceBroadcastService.SplitRecipients(friends, PresenceVisibility.FriendsOnly, favorited);
        Assert.Equal(friends, friendsOnly.VisibleTo);
        Assert.Empty(friendsOnly.HiddenFrom);

        var favoritesOnly = PresenceBroadcastService.SplitRecipients(friends, PresenceVisibility.FavoriteFriendsOnly, favorited);
        Assert.Equal([11L], favoritesOnly.VisibleTo);
        Assert.Equal([10L, 12L], favoritesOnly.HiddenFrom);

        var offline = PresenceBroadcastService.SplitRecipients(friends, PresenceVisibility.Offline, favorited);
        Assert.Empty(offline.VisibleTo);
        Assert.Equal(friends, offline.HiddenFrom);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private async Task<HttpClient> SignedInMatchClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://match.{_factory.ApexDomain}");
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<long> PlayerIdOfAsync(HttpClient client)
    {
        var beat = await HeartbeatAsync(client);
        return beat.GetProperty("playerId").GetInt64();
    }

    private static async Task<string> MatchmakeDormAsync(HttpClient client)
    {
        using var resp = await client.PostAsync("/matchmake/dorm", Form(("MaxPersistenceVersion", "45")));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"POST /matchmake/dorm -> {(int)resp.StatusCode}: {body}");
        var id = FindInstanceId(JsonDocument.Parse(body).RootElement);
        Assert.False(string.IsNullOrEmpty(id), $"no roomInstanceId in the dorm reply: {body}");
        return id!;
    }

    private static async Task ReportJoinAsync(HttpClient client, string instanceId)
    {
        using var resp = await client.PostAsync($"/roominstance/{instanceId}/reportjoinresult", Form(("result", "0")));
        Assert.True(resp.IsSuccessStatusCode, $"reportjoinresult -> {(int)resp.StatusCode}");
    }

    private static async Task<JsonElement> HeartbeatAsync(HttpClient client)
    {
        using var resp = await client.PostAsync("/player/heartbeat", Form(("LoginLock", Guid.NewGuid().ToString())));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"POST /player/heartbeat -> {(int)resp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> BulkPresenceAsync(HttpClient viewer, long subjectId)
    {
        using var resp = await viewer.GetAsync($"/player?{subjectId}");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"GET /player -> {(int)resp.StatusCode}: {body}");
        foreach (var row in JsonDocument.Parse(body).RootElement.EnumerateArray())
        {
            if (row.GetProperty("playerId").GetInt64() == subjectId) return row.Clone();
        }
        Assert.Fail($"no row for {subjectId}: {body}");
        return default;
    }

    private static JsonElement RoomInstanceOf(JsonElement presence)
    {
        Assert.True(presence.TryGetProperty("roomInstance", out var room),
            $"no roomInstance key: {presence.GetRawText()}");
        return room;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static string? FindInstanceId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("roomInstanceId", StringComparison.OrdinalIgnoreCase))
                return property.Value.ToString();
            if (property.Value.ValueKind == JsonValueKind.Object
                && FindInstanceId(property.Value) is { } nested)
                return nested;
        }
        return null;
    }
}
