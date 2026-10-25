using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Sub-room ids and the room instance browser for the 2023 client.
/// <list type="bullet">
/// <item>The client will not load a sub-room whose id is not positive
/// (SubroomModel.DOKCPLJOKIE: <c>RoomId &gt; 0 &amp;&amp; SubroomId &gt; 0</c>),
/// so the wire SubRoomId is OrderIndex + 1 everywhere the client sees it:
/// room details, sub-room routes, matchmaking and the instance browser.</item>
/// <item>A new room always owns a real, named entry sub-room, and a startup
/// pass repairs rooms that lack one.</item>
/// <item><c>GET room/{id}/instances</c> lists live instances with their real
/// players, including the private instance of a private room for the people
/// allowed into it.</item>
/// </list>
/// </summary>
public sealed class SubRoomsAndInstances2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public SubRoomsAndInstances2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public void Wire_sub_room_ids_are_positive_and_round_trip()
    {
        Assert.Equal(1, RoomSceneIds.ToWire(0));
        Assert.Equal(3, RoomSceneIds.ToWire(2));
        Assert.Equal(0, RoomSceneIds.ToOrderIndex(1));
        Assert.Equal(2, RoomSceneIds.ToOrderIndex(3));
        // 0 (cached client state, bare matchmake) is the entry sub-room.
        Assert.Equal(0, RoomSceneIds.ToOrderIndex(0));
        Assert.Equal(0, RoomSceneIds.ToOrderIndex(-1));
    }

    [Fact]
    public async Task Details_matchmaking_and_sub_room_routes_share_the_wire_id()
    {
        var owner = await PlayerAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId, accessibility: 1, scenes: new[] { "Home", "Arena" });

        using var rooms = Client("rooms", owner);
        var details = await GetJsonAsync(rooms, $"/roomserver/rooms/{roomId}");
        var subRooms = details.GetProperty("SubRooms").EnumerateArray().ToList();
        Assert.Equal(new long[] { 1, 2 }, subRooms.Select(s => s.GetProperty("SubRoomId").GetInt64()));
        Assert.Equal("Home", subRooms[0].GetProperty("Name").GetString());

        // matchmake/room/{id}/{sub} takes the wire id and echoes it.
        using var match = Client("match", owner);
        var arena = await PostFormAsync(match, $"/matchmake/room/{roomId}/2");
        Assert.Equal(2, arena.GetProperty("roomInstance").GetProperty("subRoomId").GetInt64());
        var lobby = await PostFormAsync(match, $"/matchmake/room/{roomId}");
        Assert.Equal(1, lobby.GetProperty("roomInstance").GetProperty("subRoomId").GetInt64());

        // A sub-room route addressed by wire id 2 edits OrderIndex 1.
        using (var rename = await rooms.PutAsync($"/roomserver/rooms/{roomId}/subrooms/2/name",
                   new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "Basement" })))
            Assert.True(rename.IsSuccessStatusCode, await rename.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        Assert.Equal("Basement", (await db.RoomScenes.SingleAsync(s => s.RoomId == roomId && s.OrderIndex == 1)).Name);
        Assert.Equal("Home", (await db.RoomScenes.SingleAsync(s => s.RoomId == roomId && s.OrderIndex == 0)).Name);
    }

    [Fact]
    public async Task Cloning_a_template_without_scene_rows_creates_a_named_home_sub_room()
    {
        var owner = await PlayerAsync();
        var templateId = await SeedRoomAsync(1, accessibility: 1, scenes: Array.Empty<string>(), isAg: true);

        using var rooms = Client("rooms", owner);
        using var resp = await rooms.PostAsync($"/roomserver/rooms/{templateId}/clone",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = $"Fresh{Guid.NewGuid():N}"[..20] }));
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, text);
        using var json = JsonDocument.Parse(text);
        var value = json.RootElement.GetProperty("value");
        var cloneId = value.GetProperty("RoomId").GetInt64();
        var sub = value.GetProperty("SubRooms").EnumerateArray().Single();
        Assert.Equal(1, sub.GetProperty("SubRoomId").GetInt64());
        Assert.Equal("Home", sub.GetProperty("Name").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var scene = await db.RoomScenes.SingleAsync(s => s.RoomId == cloneId);
        Assert.Equal(0, scene.OrderIndex);
        Assert.Equal("Home", scene.Name);
        Assert.Equal("a75f7547-79eb-47c6-8986-6767abcb4f92", scene.RoomSceneLocationId);
    }

    [Fact]
    public async Task Startup_repair_gives_scene_less_rooms_a_home_and_names_blank_rows()
    {
        var bare = await SeedRoomAsync(1, accessibility: 0, scenes: Array.Empty<string>());
        var blank = await SeedRoomAsync(1, accessibility: 0, scenes: new[] { "", " " });

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RoomService>();
        var (added, renamed) = await service.RepairRoomScenesAsync();
        Assert.True(added >= 1);
        Assert.True(renamed >= 2);

        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var home = await db.RoomScenes.SingleAsync(s => s.RoomId == bare);
        Assert.Equal((0, "Home"), (home.OrderIndex, home.Name));
        var names = await db.RoomScenes.Where(s => s.RoomId == blank).OrderBy(s => s.OrderIndex).Select(s => s.Name).ToListAsync();
        Assert.Equal(new[] { "Home", "Subroom 2" }, names);

        // Idempotent.
        Assert.Equal((0, 0), await service.RepairRoomScenesAsync());
    }

    [Fact]
    public async Task Room_level_restore_also_restores_the_entry_sub_room()
    {
        var owner = await PlayerAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId, accessibility: 0, scenes: new[] { "Home", "Back" });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            await db.RoomScenes.Where(s => s.RoomId == roomId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DataBlobName, "blob-old"));
            db.RoomDataBlobs.Add(new RoomDataBlobEntity { RoomId = roomId, BlobName = "blob-new", UploadedByPlayerId = owner.PlayerId });
            await db.SaveChangesAsync();
        }

        using var api = Client("api", owner);
        using var response = await api.PostAsync("/api/rooms/v1/restore", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["roomId"] = roomId.ToString(), ["blobName"] = "blob-new",
        }));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        await using var check = _factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var blobs = await checkDb.RoomScenes.AsNoTracking().Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex).Select(s => s.DataBlobName).ToListAsync();
        // The entry sub-room follows the restore; other sub-rooms keep theirs.
        Assert.Equal(new[] { "blob-new", "blob-old" }, blobs);
    }

    [Fact]
    public async Task Startup_repair_restores_a_missing_entry_sub_room_next_to_others()
    {
        var roomId = await SeedRoomAsync(1, accessibility: 0, scenes: new[] { "Home", "Back" });
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        await db.RoomScenes.Where(s => s.RoomId == roomId && s.OrderIndex == 0).ExecuteDeleteAsync();

        var service = scope.ServiceProvider.GetRequiredService<RoomService>();
        var (added, _) = await service.RepairRoomScenesAsync();
        Assert.True(added >= 1);

        var scenes = await db.RoomScenes.AsNoTracking().Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex).Select(s => new { s.OrderIndex, s.Name }).ToListAsync();
        Assert.Equal(new[] { (0, "Home"), (1, "Back") }, scenes.Select(s => (s.OrderIndex, s.Name)));
        Assert.Equal((0, 0), await service.RepairRoomScenesAsync());
    }

    [Fact]
    public async Task Instance_browser_lists_the_private_instance_with_its_players_for_allowed_callers()
    {
        var owner = await PlayerAsync();
        var coOwner = await PlayerAsync();
        var stranger = await PlayerAsync();
        var roomId = await SeedRoomAsync(owner.PlayerId, accessibility: 0, scenes: new[] { "Home" });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = roomId, PlayerId = coOwner.PlayerId, Role = RoomRoleRules.StoredCoOwner, Accepted = true,
            });
            await db.SaveChangesAsync();
        }

        using var ownerMatch = Client("match", owner);
        var joined = await PostFormAsync(ownerMatch, $"/matchmake/room/{roomId}");
        var instanceId = joined.GetProperty("roomInstance").GetProperty("roomInstanceId").GetInt64();

        foreach (var viewer in new[] { owner, coOwner })
        {
            using var match = Client("match", viewer);
            var list = await GetJsonAsync(match, $"/room/{roomId}/instances");
            var row = list.EnumerateArray().Single();
            Assert.Equal(instanceId, row.GetProperty("roomInstanceId").GetInt64());
            Assert.Equal(roomId, row.GetProperty("roomId").GetInt64());
            Assert.Equal(1, row.GetProperty("subRoomId").GetInt64());
            Assert.False(row.GetProperty("isFull").GetBoolean());
            Assert.Equal(new[] { (int)owner.PlayerId },
                row.GetProperty("playerIds").EnumerateArray().Select(p => p.GetInt32()));
        }

        // Someone with no way into the private room is not shown it.
        using var strangerMatch = Client("match", stranger);
        var hidden = await GetJsonAsync(strangerMatch, $"/room/{roomId}/instances");
        Assert.Empty(hidden.EnumerateArray());
    }

    [Fact]
    public void Private_instance_visibility_rule()
    {
        var row = new PrivateInstanceEntity { Id = 5, OwnerPlayerId = 3 };
        Assert.True(Controllers.Match.MatchController.CanSeePrivateInstance(9, true, Array.Empty<long>(), row, false));
        Assert.True(Controllers.Match.MatchController.CanSeePrivateInstance(9, false, new long[] { 9 }, row, false));
        Assert.True(Controllers.Match.MatchController.CanSeePrivateInstance(3, false, Array.Empty<long>(), row, false));
        Assert.True(Controllers.Match.MatchController.CanSeePrivateInstance(9, false, Array.Empty<long>(), row, true));
        Assert.False(Controllers.Match.MatchController.CanSeePrivateInstance(9, false, Array.Empty<long>(), row, false));
        Assert.False(Controllers.Match.MatchController.CanSeePrivateInstance(0, true, Array.Empty<long>(), row, true));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<GameClientSession> PlayerAsync()
    {
        using var setup = Client("api");
        return await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
    }

    private async Task<long> SeedRoomAsync(long creatorId, int accessibility, string[] scenes, bool isAg = false)
    {
        var roomId = 9_200_000 + Random.Shared.Next(1, 99_999);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        db.Rooms.Add(new RoomEntity
        {
            Id = roomId,
            Name = $"Subs{Guid.NewGuid():N}"[..18],
            CreatorPlayerId = creatorId,
            ImageName = RoomService.DefaultRoomImageName,
            Accessibility = accessibility,
            CloningAllowed = true,
            IsAGRoom = isAg,
            LocationReplicationId = "a75f7547-79eb-47c6-8986-6767abcb4f92",
        });
        for (var i = 0; i < scenes.Length; i++)
            db.RoomScenes.Add(new RoomSceneEntity { RoomId = roomId, OrderIndex = i, Name = scenes[i] });
        await db.SaveChangesAsync();
        return roomId;
    }

    private static async Task<JsonElement> PostFormAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client(string subdomain, GameClientSession? session = null)
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
