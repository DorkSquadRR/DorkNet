using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DorkNet.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// The 2023 "copy room" flow POSTs <c>rooms/{id}/clone</c> and deserialises the
/// reply into <c>FGCPNAACHIK</c> — the SAME type <c>GET rooms/{id}</c> returns
/// (RecNet.Runtime/NLDBPDCNNCF.txt:4676 declares
/// <c>Task&lt;FGCPNAACHIK&gt; GDHIIAHCBMN(Int64, String, CancellationToken)</c>,
/// with the route literal on :4802).
///
/// So the two responses must be shape-identical. When they drift, the server
/// still answers 200 and the client throws while reading it — and because the
/// clone promise's exception is never observed, it surfaces from the finalizer
/// thread seconds later as a bare "Failed to copy room: Exception of type
/// '...' was thrown", with nothing in the server log to show for it. That is
/// exactly how this failed in the wild: the room WAS created, and the client
/// simply never registered it.
/// </summary>
public sealed class RoomCloneShapeTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomCloneShapeTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Clone_response_matches_the_get_by_id_shape()
    {
        using var client = Client();
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var sourceId = 9_400_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = sourceId,
                Name = $"CloneSrc{Guid.NewGuid():N}"[..20],
                CreatorPlayerId = session.PlayerId,
                CloningAllowed = true,
            });
            // Give the source a real sub-room. Without one, BOTH responses fall
            // back to the synthesised default sub-room and the comparison never
            // exercises the branch a real room (like the seeded RecCenter the
            // room-creation picker clones) actually takes.
            db.RoomScenes.Add(new RoomSceneEntity
            {
                RoomId = sourceId,
                Name = "Home",
                OrderIndex = 0,
            });
            await db.SaveChangesAsync();
        }

        var getBody = await ReadJsonAsync(client, HttpMethod.Get, $"/rooms/{sourceId}");
        var cloneBody = await ReadJsonAsync(client, HttpMethod.Post, $"/rooms/{sourceId}/clone",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("name", $"Copy{Guid.NewGuid():N}"[..20])]));

        var expected = getBody.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var actual = cloneBody.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var missing = expected.Except(actual).Order(StringComparer.Ordinal).ToList();

        // The reader walks the nested sub-room list too, so a key missing there
        // breaks it just as hard as one missing at the top level.
        foreach (var list in new[] { "SubRooms", "Scenes" })
        {
            if (!getBody.TryGetProperty(list, out var expectedList) ||
                expectedList.ValueKind != JsonValueKind.Array ||
                expectedList.GetArrayLength() == 0) continue;

            Assert.True(cloneBody.TryGetProperty(list, out var actualList)
                        && actualList.ValueKind == JsonValueKind.Array,
                $"clone response has no {list} array while get-by-id does");
            Assert.True(actualList.GetArrayLength() > 0,
                $"clone response has an EMPTY {list} array while get-by-id returns "
                + $"{expectedList.GetArrayLength()}; the client's reader requires at least one entry");

            var expectedKeys = expectedList[0].EnumerateObject().Select(p => p.Name);
            var actualKeys = actualList[0].EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            missing.AddRange(expectedKeys.Where(k => !actualKeys.Contains(k)).Select(k => $"{list}[0].{k}"));
        }
        Assert.True(missing.Count == 0,
            $"""
             The clone response is missing {missing.Count} key(s) that get-by-id
             returns. Both deserialize into FGCPNAACHIK, so the client throws while
             reading the clone and silently never registers the new room:

             {string.Join(Environment.NewLine, missing)}

             clone keys: {string.Join(", ", actual.Order(StringComparer.Ordinal))}
             """);
    }

    /// <summary>A clone must own its save data. Cloning used to be a shallow
    /// copy — the new room's scenes kept pointing at the SOURCE's blob until
    /// someone saved over them — so every copy of a room shared one object and
    /// cloning RecCenter produced rooms whose data blob was literally
    /// <c>room_100_v1.dat</c>. A clone that references another room's blob is
    /// the bug, whatever the name.</summary>
    [Fact]
    public async Task Clone_does_not_reference_the_source_rooms_blob()
    {
        // The suite runs with S3 unconfigured, where there is nothing to copy
        // and the clone deliberately keeps the shallow reference. Swap in a
        // real (in-memory) store so this exercises the copy path itself.
        var store = new InMemoryObjectStorage();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<IObjectStorage>(store);
            }));

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://rooms.{_factory.ApexDomain}");
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var sourceId = 9_500_000 + Random.Shared.Next(1, 99_999);
        var sourceBlob = $"room_{sourceId}_v1.dat";
        var sourceBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var (srcBucket, srcKey) = BlobRouter.Route(sourceBlob);
        await store.PutAsync(srcBucket, srcKey, sourceBytes, "application/octet-stream");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = sourceId,
                Name = $"BlobSrc{Guid.NewGuid():N}"[..20],
                CreatorPlayerId = session.PlayerId,
                CloningAllowed = true,
                CurrentDataBlobName = sourceBlob,
            });
            db.RoomScenes.Add(new RoomSceneEntity
            {
                RoomId = sourceId,
                Name = "Home",
                OrderIndex = 0,
                DataBlobName = sourceBlob,
            });
            await db.SaveChangesAsync();
        }

        var clone = await ReadJsonAsync(client, HttpMethod.Post, $"/rooms/{sourceId}/clone",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("name", $"Copy{Guid.NewGuid():N}"[..20])]));
        var cloneId = clone.GetProperty("RoomId").GetInt64();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var room = await db.Rooms.FirstAsync(r => r.Id == cloneId);
            var scenes = await db.RoomScenes.Where(s => s.RoomId == cloneId).ToListAsync();

            Assert.False(room.CurrentDataBlobName == sourceBlob,
                $"the clone's room row still points at the source's blob ({sourceBlob})");
            foreach (var scene in scenes)
                Assert.False(scene.DataBlobName == sourceBlob,
                    $"cloned sub-room {scene.OrderIndex} still points at the source's blob ({sourceBlob})");

            // It must be a real copy, not just a cleared reference — the point
            // is that the player's content comes along.
            Assert.False(string.IsNullOrEmpty(room.CurrentDataBlobName),
                "the clone has no blob at all; the source's data should have been copied, not dropped");
            var (dstBucket, dstKey) = BlobRouter.Route(room.CurrentDataBlobName);
            Assert.Equal(sourceBytes, await store.GetAsync(dstBucket, dstKey));
        }
    }

    /// <summary>Minimal in-memory <see cref="IObjectStorage"/>. The suite runs
    /// with S3 unconfigured, so without this the clone's copy path short-circuits
    /// and never gets exercised.</summary>
    private sealed class InMemoryObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public bool IsS3Configured => true;

        public Task<long> PutAsync(
            string bucket, string key, byte[] bytes, string contentType, CancellationToken ct = default)
        {
            _objects[$"{bucket}/{key}"] = bytes;
            return Task.FromResult((long)bytes.Length);
        }

        public Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(_objects.TryGetValue($"{bucket}/{key}", out var bytes) ? bytes : null);

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(_objects.ContainsKey($"{bucket}/{key}"));
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpClient client, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"{method} {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://rooms.{_factory.ApexDomain}");
        return client;
    }
}
