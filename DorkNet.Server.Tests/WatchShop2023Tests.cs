using System.Net.Http.Headers;
using System.Text.Json;

namespace DorkNet.Server.Tests;

/// <summary>
/// The 2023 client's watch Shop tab pipeline: rows come from
/// <c>/api/storefronts/v3/giftdropstore/{id}</c> (each row MUST carry a
/// singular <c>GiftDrop</c> object — the 2023 POGBAGAHGIA formatter
/// ignores the 2020 <c>GiftDrops</c> array, and a null inner desc makes
/// StoreItemListModel's filter predicate NRE so the Shop shows nothing),
/// and the featured strip ids come from <c>/v1/toptoday</c> as a BARE
/// int list that must resolve against those rows.
/// </summary>
public sealed class WatchShop2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public WatchShop2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Giftdropstore_rows_carry_singular_giftdrop_and_toptoday_ids_resolve()
    {
        using var client = ApiClient();

        var storefront = await GetJsonAsync(client, "/api/storefronts/v3/giftdropstore/3");
        var rows = storefront.GetProperty("StoreItems");
        Assert.True(rows.GetArrayLength() > 0, "watch storefront seeded no rows");

        var rowIds = new HashSet<int>();
        foreach (var row in rows.EnumerateArray())
        {
            rowIds.Add(row.GetProperty("PurchasableItemId").GetInt32());
            Assert.Equal(0, row.GetProperty("Type").GetInt32());
            Assert.True(row.GetProperty("Prices").GetArrayLength() > 0);

            var giftDrop = row.GetProperty("GiftDrop");
            Assert.Equal(JsonValueKind.Object, giftDrop.ValueKind);
            Assert.Equal(row.GetProperty("PurchasableItemId").GetInt32(),
                giftDrop.GetProperty("GiftDropId").GetInt32());
            Assert.Equal(JsonValueKind.String, giftDrop.GetProperty("FriendlyName").ValueKind);
            Assert.Equal(JsonValueKind.String, giftDrop.GetProperty("TagList").ValueKind);
            var rarity = giftDrop.GetProperty("Rarity").GetInt32();
            Assert.Contains(rarity, new[] { 0, 10, 20, 30, 50 });

            // Int32 on the 2023 wire — a descriptor STRING here makes the
            // strict Utf8Json reader throw "Malformed Response" for the
            // whole storefront payload (expected:'Number Token').
            Assert.Equal(JsonValueKind.Number, giftDrop.GetProperty("AvatarItemId").ValueKind);
            Assert.Equal(JsonValueKind.String, giftDrop.GetProperty("AvatarItemDesc").ValueKind);
        }

        // Featured strip: bare int ids, every one resolvable in the rows.
        var topToday = await GetJsonAsync(client, "/api/storefronts/v1/toptoday");
        Assert.Equal(JsonValueKind.Array, topToday.ValueKind);
        Assert.True(topToday.GetArrayLength() > 0, "toptoday returned no featured ids");
        foreach (var id in topToday.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Number, id.ValueKind);
            Assert.Contains(id.GetInt32(), rowIds);
        }
    }

    [Fact]
    public async Task Storefront_includes_food_consumables_with_literal_desc()
    {
        using var client = ApiClient();
        var storefront = await GetJsonAsync(client, "/api/storefronts/v3/giftdropstore/3");
        var rows = storefront.GetProperty("StoreItems");

        // Every consumable tile carries a bracketed ConsumableItemDesc (the
        // client's literal item key) and no avatar item — that's what routes
        // it to the Shop's Consumables tab and binds the baked prefab.
        var consumables = new List<string>();
        foreach (var row in rows.EnumerateArray())
        {
            var gd = row.GetProperty("GiftDrop");
            var desc = gd.GetProperty("ConsumableItemDesc").GetString() ?? "";
            if (desc.StartsWith("[") || desc.StartsWith("("))
            {
                consumables.Add(desc);
                // Consumables are not avatar items: AvatarItemDesc empty and
                // AvatarItemType null (a non-null enum here would tab it as
                // clothing).
                Assert.Equal("", gd.GetProperty("AvatarItemDesc").GetString());
                Assert.Equal(JsonValueKind.Null, gd.GetProperty("AvatarItemType").ValueKind);
            }
        }

        Assert.Contains("[FoodConsumable_RootBeer]", consumables);
        Assert.Contains("[KOConsumable_Cola]", consumables);
    }

    private HttpClient ApiClient()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecRoom/2023.03.21");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
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
