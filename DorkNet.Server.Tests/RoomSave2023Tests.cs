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

        // 2023 commit-response contract (NEOPBOMGIOG): value must carry
        // BOTH a Room and a SubRoomDataSave or the client's post-parse
        // Dispose walk NREs and the watch shows "Failed to save room".
        var value = json.RootElement.GetProperty("value");
        var roomDto = value.GetProperty("Room");
        Assert.Equal(JsonValueKind.Object, roomDto.ValueKind);
        // Keys the FGCPNAACHIK mapper reads and its Dispose walk derefs.
        foreach (var key in new[] { "RoomId", "DataBlob", "MaxPlayers", "ToxmodEnabled", "RankingContext", "SubRooms", "Roles", "Tags", "Stats" })
            Assert.True(roomDto.TryGetProperty(key, out _), $"Room missing '{key}': {responseText}");
        Assert.Equal(JsonValueKind.Object, roomDto.GetProperty("RankingContext").ValueKind);

        var save = value.GetProperty("SubRoomDataSave");
        Assert.Equal(blobName, save.GetProperty("DataBlob").GetString());
        // Keys the JKIFFPPAJNK save mapper reads.
        foreach (var key in new[] { "SubRoomDataSaveId", "SubRoomId", "UnityAssetId", "DataBlobHash", "SavedByAccountId", "Description", "CreatedAt" })
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
