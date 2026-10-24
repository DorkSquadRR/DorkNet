using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Controllers.API.Inventory;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Hubs;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DorkNet.Server.Tests;

/// <summary>
/// Push frames checked against the 2023-03-21 client's notification readers
/// (RecNet.Runtime). Every push is the hub method "Notification" with one
/// JSON string <c>{"Id":"&lt;key&gt;","Msg":{…}}</c>; the client looks the Id
/// up case-sensitively and parses Msg strictly into the handler's DTO, so a
/// wrong key or shape drops the push without any error.
/// </summary>
public sealed class NotificationContractTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public NotificationContractTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Friend_request_pushes_each_side_its_own_relationship_and_an_inbox_message()
    {
        var (factory, hub) = WithCapturingHub();
        using var setup = ApiClient(factory);
        var sender = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var target = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        using var client = ApiClient(factory, sender);
        (await client.PostAsync($"/api/relationships/v2/sendfriendrequest?id={target.PlayerId}", null))
            .EnsureSuccessStatusCode();

        // RelationshipChanged "1" = the Relationship row from each side's view.
        var toTarget = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(target.PlayerId), "1"));
        Assert.Equal("Notification", toTarget.Method);
        Assert.Equal(sender.PlayerId, toTarget.Msg.GetProperty("PlayerID").GetInt64());
        Assert.Equal(2, toTarget.Msg.GetProperty("RelationshipType").GetInt32()); // FriendRequestReceived
        Assert.False(toTarget.Msg.TryGetProperty("Reason", out _));

        var toSender = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(sender.PlayerId), "1"));
        Assert.Equal(target.PlayerId, toSender.Msg.GetProperty("PlayerID").GetInt64());
        Assert.Equal(1, toSender.Msg.GetProperty("RelationshipType").GetInt32()); // FriendRequestSent

        // MessageReceived "2" — the "X sent you a friend request" inbox row.
        var invite = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(target.PlayerId), "2"));
        Assert.Equal(4, invite.Msg.GetProperty("Type").GetInt32()); // FriendInvite
        Assert.Equal(sender.PlayerId, invite.Msg.GetProperty("FromPlayerId").GetInt64());

        using var targetClient = ApiClient(factory, target);
        (await targetClient.PostAsync($"/api/relationships/v2/acceptfriendrequest?id={sender.PlayerId}", null))
            .EnsureSuccessStatusCode();

        Assert.Equal(3, hub.To(CapturingHubContext.PlayerGroup(sender.PlayerId), "1")[^1]
            .Msg.GetProperty("RelationshipType").GetInt32());
        Assert.Equal(3, hub.To(CapturingHubContext.PlayerGroup(target.PlayerId), "1")[^1]
            .Msg.GetProperty("RelationshipType").GetInt32());
        var accepted = hub.To(CapturingHubContext.PlayerGroup(sender.PlayerId), "2")[^1];
        Assert.Equal(40, accepted.Msg.GetProperty("Type").GetInt32()); // FriendRequestAccepted
    }

    [Fact]
    public async Task Favorite_pushes_the_callers_updated_row_to_the_caller_only()
    {
        var (factory, hub) = WithCapturingHub();
        using var setup = ApiClient(factory);
        var me = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var other = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        using var client = ApiClient(factory, me);
        (await client.GetAsync($"/api/relationships/v1/favorite?id={other.PlayerId}")).EnsureSuccessStatusCode();

        var frame = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(me.PlayerId), "1"));
        Assert.Equal(other.PlayerId, frame.Msg.GetProperty("PlayerID").GetInt64());
        Assert.Equal(1, frame.Msg.GetProperty("Favorited").GetInt32());
        Assert.Empty(hub.To(CapturingHubContext.PlayerGroup(other.PlayerId), "1"));
    }

    [Fact]
    public async Task Kick_is_the_flat_moderation_block_with_the_reason_in_VoteKickReason()
    {
        var (factory, hub) = WithCapturingHub();
        await using var scope = factory.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        await notifications.KickPlayerAsync(42, "Too loud", gameSessionId: 777);

        var frame = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(42), "22"));
        var msg = frame.Msg;
        // The handler only shows a reason for ReportCategory 10 + !IsHostKick.
        Assert.Equal(10, msg.GetProperty("ReportCategory").GetInt32());
        Assert.False(msg.GetProperty("IsHostKick").GetBoolean());
        Assert.Equal("Too loud", msg.GetProperty("VoteKickReason").GetString());
        Assert.Equal(777, msg.GetProperty("GameSessionId").GetInt64());
        Assert.Equal(0, msg.GetProperty("Duration").GetInt32());
        Assert.False(msg.TryGetProperty("Reason", out _)); // the old nested 2020 shape
    }

    [Theory]
    [InlineData(NotificationService.LogoutReason.SessionTakeover, 2)]
    [InlineData(NotificationService.LogoutReason.ForciblyLoggedOut, 3)]
    [InlineData(NotificationService.LogoutReason.Banned, 4)]
    public async Task Logout_reason_is_the_numeric_enum(NotificationService.LogoutReason reason, int wire)
    {
        var (factory, hub) = WithCapturingHub();
        await using var scope = factory.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        await notifications.LogoutAsync(43, reason);

        var frame = Assert.Single(hub.To(CapturingHubContext.PlayerGroup(43), "6"));
        var value = frame.Msg.GetProperty("Reason");
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal(wire, value.GetInt32());
    }

    [Fact]
    public async Task Presence_for_subscribers_goes_to_the_subjects_presence_group_minus_excluded_connections()
    {
        var (factory, hub) = WithCapturingHub();
        await using var scope = factory.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        await notifications.PresenceToSubscribersAsync(55, room: null, isOnline: true, statusVisibility: 0,
            excludeConnectionIds: ["self-conn", "friend-conn"]);

        var frame = Assert.Single(hub.To($"group:{NotifyHub.PresenceGroupForPlayer(55)}", "PresenceUpdate"));
        Assert.Equal(["self-conn", "friend-conn"], frame.Excluded);
        Assert.Equal(55, frame.Msg.GetProperty("playerId").GetInt32());
        Assert.True(frame.Msg.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.String, frame.Msg.GetProperty("appVersion").ValueKind);
    }

    [Theory]
    [InlineData("""{"PlayerIds":[1,2,3]}""", new long[] { 1, 2, 3 })]
    [InlineData("""{"playerIds":["4",5]}""", new long[] { 4, 5 })]
    [InlineData("""[6,7]""", new long[] { 6, 7 })]
    [InlineData("""{"PlayerIds":[]}""", new long[0])]
    public void SubscribeToPlayers_payload_is_read_in_any_casing(string json, long[] expected)
    {
        Assert.True(NotifyHub.TryReadPlayerIds(JsonDocument.Parse(json).RootElement, out var ids));
        Assert.Equal(expected.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public void Unreadable_SubscribeToPlayers_payload_is_rejected_not_read_as_empty()
    {
        Assert.False(NotifyHub.TryReadPlayerIds(JsonDocument.Parse("""{"Channels":[]}""").RootElement, out _));
    }

    [Fact]
    public void Consumable_mapping_push_has_exactly_the_clients_keys()
    {
        var wire = JsonSerializer.SerializeToElement(InventoryController.ConsumableMappingWire(new PlayerInventoryEntity
        {
            Id = 9,
            ItemSlug = "some-consumable",
            Quantity = 3,
        }));

        var keys = wire.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "Id", "ConsumableItemDesc", "Count", "InitialCount", "CreatedAt", "ActiveDurationMinutes", "IsActive", "IsTransferable" },
            keys);
        Assert.Equal(JsonValueKind.String, wire.GetProperty("ConsumableItemDesc").ValueKind);
        Assert.Equal(3, wire.GetProperty("Count").GetInt32());
    }

    private (WebApplicationFactory<Program> Factory, CapturingHubContext Hub) WithCapturingHub()
    {
        var hub = new CapturingHubContext();
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHubContext<NotifyHub>>();
                services.AddSingleton<IHubContext<NotifyHub>>(hub);
            }));
        return (factory, hub);
    }

    private HttpClient ApiClient(WebApplicationFactory<Program> factory, GameClientSession? session = null)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
