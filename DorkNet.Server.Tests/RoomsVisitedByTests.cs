using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

public sealed class RoomsVisitedByTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomsVisitedByTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Continue_rooms_hide_private_rooms_from_non_owners()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);
        var visitor = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var publicRoomId = 9_100_001L;
        var privateRoomId = 9_100_002L;
        var publicRoomName = $"ContinuePublic_{suffix}";
        var privateRoomName = $"ContinuePrivate_{suffix}";
        var now = DateTime.UtcNow;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.AddRange(
                BuildRoom(publicRoomId, publicRoomName, owner.PlayerId, accessibility: 1),
                BuildRoom(privateRoomId, privateRoomName, owner.PlayerId, accessibility: 0));
            db.RoomVisits.AddRange(
                BuildVisit(publicRoomId, visitor.PlayerId, now.AddMinutes(-2)),
                BuildVisit(privateRoomId, visitor.PlayerId, now.AddMinutes(-1)),
                BuildVisit(privateRoomId, owner.PlayerId, now));
            await db.SaveChangesAsync();
        }

        using var visitorClient = ApiClient(visitor);
        var visitorNames = await GetContinueRoomNamesAsync(visitorClient);

        Assert.Contains(publicRoomName, visitorNames);
        Assert.DoesNotContain(privateRoomName, visitorNames);

        using var ownerClient = ApiClient(owner);
        var ownerNames = await GetContinueRoomNamesAsync(ownerClient);

        Assert.Contains(privateRoomName, ownerNames);
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

    private static async Task<string[]> GetContinueRoomNamesAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/rooms/visitedby/me");
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        return document.RootElement
            .EnumerateArray()
            .Select(room => room.GetProperty("Name").GetString() ?? string.Empty)
            .ToArray();
    }

    private static RoomEntity BuildRoom(long id, string name, long creatorPlayerId, int accessibility) => new()
    {
        Id = id,
        Name = name,
        Description = name,
        CreatorPlayerId = creatorPlayerId,
        ImageName = RoomService.DefaultRoomImageName,
        State = 0,
        Accessibility = accessibility,
        IsAGRoom = true,
        IsDormRoom = false,
        HiddenFromBrowse = false,
        TagsCsv = "community",
    };

    private static RoomVisitEntity BuildVisit(long roomId, long playerId, DateTime visitedAt) => new()
    {
        RoomId = roomId,
        PlayerId = playerId,
        FirstVisitAt = visitedAt,
        LastVisitAt = visitedAt,
        VisitCount = 1,
    };
}
