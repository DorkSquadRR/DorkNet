using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// 2023 play-menu browse sources. The client's room-discovery URL
/// builder (IBEOONPEELF/GGDPFFDEDKN) requests
/// <c>rooms/search_rooms/{query}&amp;skip={skip}&amp;take={take}</c> —
/// note the paging pairs live INSIDE the final path segment, and the
/// whole family is prefixed with <c>rooms/</c> on the rooms host.
/// The room photo gallery sends <c>sort</c>/<c>filter</c> as enum
/// names (<c>CheerCount_Desc</c>, <c>PublicOnly</c>), which must not
/// trip int model binding.
/// </summary>
public sealed class RoomBrowse2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomBrowse2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Search_rooms_2023_path_with_embedded_paging_returns_matches()
    {
        using var setupClient = Client();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var marker = $"Zz{Guid.NewGuid():N}"[..14];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = 9_300_000 + Random.Shared.Next(1, 99_999),
                Name = $"Search{marker}",
                Description = "2023 search test room",
                CreatorPlayerId = owner.PlayerId,
                ImageName = RoomService.DefaultRoomImageName,
                Accessibility = 1,
                IsAGRoom = true,
                IsDormRoom = false,
            });
            await db.SaveChangesAsync();
        }

        using var roomsClient = Client(owner, subdomain: "rooms");
        using var response = await roomsClient.GetAsync($"/rooms/search_rooms/{marker}&skip=0&take=20");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");

        using var json = JsonDocument.Parse(text);
        var names = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("Name").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(names, n => n.Contains(marker));
    }

    [Fact]
    public async Task Room_images_accept_2023_enum_name_sort_and_filter()
    {
        using var setupClient = Client();
        var player = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        using var apiClient = Client(player);
        using var response = await apiClient.GetAsync(
            "/api/images/v4/room/116?sort=CheerCount_Desc&filter=PublicOnly&take=8&skip=0");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");

        using var json = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    private HttpClient Client(GameClientSession? session = null, string subdomain = "api")
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
