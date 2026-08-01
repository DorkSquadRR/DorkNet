using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// While a join is pending, the heartbeat must tell the joining player which
/// instance they are joining — never null.
///
/// Deferring the presence commit is about what everyone ELSE sees: presence
/// stays uncommitted until the join is confirmed so a failed join leaves no
/// ghost behind. It must not change what the joining player is told about
/// themselves, because the client XORs (cachedRoomInstance != null) against
/// (serverRoomInstance != null): holding the target while the server says null
/// reads as the room vanishing mid-join. The client then logs "presence
/// heartbeat response indicates local presence is out-of-sync", "Attempting to
/// go to Invalid RoomInstance", and cancels the very join the server was
/// waiting on — so it retries until a heartbeat happens to miss the window, and
/// entering a dorm takes several attempts while looking like a load failure.
/// </summary>
public sealed class PendingJoinPresenceTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public PendingJoinPresenceTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Heartbeat_during_a_pending_dorm_join_reports_the_instance_being_joined()
    {
        using var client = Client("match");
        var session = await GameClientSessionFactory.CreateAsync(client, _factory.ApexDomain);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        // matchmake/dorm is what arms the deferred-commit pending join.
        using var dormForm = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("MaxPersistenceVersion", "45")]);
        using var dorm = await client.PostAsync("/matchmake/dorm", dormForm);
        var dormBody = await dorm.Content.ReadAsStringAsync();
        Assert.True(dorm.IsSuccessStatusCode,
            $"POST /matchmake/dorm -> {(int)dorm.StatusCode}: {dormBody}");

        var expectedInstance = JsonDocument.Parse(dormBody).RootElement;
        var instanceId = FindInstanceId(expectedInstance);
        Assert.True(instanceId is not null, $"no roomInstanceId in the dorm reply: {dormBody}");

        // The join has NOT been reported yet, so the commit is still deferred —
        // this is exactly the window the client heartbeats in.
        using var beatForm = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("LoginLock", Guid.NewGuid().ToString())]);
        using var beat = await client.PostAsync("/player/heartbeat", beatForm);
        var beatBody = await beat.Content.ReadAsStringAsync();
        Assert.True(beat.IsSuccessStatusCode,
            $"POST /player/heartbeat -> {(int)beat.StatusCode}: {beatBody}");

        var presence = JsonDocument.Parse(beatBody).RootElement;
        Assert.True(presence.TryGetProperty("roomInstance", out var reported)
                    || presence.TryGetProperty("RoomInstance", out reported),
            $"no roomInstance key in the heartbeat: {beatBody}");

        Assert.True(reported.ValueKind != JsonValueKind.Null,
            $"""
             The heartbeat returned a null roomInstance while the player's join was
             still pending. The client reads that as the room vanishing mid-join and
             cancels the load it is waiting on:

             {beatBody}
             """);

        Assert.Equal(instanceId, FindInstanceId(reported));
    }

    /// <summary>Pull roomInstanceId out of either the matchmake reply (which
    /// wraps it) or the heartbeat's roomInstance object, whatever the casing.</summary>
    private static string? FindInstanceId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("roomInstanceId", StringComparison.OrdinalIgnoreCase))
                return property.Value.ToString();

            if (property.Value.ValueKind == JsonValueKind.Object
                && FindInstanceId(property.Value) is { } nested)
                return nested;
        }
        return null;
    }

    private HttpClient Client(string host)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        return client;
    }
}
