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
    ///
    /// The first clone goes through the <c>roomserver/</c>-prefixed route:
    /// the 2023 in-room "copy room" flow (RecNet.Runtime NLDBPDCNNCF) sends
    /// that prefix like every other room mutation, and the bare-only route
    /// 404'd with an empty body ("Failed to copy room: Exception of type
    /// '…' was thrown").
    ///
    /// Blob rules: cloning a FIRST-PARTY template (AG + system-owned) must
    /// give the clone a FRESH empty blob — the template's blob is a MakerPen
    /// overlay against the shared baked scene, not the clone's content.
    /// Cloning a USER-owned room (even AG-flagged) must carry its blob so
    /// "copy room" keeps the player's edits.
    /// </summary>
    [Fact]
    public async Task Cloning_a_room_and_then_the_clone_both_succeed()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_300_777L;
        var templateBlob = $"room_{roomId}_v1.dat";
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
                    // A first-party RRO/AG base room (RecCenter-like):
                    // system-owned, CloningAllowed=false, yet the base-room
                    // picker must still be able to clone it. Its blob is a
                    // MakerPen overlay that must NOT leak into clones.
                    CreatorPlayerId = PlayerService.SystemAccountId,
                    ImageName = RoomService.DefaultRoomImageName,
                    Accessibility = 1,
                    IsAGRoom = true,
                    IsDormRoom = false,
                    CloningAllowed = false,
                    CurrentDataBlobName = templateBlob,
                });
                db.RoomScenes.Add(new RoomSceneEntity
                {
                    RoomId = roomId,
                    OrderIndex = 0,
                    Name = "Main",
                    DataBlobName = templateBlob,
                });
                await db.SaveChangesAsync();
            }
        }

        using var roomsClient = ApiClient(owner, subdomain: "rooms");

        // First clone: template → player copy, via the roomserver/ prefix the
        // real client uses.
        var firstCloneId = await CloneAndAssertAsync(
            roomsClient, roomId, "FirstClone", pathPrefix: "/roomserver");
        Assert.NotEqual(roomId, firstCloneId);
        // A clone of a first-party template used to be left with NO blob, on
        // the reasoning that its content is baked client geometry. That left
        // the copy with nothing to load: the CDN synthesises a ~1.8 KB stub for
        // a name it has nothing stored under, and the client fetches exactly
        // that through GetRoomData ("/room/{blob}") while copying, then rejects
        // it. Clones that carried real content joined fine.
        //
        // The content now comes across. Sharing the SOURCE's object is still
        // wrong, but that is prevented by copying it under the clone's own name
        // rather than by having no blob at all — see
        // RoomCloneShapeTests.Clone_does_not_reference_the_source_rooms_blob,
        // which runs with a real object store. This suite has none, so the
        // copy deliberately falls back to keeping the reference and only the
        // "has content" half is checkable here.
        await AssertBlobsAsync(firstCloneId, expectEmpty: false,
            "a clone of a first-party template must carry its content");

        // Simulate the player saving MakerPen edits into their clone.
        var editedBlob = $"room_{firstCloneId}_edit_{Guid.NewGuid():N}.dat";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var room = await db.Rooms.SingleAsync(r => r.Id == firstCloneId);
            room.CurrentDataBlobName = editedBlob;
            var scene = await db.RoomScenes.SingleAsync(s => s.RoomId == firstCloneId);
            scene.DataBlobName = editedBlob;
            await db.SaveChangesAsync();
        }

        // The regression: cloning the clone (bare path still works too).
        // The source is user-owned — even though it inherited IsAGRoom, the
        // player's edits must carry over.
        var secondCloneId = await CloneAndAssertAsync(roomsClient, firstCloneId, "SecondClone");
        Assert.NotEqual(firstCloneId, secondCloneId);
        await AssertBlobsAsync(secondCloneId, expectEmpty: false,
            "copying a user-owned room must keep its blob (player edits)");
    }

    private async Task AssertBlobsAsync(long roomId, bool expectEmpty, string why)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var room = await db.Rooms.SingleAsync(r => r.Id == roomId);
        var scene = await db.RoomScenes.SingleAsync(s => s.RoomId == roomId);
        if (expectEmpty)
        {
            Assert.True(string.IsNullOrEmpty(room.CurrentDataBlobName), $"{why}: room blob was '{room.CurrentDataBlobName}'");
            Assert.True(string.IsNullOrEmpty(scene.DataBlobName), $"{why}: scene blob was '{scene.DataBlobName}'");
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(room.CurrentDataBlobName), $"{why}: room blob was empty");
            Assert.False(string.IsNullOrEmpty(scene.DataBlobName), $"{why}: scene blob was empty");
        }
    }

    private static async Task<long> CloneAndAssertAsync(
        HttpClient client, long sourceId, string name, string pathPrefix = "")
    {
        // The real 2023 client posts the name as x-www-form-urlencoded
        // (`name=...`), NOT JSON — testing with JSON would miss the 415 that
        // a [FromBody] handler returns for a form POST.
        using var resp = await client.PostAsync(
            $"{pathPrefix}/rooms/{sourceId}/clone",
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

    /// <summary>
    /// Regression: <c>GET rooms/{id}/subrooms/{sub}/saves</c> must return the
    /// paged wrapper <c>{"Results":[…],"TotalResults":N}</c>
    /// (PagedResultsDTO&lt;SubRoomDataSaveDTO&gt;), NOT a bare array. The 2023
    /// client's GetSubRoomDataSaves strict reader throws
    /// <c>expected:'{', actual:'['</c> on an array, which aborts the whole
    /// calling flow — room clone fails with the message-less "Failed to copy
    /// room" and the private-instance button silently does nothing.
    /// </summary>
    [Fact]
    public async Task SubRoom_saves_returns_paged_object_not_bare_array()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_300_888L;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            if (!await db.Rooms.AnyAsync(r => r.Id == roomId))
            {
                db.Rooms.Add(new RoomEntity
                {
                    Id = roomId,
                    Name = $"SavesShape_{Guid.NewGuid():N}"[..20],
                    CreatorPlayerId = owner.PlayerId,
                    ImageName = RoomService.DefaultRoomImageName,
                    Accessibility = 1,
                    CurrentDataBlobName = $"room_{roomId}_v1.dat",
                });
                db.RoomScenes.Add(new RoomSceneEntity
                {
                    RoomId = roomId,
                    OrderIndex = 0,
                    Name = "Home",
                    DataBlobName = $"room_{roomId}_v1.dat",
                });
                await db.SaveChangesAsync();
            }
        }

        using var roomsClient = ApiClient(owner, subdomain: "rooms");
        foreach (var path in new[]
        {
            $"/roomserver/rooms/{roomId}/subrooms/0/saves?skip=0&take=10",
            $"/rooms/{roomId}/subrooms/0/saves",
            $"/roomserver/rooms/{roomId}/subrooms/0/saves/no_unity_assets?skip=0&take=10",
        })
        {
            using var resp = await roomsClient.GetAsync(path);
            var text = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode, $"{path} -> {(int)resp.StatusCode}: {text}");
            using var json = JsonDocument.Parse(text);
            Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("Results").ValueKind);
            Assert.True(json.RootElement.GetProperty("TotalResults").GetInt64() >= 1, $"TotalResults missing/zero: {text}");
        }
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
