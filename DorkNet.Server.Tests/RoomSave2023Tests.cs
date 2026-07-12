using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// 2023 room-save flow (captured from the 2023.03.21 client against dev,
/// 2026-07-07). The client uploads two blobs to storage /upload —
/// FileType=6 room metadata, then FileType=1 scene save — and commits
/// them with a JSON POST to rooms/{id}/subrooms/{id}/data:
///   { "UnityAssetId": null,
///     "RoomData":    { "Filename": "...", "Hash": null, "OwnershipProof": null },
///     "SubRoomData": { "Filename": "...", "Hash": null, "OwnershipProof": null } }
/// SubRoomData.Filename is the scene blob and must become the room's
/// CurrentDataBlobName.
/// </summary>
public sealed class RoomSave2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomSave2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task SubRoomData_accepts_2023_nested_json_and_commits_blob()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_200_001L;
        var blobName = $"room_{roomId}_v1_{Guid.NewGuid():N}.dat";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            if (!await db.Rooms.AnyAsync(r => r.Id == roomId))
            {
                db.Rooms.Add(new RoomEntity
                {
                    Id = roomId,
                    Name = $"Save2023_{Guid.NewGuid():N}"[..24],
                    Description = "2023 save flow test room",
                    CreatorPlayerId = owner.PlayerId,
                    ImageName = RoomService.DefaultRoomImageName,
                    Accessibility = 1,
                    IsAGRoom = true,
                    IsDormRoom = false,
                });
                await db.SaveChangesAsync();
            }
            else
            {
                await db.Rooms.Where(r => r.Id == roomId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorPlayerId, owner.PlayerId));
            }
        }

        using var roomsClient = ApiClient(owner, subdomain: "rooms");
        var body = new
        {
            UnityAssetId = (long?)null,
            RoomData = new { Filename = "roommeta_test.bin", Hash = (string?)null, OwnershipProof = (string?)null },
            SubRoomData = new { Filename = blobName, Hash = (string?)null, OwnershipProof = (string?)null },
        };
        using var response = await roomsClient.PostAsync(
            $"/rooms/{roomId}/subrooms/0/data",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), responseText);

        // 2023 commit-response contract (NEOPBOMGIOG): value carries a
        // Room and a SubRoomDataSave. The Room reuses the exact GET
        // rooms/{id} shape (BuildRoomServerDetails) — do NOT bolt extra
        // keys onto it: the room-details mapper is strict about value
        // shapes and an invented RankingContext object broke every
        // room-details parse ("Malformed Response"). See befd590 revert.
        var value = json.RootElement.GetProperty("value");
        var roomDto = value.GetProperty("Room");
        Assert.Equal(JsonValueKind.Object, roomDto.ValueKind);
        foreach (var key in new[] { "RoomId", "SubRooms", "Roles", "Tags", "Stats" })
            Assert.True(roomDto.TryGetProperty(key, out _), $"Room missing '{key}': {responseText}");

        var save = value.GetProperty("SubRoomDataSave");
        Assert.Equal(blobName, save.GetProperty("DataBlob").GetString());
        foreach (var key in new[] { "SubRoomDataSaveId", "SubRoomId", "UnityAssetId", "SavedByAccountId", "CreatedAt" })
            Assert.True(save.TryGetProperty(key, out _), $"SubRoomDataSave missing '{key}': {responseText}");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var room = await db.Rooms.AsNoTracking().SingleAsync(r => r.Id == roomId);
            Assert.Equal(blobName, room.CurrentDataBlobName);
        }
    }

    [Fact]
    public async Task Upload_fileType6_room_metadata_is_stored_not_stubbed()
    {
        using var setupClient = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        using var storageClient = ApiClient(player, subdomain: "storage");
        using var form = new MultipartFormDataContent("BestHTTP_HTTPMultiPartForm_TEST");
        var file = new ByteArrayContent(new byte[] { 0x0A, 0x02, 0x08, 0x01 });
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "File", "file.bin");
        form.Add(new StringContent("6"), "FileType");

        using var response = await storageClient.PostAsync("/upload", form);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {responseText}");

        using var json = JsonDocument.Parse(responseText);
        var filename = json.RootElement.GetProperty("filename").GetString();
        Assert.NotNull(filename);
        Assert.StartsWith("roommeta_", filename);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        Assert.True(
            await db.RoomDataBlobs.AnyAsync(b => b.BlobName == filename),
            $"expected a RoomDataBlobs row for {filename}");
    }

    /// <summary>
    /// Regression: cloning a room — and then cloning the CLONE — must both
    /// succeed and return the full room-details shape (FGCPNAACHIK). Previously
    /// the handler relied on DB auto-increment for the room id AND the copied
    /// scene ids, which collide with the seeded id range once the identity
    /// sequence lags → the clone POST 500s and the client boots the player to
    /// their dorm ("Failed to copy room: Failed to clone room").
    /// </summary>
    [Fact]
    public async Task Cloning_a_room_and_then_the_clone_both_succeed()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_300_777L;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            if (!await db.Rooms.AnyAsync(r => r.Id == roomId))
            {
                db.Rooms.Add(new RoomEntity
                {
                    Id = roomId,
                    Name = $"CloneSrc_{Guid.NewGuid():N}"[..20],
                    Description = "clone source",
                    CreatorPlayerId = owner.PlayerId,
                    ImageName = RoomService.DefaultRoomImageName,
                    Accessibility = 1,
                    IsAGRoom = false,
                    IsDormRoom = false,
                    CloningAllowed = true,
                    CurrentDataBlobName = $"room_{roomId}_v1.dat",
                });
                db.RoomScenes.Add(new RoomSceneEntity
                {
                    RoomId = roomId,
                    OrderIndex = 0,
                    Name = "Main",
                    DataBlobName = $"room_{roomId}_v1.dat",
                });
                await db.SaveChangesAsync();
            }
        }

        using var roomsClient = ApiClient(owner, subdomain: "rooms");

        var firstCloneId = await CloneAndAssertAsync(roomsClient, roomId, "FirstClone");
        Assert.NotEqual(roomId, firstCloneId);

        // The regression: cloning the clone.
        var secondCloneId = await CloneAndAssertAsync(roomsClient, firstCloneId, "SecondClone");
        Assert.NotEqual(firstCloneId, secondCloneId);
    }

    private static async Task<long> CloneAndAssertAsync(HttpClient client, long sourceId, string name)
    {
        // The real 2023 client posts the name as x-www-form-urlencoded
        // (`name=...`), NOT JSON — testing with JSON would miss the 415 that
        // a [FromBody] handler returns for a form POST.
        using var resp = await client.PostAsync(
            $"/rooms/{sourceId}/clone",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = name }));
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"clone of {sourceId} -> {(int)resp.StatusCode}: {text}");

        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        // Full room-details shape (FGCPNAACHIK), not a status wrapper.
        foreach (var key in new[] { "RoomId", "SubRooms", "Roles" })
            Assert.True(root.TryGetProperty(key, out _), $"clone response missing '{key}': {text}");
        return root.GetProperty("RoomId").GetInt64();
    }

    private HttpClient ApiClient(GameClientSession? session = null, string subdomain = "api")
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{subdomain}.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
