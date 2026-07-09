using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DorkNet.Server.Tests;

/// <summary>
/// api/avatar/v4/items — the owned/unlocked wardrobe. The 2023 client's
/// owned-item DTO (RecNet IFNGBPJGINL) requires <c>AvatarItemId</c> (Int32),
/// <c>TagList</c> (string) and <c>IsBaseAvatarItem</c> (bool). Omitting them
/// left every item at AvatarItemId 0; the client keys the wardrobe by that
/// id, so the second item collided, the unlocked-items list failed to build
/// (empty wardrobe = "no owned items"), and any refetch — including the one
/// after a purchase — NRE'd in OutfitManager.UnlockAvatarItemAndMarkNew
/// ("can't buy items"). Each item must carry a unique, non-zero id.
/// </summary>
public sealed class OwnedAvatarItemsTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public OwnedAvatarItemsTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Owned_items_carry_unique_nonzero_ids_and_2023_keys()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        var items = await GetJsonAsync(client, "/api/avatar/v4/items");
        Assert.True(items.GetArrayLength() > 0, "starter wardrobe was empty");

        var ids = new List<int>();
        foreach (var it in items.EnumerateArray())
        {
            // Every 2023 key present and correctly typed.
            Assert.Equal(JsonValueKind.Number, it.GetProperty("AvatarItemId").ValueKind);
            Assert.Equal(JsonValueKind.String, it.GetProperty("TagList").ValueKind);
            var isBase = it.GetProperty("IsBaseAvatarItem").ValueKind;
            Assert.True(isBase is JsonValueKind.True or JsonValueKind.False);
            Assert.Equal(JsonValueKind.String, it.GetProperty("AvatarItemDesc").ValueKind);

            var id = it.GetProperty("AvatarItemId").GetInt32();
            Assert.NotEqual(0, id);
            ids.Add(id);
        }

        // No duplicate ids — a collision is what crashed the wardrobe.
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Purchased_item_appears_owned_with_valid_shape()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        var before = (await GetJsonAsync(client, "/api/avatar/v4/items")).GetArrayLength();

        // Buy the first avatar-backed watch-storefront item.
        var storefront = await GetJsonAsync(client, "/api/storefronts/v3/giftdropstore/3");
        var purchasableId = storefront.GetProperty("StoreItems").EnumerateArray()
            .First(r => !string.IsNullOrEmpty(r.GetProperty("GiftDrop").GetProperty("AvatarItemDesc").GetString()))
            .GetProperty("PurchasableItemId").GetInt32();

        using var buy = await client.PostAsync("/api/storefronts/v2/buyItem",
            new StringContent(JsonSerializer.Serialize(new
            {
                StorefrontType = 3,
                PurchasableItemId = purchasableId,
                CurrencyType = 2,
                RequestedPrice = 1,
                Gift = (object?)null,
            }), Encoding.UTF8, "application/json"));
        buy.EnsureSuccessStatusCode();

        var after = await GetJsonAsync(client, "/api/avatar/v4/items");
        Assert.True(after.GetArrayLength() >= before, "owned count shrank after purchase");
        // Still no duplicate ids after the new item joins the list.
        var ids = after.EnumerateArray().Select(x => x.GetProperty("AvatarItemId").GetInt32()).ToList();
        Assert.DoesNotContain(0, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
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

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
