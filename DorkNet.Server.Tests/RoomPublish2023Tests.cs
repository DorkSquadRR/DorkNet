using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// "Publish room" in the 2023 client: RoomModel.TryPublishRoom →
/// <c>PUT roomserver/rooms/{id}/accessibility</c> with form
/// <c>accessibility=1</c> (boxed Int32, NLDBPDCNNCF_NestedType_OMHIIEAKHCH),
/// typed <c>Task&lt;FGCPNAACHIK&gt;</c>. The button only exists when
/// get_CanPublishRoom holds: role ≥ CoOwner, not a dorm, <b>not IsRRO</b>,
/// not already public. A clone of a Rec Room Original template used to be
/// advertised as IsRRO and so could never be published.
/// </summary>
public sealed class RoomPublish2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomPublish2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public void Only_system_owned_originals_are_RRO_on_the_wire()
    {
        Assert.True(RoomService.WireIsRRO(new RoomEntity { IsAGRoom = true, CreatorPlayerId = PlayerService.SystemAccountId }));
        Assert.False(RoomService.WireIsRRO(new RoomEntity { IsAGRoom = true, CreatorPlayerId = 4242 }));
        Assert.False(RoomService.WireIsRRO(new RoomEntity { IsAGRoom = false, CreatorPlayerId = PlayerService.SystemAccountId }));
    }

    [Fact]
    public async Task Clone_of_an_original_is_not_RRO_and_can_be_published_by_owner_and_co_owner()
    {
        using var setup = Client("api");
        var owner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var coOwner = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var moderator = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        var templateId = 9_100_000 + Random.Shared.Next(1, 99_999);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = templateId,
                Name = $"Tmpl{Guid.NewGuid():N}"[..18],
                CreatorPlayerId = PlayerService.SystemAccountId,
                IsAGRoom = true,
                Accessibility = 1,
                CloningAllowed = false,
                ImageName = "image_RecCenter.png",
                LocationReplicationId = "a75f7547-79eb-47c6-8986-6767abcb4f92",
            });
            db.RoomScenes.Add(new RoomSceneEntity { RoomId = templateId, OrderIndex = 0, Name = "Home" });
            await db.SaveChangesAsync();
        }

        using var ownerRooms = Client("rooms", owner);
        var template = await GetJsonAsync(ownerRooms, $"/roomserver/rooms/{templateId}");
        Assert.True(template.GetProperty("IsRRO").GetBoolean());

        using var cloneResp = await ownerRooms.PostAsync($"/roomserver/rooms/{templateId}/clone",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = $"Pub{Guid.NewGuid():N}"[..20] }));
        var cloneText = await cloneResp.Content.ReadAsStringAsync();
        Assert.True(cloneResp.IsSuccessStatusCode, cloneText);
        var clone = JsonDocument.Parse(cloneText).RootElement.GetProperty("value").Clone();
        var cloneId = clone.GetProperty("RoomId").GetInt64();
        Assert.False(clone.GetProperty("IsRRO").GetBoolean());
        Assert.Equal(0, clone.GetProperty("Accessibility").GetInt32());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.RoomRoles.Add(new RoomRoleEntity { RoomId = cloneId, PlayerId = coOwner.PlayerId, Role = RoomRoleRules.StoredCoOwner, Accepted = true });
            db.RoomRoles.Add(new RoomRoleEntity { RoomId = cloneId, PlayerId = moderator.PlayerId, Role = RoomRoleRules.StoredModerator, Accepted = true });
            await db.SaveChangesAsync();
        }

        // Publish as the owner: full room details back, now public, still not RRO.
        var published = await PutAccessibilityAsync(ownerRooms, cloneId, "1");
        Assert.Equal(1, published.GetProperty("Accessibility").GetInt32());
        Assert.False(published.GetProperty("IsRRO").GetBoolean());
        Assert.True(published.TryGetProperty("SubRooms", out _));

        // A moderator may not change accessibility; a co-owner may.
        using var modRooms = Client("rooms", moderator);
        using (var denied = await modRooms.PutAsync($"/roomserver/rooms/{cloneId}/accessibility",
                   new FormUrlEncodedContent(new Dictionary<string, string> { ["accessibility"] = "0" })))
        {
            // Refusals are 200 {success:false, error} so the client can show them.
            using var doc = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        }
        using var coRooms = Client("rooms", coOwner);
        var unpublished = await PutAccessibilityAsync(coRooms, cloneId, "0");
        Assert.Equal(0, unpublished.GetProperty("Accessibility").GetInt32());
    }

    private static async Task<JsonElement> PutAccessibilityAsync(HttpClient client, long roomId, string value)
    {
        using var response = await client.PutAsync($"/roomserver/rooms/{roomId}/accessibility",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["accessibility"] = value }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        // "change room accessibility" goes through the {success, value, error}
        // helper (NLDBPDCNNCF.BDBHCEGKGDC<FGCPNAACHIK>), same as clone.
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), body);
        return doc.RootElement.GetProperty("value").Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient Client(string subdomain, GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{subdomain}.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
