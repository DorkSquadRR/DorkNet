using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Max players is a PER SUB-ROOM setting, adjustable from two places:
///
///   in-game  PUT rooms/{id}/subrooms/{sub}/maxplayers   (owner only — the
///            "Max Player Count in This Subroom" slider)
///   admin    PUT api/admin/v1/rooms/{id}/subrooms/{sub}/maxplayers
///
/// Both write the same <see cref="RoomSceneEntity.MaxPlayers"/>, so a change
/// made in one place has to be visible from the other. That is the property
/// worth pinning: two independent write paths onto one column drift silently,
/// and the symptom (a slider that snaps back) looks like a client bug.
///
/// Kept distinct from the room's own <see cref="RoomEntity.MaxCapacity"/>,
/// which is what the ROOM advertises and what matchmaking hands to
/// RoomInstance.MaxCapacity. Reading the room-level figure off sub-room 0
/// would collapse the two settings into one.
/// </summary>
public sealed class SubRoomMaxPlayersTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public SubRoomMaxPlayersTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_and_admin_edit_the_same_per_subroom_value()
    {
        using var setup = Client("rooms");
        var owner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_700_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;

            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Caps{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = owner.PlayerId,
                MaxCapacity = 12,
            });
            db.RoomScenes.Add(new RoomSceneEntity
            { RoomId = roomId, Name = "Home", OrderIndex = 0, MaxPlayers = 8 });
            db.RoomScenes.Add(new RoomSceneEntity
            { RoomId = roomId, Name = "Arena", OrderIndex = 1, MaxPlayers = 8 });
            await db.SaveChangesAsync();
        }

        // ── in-game: the owner's slider on sub-room 1 only ──────────────────
        // Sent exactly as the client sends it. NLDBPDCNNCF.MNBPCGJNLNP(Int64,
        // Int64, Int32) builds a BLNOGFGHIIF whose single JSON key is
        // "maxPlayers" and issues it with verb 3 = PUT — NOT the form encoding
        // most of its siblings use. Testing with a form would pass while the
        // real client's body went unread.
        using var ownerClient = Client("rooms", owner);
        using var slider = await ownerClient.PutAsJsonAsync(
            $"/rooms/{roomId}/subrooms/1/maxplayers", new { maxPlayers = 24 });
        var sliderBody = await slider.Content.ReadAsStringAsync();
        Assert.True(slider.IsSuccessStatusCode,
            $"owner PUT maxplayers -> {(int)slider.StatusCode}: {sliderBody}");

        // The client deserialises the reply as FGCPNAACHIK — the room-details
        // shape — so the slider's own response has to carry the new value.
        var echoed = JsonDocument.Parse(sliderBody).RootElement
            .GetProperty("SubRooms").EnumerateArray()
            .First(e => e.GetProperty("SubRoomId").GetInt64() == 1);
        Assert.Equal(24, echoed.GetProperty("MaxPlayers").GetInt32());

        // ── admin: reads what the owner just set, then edits sub-room 0 ─────
        using var adminClient = Client("admin", admin);
        var listed = await GetJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms");
        var subRooms = listed.EnumerateArray()
            .ToDictionary(e => e.GetProperty("subRoomId").GetInt32(),
                          e => e.GetProperty("maxPlayers").GetInt32());

        Assert.Equal(24, subRooms[1]);
        Assert.Equal(8, subRooms[0]);

        using var adminSet = await adminClient.PutAsJsonAsync(
            $"/api/admin/v1/rooms/{roomId}/subrooms/0/maxplayers", new { MaxPlayers = 40 });
        Assert.True(adminSet.IsSuccessStatusCode,
            $"admin PUT maxplayers -> {(int)adminSet.StatusCode}: {await adminSet.Content.ReadAsStringAsync()}");

        // ── the room's own details carry each sub-room's own figure ─────────
        var details = await GetJsonAsync(ownerClient, $"/rooms/{roomId}");
        var wire = details.GetProperty("SubRooms").EnumerateArray()
            .ToDictionary(e => e.GetProperty("SubRoomId").GetInt64(),
                          e => e.GetProperty("MaxPlayers").GetInt32());

        Assert.Equal(40, wire[0]);
        Assert.Equal(24, wire[1]);

        // The room-level figure is the room's advertised capacity, NOT a copy
        // of sub-room 0 — otherwise the admin edit above would have moved it.
        Assert.Equal(12, details.GetProperty("MaxPlayers").GetInt32());
    }

    [Fact]
    public async Task Admin_cap_is_clamped_to_a_joinable_range()
    {
        using var setup = Client("rooms");
        var owner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_800_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Clamp{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = owner.PlayerId,
            });
            db.RoomScenes.Add(new RoomSceneEntity
            { RoomId = roomId, Name = "Home", OrderIndex = 0, MaxPlayers = 8 });
            await db.SaveChangesAsync();
        }

        using var adminClient = Client("admin", admin);

        // 0 would make the sub-room unjoinable.
        using var tooLow = await adminClient.PutAsJsonAsync(
            $"/api/admin/v1/rooms/{roomId}/subrooms/0/maxplayers", new { MaxPlayers = 0 });
        var low = JsonDocument.Parse(await tooLow.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, low.GetProperty("maxPlayers").GetInt32());

        using var tooHigh = await adminClient.PutAsJsonAsync(
            $"/api/admin/v1/rooms/{roomId}/subrooms/0/maxplayers", new { MaxPlayers = 9999 });
        var high = JsonDocument.Parse(await tooHigh.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(80, high.GetProperty("maxPlayers").GetInt32());
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
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
