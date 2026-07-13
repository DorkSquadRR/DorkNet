using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Private-room semantics:
///   1. A freshly cloned room is PRIVATE (Accessibility=0) regardless of
///      the source's visibility — the owner publishes explicitly.
///   2. A private room has exactly ONE instance per sub-room: every
///      JoinMode (plain matchmake / new public / new private) funnels
///      into the same deterministic invite-gated instance instead of
///      nonce-forking parallel copies.
///   3. Players who are neither the creator nor role-holders/invitees
///      are turned away with ErrorCode=4 (indistinguishable from a
///      nonexistent room, so private rooms don't leak).
/// </summary>
public sealed class PrivateRoomInstanceTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public PrivateRoomInstanceTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Clone_is_private_by_default()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var sourceId = 9_400_100L;
        await SeedRoomAsync(sourceId, creatorId: owner.PlayerId, accessibility: 1);

        using var roomsClient = ApiClient(owner, subdomain: "rooms");
        using var resp = await roomsClient.PostAsync(
            $"/rooms/{sourceId}/clone",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = $"PrivClone_{Guid.NewGuid():N}"[..24] }));
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"{(int)resp.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        var cloneId = json.RootElement.GetProperty("RoomId").GetInt64();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var clone = await db.Rooms.SingleAsync(r => r.Id == cloneId);
        Assert.Equal(0, clone.Accessibility); // private by default
    }

    [Fact]
    public async Task Private_room_gets_one_instance_per_subroom_for_all_join_modes()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_400_200L;
        await SeedRoomAsync(roomId, creatorId: owner.PlayerId, accessibility: 0);

        using var matchClient = ApiClient(owner, subdomain: "match");

        var joins = new List<(long InstanceId, string Photon, bool IsPrivate)>();
        foreach (var joinMode in new[] { 0, 1, 2, 0 })
        {
            using var resp = await matchClient.PostAsync(
                $"/matchmake/room/{roomId}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["JoinMode"] = joinMode.ToString() }));
            var text = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"JoinMode={joinMode} -> {(int)resp.StatusCode}: {text}");
            using var json = JsonDocument.Parse(text);
            Assert.Equal(0, json.RootElement.GetProperty("errorCode").GetInt32());
            var inst = json.RootElement.GetProperty("roomInstance");
            joins.Add((
                inst.GetProperty("roomInstanceId").GetInt64(),
                inst.GetProperty("photonRoomId").GetString()!,
                inst.GetProperty("isPrivate").GetBoolean()));
        }

        // Every join mode must land in the SAME single instance.
        Assert.Single(joins.Select(j => j.InstanceId).Distinct());
        Assert.Single(joins.Select(j => j.Photon).Distinct());
        Assert.All(joins, j => Assert.True(j.IsPrivate, "private room instances must be invite-gated"));
    }

    [Fact]
    public async Task Private_room_turns_away_uninvited_players()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);
        var stranger = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_400_300L;
        await SeedRoomAsync(roomId, creatorId: owner.PlayerId, accessibility: 0);

        using var strangerClient = ApiClient(stranger, subdomain: "match");
        using var resp = await strangerClient.PostAsync(
            $"/matchmake/room/{roomId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["JoinMode"] = "0" }));
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"{(int)resp.StatusCode}: {text}");

        using var json = JsonDocument.Parse(text);
        // 4 = RoomDoesNotExist — private rooms are invisible, not "forbidden".
        Assert.Equal(4, json.RootElement.GetProperty("errorCode").GetInt32());
    }

    private async Task SeedRoomAsync(long roomId, long creatorId, int accessibility)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        if (await db.Rooms.AnyAsync(r => r.Id == roomId)) return;
        db.Rooms.Add(new RoomEntity
        {
            Id = roomId,
            Name = $"PrivRoom_{roomId}",
            Description = "private-room test",
            CreatorPlayerId = creatorId,
            ImageName = RoomService.DefaultRoomImageName,
            Accessibility = accessibility,
            CloningAllowed = true,
            LocationReplicationId = "76d98498-60a1-430c-ab76-b54a29b7a163",
        });
        db.RoomScenes.Add(new RoomSceneEntity
        {
            RoomId = roomId,
            OrderIndex = 0,
            Name = "Home",
        });
        await db.SaveChangesAsync();
    }

    private HttpClient ApiClient(GameClientSession? session = null, string subdomain = "api")
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{subdomain}.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
