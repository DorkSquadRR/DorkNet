using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Asserts our responses carry EXACTLY the keys the 2023-03-21 client's
/// generated readers register — no missing ones, no invented ones.
///
/// This is stricter than "the client doesn't crash" on purpose. A wrong key set
/// does not produce a bad request: the server answers HTTP 200, the reader
/// throws while deserialising, and it surfaces later as a bare "Failed to copy
/// room" with no URL and no payload logged on either side. By the time it is
/// visible there is nothing left to diagnose it with.
///
/// The expected sets are extracted straight from the binary by
/// <c>tools/extract-client-dto-keys.py</c> into
/// <c>data/client-dto-keys-2023.json</c>, keyed by the obfuscated reader type.
/// Cpp2IL emits each reader's property names as string literals in registration
/// order, so that file is the client's own contract rather than our reading of
/// it — regenerate it rather than editing it by hand.
/// </summary>
public sealed class ClientDtoParityTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public ClientDtoParityTests(DorkNetServerFactory factory) => _factory = factory;

    /// <summary>GLEGPFFPDBE — the room-details reader. This is what
    /// <c>GET rooms/{id}</c> and <c>POST rooms/{id}/clone</c> both deserialise
    /// into (clone's return type FGCPNAACHIK, NLDBPDCNNCF.txt:4676).</summary>
    private const string RoomDetailsReader = "GLEGPFFPDBE";

    /// <summary>EKEFFNBIOHJ — the chat-thread reader, shared by
    /// <c>POST thread/withmembers</c> and <c>GET thread/{id}</c>.</summary>
    private const string ChatThreadReader = "EKEFFNBIOHJ";

    /// <summary>Keys the reader registers that we deliberately do NOT send.
    ///
    /// A registered slot does not oblige us to fill it — Newtonsoft leaves an
    /// absent key at its default. These two were added for parity and broke
    /// the dorm: which blob a room loads is decided per INSTANCE (the matchmake
    /// response carries it, and a dorm's is per-player, dorm_p{id}_v*.dat) and
    /// per SUB-ROOM. A room-scoped answer to the same question is one the
    /// client preferred, and for a dorm it named a file that was never written,
    /// so the room came back "corrupt".
    ///
    /// Every room loaded fine while these were absent. Their absence is the
    /// fix, not a gap to close later — do not "restore parity" here without a
    /// dorm join to prove it.</summary>
    private static readonly HashSet<(string Reader, string Key)> DeliberatelyOmitted =
    [
        (RoomDetailsReader, "DataBlob"),
        (RoomDetailsReader, "DataBlobHash"),
    ];

    [Fact]
    public async Task Room_details_carries_exactly_the_keys_the_client_reads()
    {
        using var client = RoomsClient();
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var roomId = 9_600_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"Parity{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = session.PlayerId,
                CloningAllowed = true,
            });
            db.RoomScenes.Add(new RoomSceneEntity { RoomId = roomId, Name = "Home", OrderIndex = 0 });
            await db.SaveChangesAsync();
        }

        using var response = await client.GetAsync($"/rooms/{roomId}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET /rooms/{roomId} -> {(int)response.StatusCode}: {body}");

        AssertKeyParity(RoomDetailsReader, body, $"GET rooms/{roomId}");

        // The reader walks into the nested objects too, and a wrong key set
        // there fails exactly the same way — a 200 the client throws on.
        var details = JsonDocument.Parse(body).RootElement;
        foreach (var (property, reader) in NestedReaders)
        {
            if (!details.TryGetProperty(property, out var value)) continue;

            var element = value.ValueKind switch
            {
                JsonValueKind.Array when value.GetArrayLength() > 0 => value[0],
                JsonValueKind.Object => value,
                _ => default,
            };
            if (element.ValueKind != JsonValueKind.Object) continue;

            AssertKeyParity(reader, element.GetRawText(),
                $"GET rooms/{roomId} → {property}{(value.ValueKind == JsonValueKind.Array ? "[0]" : "")}");
        }
    }

    /// <summary>Nested objects in the room-details payload and the reader each
    /// one is deserialised by. Sub-room 0, the first role, the stats object and
    /// the first tag all have their own generated readers, and a key set that
    /// disagrees with them breaks the whole room just as hard as a wrong
    /// top-level one.</summary>
    private static readonly (string Property, string Reader)[] NestedReaders =
    [
        ("SubRooms", "JDEKAFDPGBE"),
        ("Roles", "LMEECKCCPHG"),
        ("Stats", "MLHBPDKBABN"),
        ("Tags", "GEGADPCDGBO"),
    ];

    [Fact]
    public async Task Chat_thread_carries_exactly_the_keys_the_client_reads()
    {
        using var client = ChatClient();
        using var otherClient = ChatClient();
        var starter = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        var other = await GameClientSessionFactory.CreateAsync(otherClient, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", starter.AccessToken);

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("ids", other.PlayerId.ToString()),
            new KeyValuePair<string, string>("messageCount", "50"),
        ]);
        using var response = await client.PostAsync("/thread/withmembers", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"POST /thread/withmembers -> {(int)response.StatusCode}: {body}");

        AssertKeyParity(ChatThreadReader, body, "POST thread/withmembers");
    }

    /// <summary>Compare one response object's keys against the reader's set.
    /// Matching is case-insensitive because the readers register three spellings
    /// per field (Pascal, camel, lower) and accept any of them — casing is the
    /// one thing they are NOT strict about.</summary>
    private static void AssertKeyParity(string readerType, string body, string what)
    {
        var expected = LoadReaderKeys(readerType)
            .Where(k => !DeliberatelyOmitted.Contains((readerType, k)))
            .ToList();
        var actual = JsonDocument.Parse(body).RootElement
            .EnumerateObject().Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expected.Where(k => !actual.Contains(k)).ToList();
        var extra = actual
            .Where(k => !expected.Contains(k, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"""
             {what} does not match what the client's {readerType} reader expects.

             MISSING ({missing.Count}) — the reader registers these and we never send them;
             a non-nullable field left null is what throws mid-deserialise:
               {string.Join(", ", missing)}

             UNEXPECTED ({extra.Count}) — we send these and the reader has no slot for them:
               {string.Join(", ", extra)}

             Expected, in the reader's own registration order:
               {string.Join(", ", expected)}
             """);
    }

    private static List<string> LoadReaderKeys(string readerType)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "client-dto-keys-2023.json");
        Assert.True(File.Exists(path),
            $"missing {path} — regenerate with tools/extract-client-dto-keys.py");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty(readerType, out var keys),
            $"{readerType} is not in the extracted catalogue");
        return keys.EnumerateArray().Select(k => k.GetString()!).ToList();
    }

    private HttpClient RoomsClient() => ClientFor("rooms");

    private HttpClient ChatClient() => ClientFor("chat");

    private HttpClient ClientFor(string host)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        return client;
    }
}
