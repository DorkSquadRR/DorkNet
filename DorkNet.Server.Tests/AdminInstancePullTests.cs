using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Controllers.Match;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Regression: admin "pull a player into an instance" broke because the
/// instance id (an int64 well past JS's 2^53 safe-integer range) was
/// emitted as a JSON number. The admin SPA's JSON.parse rounded it to the
/// nearest double, so the pull URL targeted a non-existent instance and
/// the server returned <c>instance_not_active</c>. The fix serializes the
/// id as a STRING so the exact digits survive the browser round-trip.
/// </summary>
public sealed class AdminInstancePullTests : IClassFixture<DorkNetServerFactory>
{
    // A dorm-style instance id past 2^53 — the value class that a JS
    // Number cannot represent exactly.
    private const long BigInstanceId = 7597291079085522944L;

    private readonly DorkNetServerFactory _factory;

    public AdminInstancePullTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Instances_list_emits_id_as_string_and_pull_with_exact_id_succeeds()
    {
        using var setup = ApiClient();
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var streamer = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var pulled = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        long roomId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();

            // Promote the caller to admin (AdminOnly reads IsAdmin from DB).
            var adminRow = await db.Players.FirstAsync(p => p.Id == admin.PlayerId);
            adminRow.IsAdmin = true;

            roomId = (await db.Rooms.FirstAsync(r => r.State == 0)).Id;
            await db.SaveChangesAsync();

            // Put the streamer in a live instance carrying the huge id.
            var presence = scope.ServiceProvider.GetRequiredService<PlayerPresenceService>();
            presence.SetRoom(streamer.PlayerId, new RoomInstanceDto
            {
                RoomInstanceId = BigInstanceId,
                RoomId = roomId,
                SubRoomId = 0,
                PhotonRoomId = "^teststreamerinstance",
                PhotonRegionId = "us",
                MaxCapacity = 8,
            });
            presence.MarkActive(streamer.PlayerId);
        }

        using var adminClient = ApiClient(admin);
        adminClient.BaseAddress = new Uri($"http://admin.{_factory.ApexDomain}");

        // The list must emit roomInstanceId as a STRING equal to the exact
        // digits — never a number (which would already be rounded here).
        var list = await GetJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/instances");
        var row = list.EnumerateArray()
            .First(r => r.GetProperty("roomInstanceId").GetString() == BigInstanceId.ToString());
        Assert.Equal(JsonValueKind.String, row.GetProperty("roomInstanceId").ValueKind);

        // Pull the second player in using the EXACT id from the list. This
        // is what the SPA does; with the string round-trip the digits are
        // intact and the instance matches (pre-fix: 404 instance_not_active).
        var idFromList = row.GetProperty("roomInstanceId").GetString()!;
        using var pull = await adminClient.PostAsync(
            $"/api/admin/v1/rooms/{roomId}/instances/{idFromList}/pull",
            new StringContent(JsonSerializer.Serialize(new { PlayerId = pulled.PlayerId }),
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
        using var body = await JsonDocument.ParseAsync(await pull.Content.ReadAsStreamAsync());
        Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
        // The pulled player now shares the streamer's instance.
        await using var verify = _factory.Services.CreateAsyncScope();
        var presenceCheck = verify.ServiceProvider.GetRequiredService<PlayerPresenceService>();
        Assert.Equal(BigInstanceId, presenceCheck.GetRoom(pulled.PlayerId)!.RoomInstanceId);
    }

    [Fact]
    public async Task Pull_with_precision_mangled_id_is_rejected()
    {
        using var setup = ApiClient();
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var streamer = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var pulled = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        long roomId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            roomId = (await db.Rooms.FirstAsync(r => r.State == 0)).Id;
            await db.SaveChangesAsync();
            var presence = scope.ServiceProvider.GetRequiredService<PlayerPresenceService>();
            presence.SetRoom(streamer.PlayerId, new RoomInstanceDto
            {
                RoomInstanceId = BigInstanceId,
                RoomId = roomId,
                PhotonRoomId = "^teststreamerinstance2",
                PhotonRegionId = "us",
                MaxCapacity = 8,
            });
            presence.MarkActive(streamer.PlayerId);
        }

        using var adminClient = ApiClient(admin);
        adminClient.BaseAddress = new Uri($"http://admin.{_factory.ApexDomain}");

        // The value the admin SPA's JSON.parse actually produced for this
        // id (from the bug report log) — the exact failure mode.
        const long mangled = 7597291079085523000L;
        Assert.NotEqual(BigInstanceId, mangled);
        using var pull = await adminClient.PostAsync(
            $"/api/admin/v1/rooms/{roomId}/instances/{mangled}/pull",
            new StringContent(JsonSerializer.Serialize(new { PlayerId = pulled.PlayerId }),
                Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, pull.StatusCode);
    }

    private HttpClient ApiClient(GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
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
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
