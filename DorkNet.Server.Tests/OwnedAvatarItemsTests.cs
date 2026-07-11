using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Buy_gift_desc_matches_v4items_and_consume_grants_once()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        // Buy an avatar-backed item — the server queues a post-purchase gift box.
        var storefront = await GetJsonAsync(client, "/api/storefronts/v3/giftdropstore/3");
        var purchasableId = storefront.GetProperty("StoreItems").EnumerateArray()
            .First(r => !string.IsNullOrEmpty(r.GetProperty("GiftDrop").GetProperty("AvatarItemDesc").GetString()))
            .GetProperty("PurchasableItemId").GetInt32();
        using var buy = await client.PostAsync("/api/storefronts/v2/buyItem",
            new StringContent(JsonSerializer.Serialize(new
            {
                StorefrontType = 3, PurchasableItemId = purchasableId,
                CurrencyType = 2, RequestedPrice = 1, Gift = (object?)null,
            }), Encoding.UTF8, "application/json"));
        buy.EnsureSuccessStatusCode();

        var gifts = await GetJsonAsync(client, "/api/avatar/v2/gifts");
        Assert.True(gifts.GetArrayLength() > 0, "buy did not queue a gift box");
        var gift = gifts[0];
        var giftDesc = gift.GetProperty("AvatarItemDesc").GetString()!;

        // The gift's desc MUST be the same 4-part "{guid},,," form v4/items
        // emits — the client matches on it verbatim in
        // OutfitManager.UnlockAvatarItemAndMarkNew after the box is consumed;
        // a bare guid missed and NRE'd ("items removed on every buy").
        Assert.True(giftDesc.Count(c => c == ',') >= 3, $"gift desc not 4-part: {giftDesc}");
        var items = await GetJsonAsync(client, "/api/avatar/v4/items");
        var descs = items.EnumerateArray().Select(x => x.GetProperty("AvatarItemDesc").GetString()).ToHashSet();
        Assert.Contains(giftDesc, descs);

        // Consuming the box must not add a SECOND wardrobe row for the item
        // the direct purchase already granted as a bare guid.
        using var consume = await client.PostAsync(
            $"/api/avatar/v2/gifts/consume/{gift.GetProperty("Id").GetInt64()}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        consume.EnsureSuccessStatusCode();

        var after = await GetJsonAsync(client, "/api/avatar/v4/items");
        var baseGuid = giftDesc.Split(',')[0];
        var occurrences = after.EnumerateArray()
            .Count(x => x.GetProperty("AvatarItemDesc").GetString()!.Split(',')[0] == baseGuid);
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task Legacy_bare_desc_gift_is_served_as_4part_matching_v4items()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        // Simulate a gift persisted BEFORE the fix: a bare-guid outfit desc
        // (no ",,," suffix) — the class of row that made the post-consume
        // UnlockAvatarItemAndMarkNew Find() miss and NRE.
        string bareGuid;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Data.DorkNetDbContext>();
            var item = await db.StoreItems.FirstAsync(i => i.IsActive && i.Slug.StartsWith("wardrobe-"));
            bareGuid = item.Slug["wardrobe-".Length..];
            db.GiftPackages.Add(new Data.Entities.GiftPackageEntity
            {
                RecipientPlayerId = player.PlayerId,
                AvatarItemType = 0,               // outfit
                AvatarItemDescOrHairDyeDesc = bareGuid,   // BARE — the legacy bug
                IsValid = true,
            });
            await db.SaveChangesAsync();
        }

        using var client = ApiClient(player);
        var gifts = await GetJsonAsync(client, "/api/avatar/v2/gifts");
        var gift = gifts.EnumerateArray()
            .First(g => g.GetProperty("AvatarItemDesc").GetString()!.StartsWith(bareGuid));

        // Both desc keys are served as the 4-part form regardless of how the
        // row was stored, so the client's exact-match unlock succeeds. (A
        // real buy also grants the item, so v4/items contains it — covered by
        // Purchased_item_appears_owned / Buy_gift_desc_matches_v4items.)
        var giftDesc = gift.GetProperty("AvatarItemDesc").GetString()!;
        Assert.True(giftDesc.Count(c => c == ',') >= 3, $"legacy gift not normalized: {giftDesc}");
        Assert.Equal(giftDesc, gift.GetProperty("AvatarItemDescOrHairDyeDesc").GetString());
    }

    [Fact]
    public async Task Toggle_on_wardrobe_includes_color_variants_with_unique_ids()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var settings = scope.ServiceProvider
                .GetRequiredService<DorkNet.Server.Services.ServerSettingsService>();
            await settings.SetAllAvatarItemsOwnedEnabledAsync(true);
        }

        try
        {
            using var client = ApiClient(player);
            var items = await GetJsonAsync(client, "/api/avatar/v4/items");

            // Variant descs ("{guid},{combo},{mask},") — the colorways — are
            // present, not just base "{guid},,," rows; and every id is unique
            // (base + variants share a base guid, so a base-guid hash would
            // collide → wardrobe dict crash).
            var descs = items.EnumerateArray().Select(x => x.GetProperty("AvatarItemDesc").GetString()!).ToList();
            var hasVariant = descs.Any(d =>
            {
                var p = d.Split(',');
                return p.Length >= 2 && p[1].Length > 0;
            });
            Assert.True(hasVariant, "toggle-on wardrobe has no colored variants");

            var ids = items.EnumerateArray().Select(x => x.GetProperty("AvatarItemId").GetInt32()).ToList();
            Assert.DoesNotContain(0, ids);
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<DorkNet.Server.Services.ServerSettingsService>()
                .SetAllAvatarItemsOwnedEnabledAsync(false);
        }
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
