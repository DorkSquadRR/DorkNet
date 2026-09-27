using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecRoom.Protobuf;

namespace DorkNet.Server.Tests;

/// <summary>
/// Baked Rec Room Originals (Rec Royale, Paintball, …) ship with an empty
/// <see cref="RoomSceneEntity.DataBlobName"/>, and every details builder and
/// matchmaking resolver turns that into an empty <c>DataBlob</c>. The client
/// reads that as "no room data at all": no persistence views, no room-role
/// overlay, no Maker Pen, no CV2 chips, no room mood engine. Real Rec Room
/// served these rooms a blob — Rec Royale's game-rule chip lives in it, not
/// in the scene — so DorkNet needs a switch that gives such a room one.
///
/// The property pinned here: the admin "enable blob" action stamps the
/// synthetic default name, that name flows through the game-client details
/// verbatim while <c>IsRRO</c> stays true (the client still loads the baked
/// map, and only additionally fetches the blob), the CDN answers that name
/// with the RRO-editable blob (permissive room roles, no objects), and
/// "reset" puts everything back to scene-only.
/// </summary>
public sealed class RroRoomBlobTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RroRoomBlobTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Baked_original_gets_no_blob_until_an_admin_enables_one()
    {
        using var setup = Client("rooms");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_600_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;

            // Shape of a seeded baked original: system-owned, AG-flagged,
            // tagged recroomoriginal, no blob anywhere.
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Royale{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal,sport",
                CurrentDataBlobName = string.Empty,
            });
            db.RoomScenes.Add(new RoomSceneEntity
            { RoomId = roomId, Name = "Home", OrderIndex = 0, DataBlobName = string.Empty });
            await db.SaveChangesAsync();
        }

        var synthetic = RoomService.SyntheticDefaultRoomDataBlobName(roomId);
        using var gameClient = Client("rooms", player);
        using var adminClient = Client("admin", admin);
        using var cdnClient = Client("cdn");

        // ── before: baked scene only ────────────────────────────────────────
        var before = await GetJsonAsync(gameClient, $"/rooms/{roomId}");
        Assert.True(before.GetProperty("IsRRO").GetBoolean());
        Assert.Equal(string.Empty, SubRoomZero(before).GetProperty("DataBlob").GetString());

        var listed = await GetJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms");
        var row = listed.EnumerateArray().Single();
        Assert.True(row.GetProperty("isBakedOriginal").GetBoolean());
        Assert.Equal(string.Empty, row.GetProperty("effectiveBlobName").GetString());

        // ── enable: the synthetic name lands on the scene AND the room ─────
        var enabled = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/enable");
        Assert.True(enabled.GetProperty("changed").GetBoolean());
        Assert.Equal(synthetic, enabled.GetProperty("effectiveBlobName").GetString());
        Assert.Equal(synthetic, enabled.GetProperty("dataBlobName").GetString());
        Assert.Equal(synthetic, enabled.GetProperty("roomCurrentDataBlobName").GetString());

        // Idempotent: a second enable changes nothing and keeps the name.
        var again = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/enable");
        Assert.False(again.GetProperty("changed").GetBoolean());
        Assert.Equal(synthetic, again.GetProperty("effectiveBlobName").GetString());

        // The game client now gets the blob name, and IsRRO is untouched —
        // that flag is what makes it load the baked map (a false there sends
        // it looking for geometry inside the blob and the join fails).
        var after = await GetJsonAsync(gameClient, $"/rooms/{roomId}");
        Assert.True(after.GetProperty("IsRRO").GetBoolean());
        Assert.Equal(synthetic, SubRoomZero(after).GetProperty("DataBlob").GetString());

        // ── the CDN answers that name with the RRO-editable blob ───────────
        using var blobResponse = await cdnClient.GetAsync($"/room/{synthetic}");
        var blobBytes = await blobResponse.Content.ReadAsByteArrayAsync();
        Assert.True(blobResponse.IsSuccessStatusCode,
            $"GET cdn /room/{synthetic} -> {(int)blobResponse.StatusCode}");
        var persisted = PersistedRoomData.Parser.ParseFrom(blobBytes);
        Assert.NotNull(persisted.RoomRoleData);
        Assert.NotEmpty(persisted.RoomRoleData.RoomRoles);

        // ── reset: back to scene-only ───────────────────────────────────────
        var reset = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/reset");
        Assert.True(reset.GetProperty("changed").GetBoolean());
        Assert.Equal(string.Empty, reset.GetProperty("effectiveBlobName").GetString());

        var restored = await GetJsonAsync(gameClient, $"/rooms/{roomId}");
        Assert.True(restored.GetProperty("IsRRO").GetBoolean());
        Assert.Equal(string.Empty, SubRoomZero(restored).GetProperty("DataBlob").GetString());
    }

    /// <summary>The seeded Rec Royale rows have NO RoomScenes at all (the admin
    /// page showed "Scenes 0 / No sub-rooms"). The admin list must synthesise
    /// the entry scene the way the game-client details do, and enable must
    /// create the row rather than 404.</summary>
    [Fact]
    public async Task Room_without_scene_rows_gets_a_virtual_entry_scene_and_enable_creates_it()
    {
        using var setup = Client("rooms");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_670_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"NoScene{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal",
                MaxCapacity = 24,
            });
            await db.SaveChangesAsync();
        }

        var synthetic = RoomService.SyntheticDefaultRoomDataBlobName(roomId);
        using var gameClient = Client("rooms", player);
        using var adminClient = Client("admin", admin);

        var listed = await GetJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms");
        var virtualRow = listed.EnumerateArray().Single();
        Assert.True(virtualRow.GetProperty("isVirtual").GetBoolean());
        Assert.Equal(0, virtualRow.GetProperty("subRoomId").GetInt32());
        Assert.Equal(24, virtualRow.GetProperty("maxPlayers").GetInt32());
        Assert.Equal(string.Empty, virtualRow.GetProperty("effectiveBlobName").GetString());

        var enabled = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/enable");
        Assert.True(enabled.GetProperty("changed").GetBoolean());
        Assert.Equal(synthetic, enabled.GetProperty("effectiveBlobName").GetString());

        // The row now exists with the game-side defaults, and the game client
        // sees the blob on its entry scene.
        var relisted = await GetJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms");
        var real = relisted.EnumerateArray().Single();
        Assert.False(real.TryGetProperty("isVirtual", out _));
        Assert.Equal("Home", real.GetProperty("name").GetString());
        Assert.Equal(24, real.GetProperty("maxPlayers").GetInt32());
        Assert.Equal(synthetic, real.GetProperty("dataBlobName").GetString());

        var details = await GetJsonAsync(gameClient, $"/rooms/{roomId}");
        Assert.True(details.GetProperty("IsRRO").GetBoolean());
        Assert.Equal(synthetic, SubRoomZero(details).GetProperty("DataBlob").GetString());

        var reset = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/reset");
        Assert.True(reset.GetProperty("changed").GetBoolean());
        Assert.Equal(string.Empty, SubRoomZero(await GetJsonAsync(gameClient, $"/rooms/{roomId}")).GetProperty("DataBlob").GetString());
    }

    [Fact]
    public async Task Enable_on_a_saved_subroom_keeps_the_saved_blob()
    {
        using var setup = Client("rooms");
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_650_000 + Random.Shared.Next(1, 99_999);
        const string saved = "room_saved_by_a_player_v3.dat";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Saved{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal",
                CurrentDataBlobName = saved,
            });
            db.RoomScenes.Add(new RoomSceneEntity
            { RoomId = roomId, Name = "Home", OrderIndex = 0, DataBlobName = saved });
            await db.SaveChangesAsync();
        }

        using var adminClient = Client("admin", admin);
        var enabled = await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/enable");
        Assert.False(enabled.GetProperty("changed").GetBoolean());
        Assert.Equal(saved, enabled.GetProperty("effectiveBlobName").GetString());
    }

    private static JsonElement SubRoomZero(JsonElement details) =>
        details.GetProperty("SubRooms").EnumerateArray()
            .First(e => e.GetProperty("SubRoomId").GetInt64() == 0);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsync(path, content: null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client(string host, GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
