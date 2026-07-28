using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
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
