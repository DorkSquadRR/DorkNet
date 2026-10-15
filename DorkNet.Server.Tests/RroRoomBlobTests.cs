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
        var roomName = $"Royale{Guid.NewGuid():N}"[..18];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;

            // Shape of a seeded baked original: system-owned, AG-flagged,
            // tagged recroomoriginal, no blob anywhere.
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = roomName,
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal,sport",
                CurrentDataBlobName = string.Empty,
                LocationReplicationId = "b010171f-4875-4e89-baba-61e878cd41e1",
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
        Assert.True(persisted.RoomRoleData.RoomRoles.All(r => r.CanUseMakerPen is { Overrides: true, InnerValue: true }));

        // The March-2023 client rejects a blob past PersistedRoomVersion 16
        // (the old stub was stamped 19 and loaded as "no room data"), and a
        // real save carries the room's location id as activity_id. Our proto
        // doesn't type activity_id, so look for it on the wire.
        Assert.Equal(RoomDataBlobService.Client2023MaxPersistedRoomVersion, (int)persisted.Version);
        var blobText = System.Text.Encoding.UTF8.GetString(blobBytes);
        Assert.Contains("b010171f-4875-4e89-baba-61e878cd41e1", blobText);

        // Every real room save carries three GameRoleNode chips (default role
        // named after the room, "In-Game", "Eliminated" — well-known GUIDs).
        // The 2023 client grants Maker Pen off the player's GameRole, so a
        // blob with no role objects leaves even a co-owner with nothing.
        Assert.Equal(3, CountTopLevelPersistenceViews(blobBytes));
        Assert.Contains("In-Game", blobText);
        Assert.Contains("Eliminated", blobText);
        Assert.Contains("2c721342-c7ed-49fe-b8f0-93f8d282a6df", blobText);
        Assert.Contains("0b9ec39e-2cc9-4825-8321-da11561f1c4f", blobText);
        Assert.Contains("c48bac02-a743-4797-94c4-e2d6f0749004", blobText);
        Assert.Contains(roomName, blobText);

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

    /// <summary>Rec Royale's Frontier scene has no baked game rule; real Rec
    /// Room delivered the RecRoyaleSolos/Squads_GameRuleWrapperCircuit chip
    /// in the blob. The stub for those rooms must carry it (a fourth
    /// persistence view with the palette tag the client generates).</summary>
    [Theory]
    [InlineData("RecRoyaleSolos", "recroyalesolosgamerulecir")]
    [InlineData("RecRoyaleSquads", "recroyalesquadsgameruleci")]
    public async Task Rec_royale_stub_carries_its_game_rule_chip(string roomNamePrefix, string expectedTag)
    {
        using var setup = Client("rooms");
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_680_000 + Random.Shared.Next(1, 99_999);
        var roomName = $"{roomNamePrefix}-{Guid.NewGuid():N}"[..24];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = roomName,
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal",
                LocationReplicationId = "253fa009-6e65-4c90-91a1-7137a56a267f",
            });
            await db.SaveChangesAsync();
        }

        using var adminClient = Client("admin", admin);
        using var cdnClient = Client("cdn");
        await PostJsonAsync(adminClient, $"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/enable");

        var synthetic = RoomService.SyntheticDefaultRoomDataBlobName(roomId);
        using var blobResponse = await cdnClient.GetAsync($"/room/{synthetic}");
        Assert.True(blobResponse.IsSuccessStatusCode);
        var blobBytes = await blobResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(4, CountTopLevelPersistenceViews(blobBytes)); // 3 roles + the rule chip
        Assert.Contains(expectedTag, System.Text.Encoding.UTF8.GetString(blobBytes));
    }

    /// <summary>A real save from any client version can be attached to a baked
    /// original: activity id rewritten to the target room, output log /
    /// sub-room id / unknown fields dropped, version clamped, and in
    /// <c>basics</c> mode only the role + rule objects kept with circuits
    /// stripped. The room then loads that blob (with the CDN's role overlay).</summary>
    [Fact]
    public async Task Uploading_a_real_save_attaches_it_to_the_room_rewritten_for_2023()
    {
        using var setup = Client("rooms");
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var admin = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var roomId = 9_690_000 + Random.Shared.Next(1, 99_999);
        const string location = "253fa009-6e65-4c90-91a1-7137a56a267f";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            (await db.Players.FirstAsync(p => p.Id == admin.PlayerId)).IsAdmin = true;
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Upload{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = 1,
                IsAGRoom = true,
                TagsCsv = "recroomoriginal",
                LocationReplicationId = location,
            });
            await db.SaveChangesAsync();
        }

        // A "modern" save: version 138, someone else's activity id, an output
        // log, a sub-room id, a newer-only field 37, circuit payloads, and
        // three views — a game role (field 50), a game rule (field 41) and a
        // plain prop (field 15 only).
        var modern = BuildModernSave();

        using var adminClient = Client("admin", admin);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(modern), "file", "persisted_room_data.binpb");
        using var uploaded = await adminClient.PostAsync($"/api/admin/v1/rooms/{roomId}/subrooms/0/blob/upload?mode=basics", form);
        var uploadBody = await uploaded.Content.ReadAsStringAsync();
        Assert.True(uploaded.IsSuccessStatusCode, $"upload -> {(int)uploaded.StatusCode}: {uploadBody}");
        var result = JsonDocument.Parse(uploadBody).RootElement;
        var blobName = result.GetProperty("dataBlobName").GetString()!;
        Assert.StartsWith($"room_{roomId}_upload_", blobName);
        var summary = result.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("viewsIn").GetInt32());
        Assert.Equal(2, summary.GetProperty("viewsKept").GetInt32());
        Assert.Equal(138, summary.GetProperty("versionIn").GetInt32());
        Assert.Equal(16, summary.GetProperty("versionOut").GetInt32());

        // The game client is now pointed at the uploaded blob …
        using var gameClient = Client("rooms", player);
        var details = await GetJsonAsync(gameClient, $"/rooms/{roomId}");
        Assert.Equal(blobName, SubRoomZero(details).GetProperty("DataBlob").GetString());

        // … and the CDN serves it rewritten: 2 views, clamped header, our
        // activity id, the role overlay applied, no field 37 / output log.
        using var cdnClient = Client("cdn");
        using var served = await cdnClient.GetAsync($"/room/{blobName}");
        Assert.True(served.IsSuccessStatusCode, $"cdn -> {(int)served.StatusCode}");
        var bytes = await served.Content.ReadAsByteArrayAsync();
        var parsed = PersistedRoomData.Parser.ParseFrom(bytes);
        Assert.Equal(RoomDataBlobService.Client2023MaxPersistedRoomVersion, (int)parsed.Version);
        Assert.NotEmpty(parsed.RoomRoleData.RoomRoles);
        Assert.Equal(2, CountTopLevelPersistenceViews(bytes));
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(location, text);
        Assert.DoesNotContain("someone-elses-activity", text);
        Assert.DoesNotContain("Serialized 3 persistence views", text);
    }

    private static byte[] BuildModernSave()
    {
        var ms = new MemoryStream();
        void Varint(int field, ulong v) { WriteVarint(ms, ((ulong)field << 3) | 0); WriteVarint(ms, v); }
        void Bytes(int field, byte[] payload) { WriteVarint(ms, ((ulong)field << 3) | 2); WriteVarint(ms, (ulong)payload.Length); ms.Write(payload); }
        static byte[] View(int markerField)
        {
            var v = new MemoryStream();
            WriteVarint(v, (1UL << 3) | 2); WriteVarint(v, 16); v.Write(Guid.NewGuid().ToByteArray());
            WriteVarint(v, ((ulong)markerField << 3) | 2); WriteVarint(v, 0);
            return v.ToArray();
        }
        Varint(1, 38);
        Bytes(2, View(50));  // game role
        Bytes(2, View(41));  // game rule wrapper
        Bytes(2, View(15));  // a prop (tagged tool only)
        Bytes(4, System.Text.Encoding.UTF8.GetBytes("someone-elses-activity-0000-000000000000"));
        Bytes(5, System.Text.Encoding.UTF8.GetBytes("Serialized 3 persistence views"));
        Varint(6, 3991471883933115623UL);
        Bytes(18, new byte[] { 0x0A, 0x02, 0x08, 0x01 });
        Bytes(28, new byte[] { 0x0A, 0x02, 0x08, 0x01 });
        Varint(30, 138);
        Varint(31, 1);
        Varint(37, 1);
        return ms.ToArray();
    }

    private static void WriteVarint(Stream s, ulong value)
    {
        while (value >= 0x80) { s.WriteByte((byte)(value | 0x80)); value >>= 7; }
        s.WriteByte((byte)value);
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

    /// <summary>Count top-level <c>persistence_views</c> (field 2) entries by
    /// walking the wire format — our server proto doesn't type field 2.</summary>
    private static int CountTopLevelPersistenceViews(byte[] blob)
    {
        var pos = 0;
        var count = 0;
        while (pos < blob.Length)
        {
            var tag = ReadVarint(blob, ref pos);
            var field = (int)(tag >> 3);
            var wireType = (int)(tag & 7);
            switch (wireType)
            {
                case 0: ReadVarint(blob, ref pos); break;
                case 1: pos += 8; break;
                case 2:
                    var len = (int)ReadVarint(blob, ref pos);
                    if (field == 2) count++;
                    pos += len;
                    break;
                case 5: pos += 4; break;
                default: throw new InvalidDataException($"wire type {wireType}");
            }
        }
        return count;
    }

    private static ulong ReadVarint(byte[] input, ref int pos)
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            var b = input[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
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
