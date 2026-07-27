using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// End-to-end coverage of the 2023 client's in-room shop
/// (api/roomconsumables/v1) using the exact request/response wire shapes
/// the 2023.03.21 client serialises (verified against the RecNet.Runtime
/// formatters in the ISIL dump). The critical regression guard: every
/// inventory row returned by <c>/room/{id}/me</c> must carry a non-null
/// <c>Consumable</c> desc — RoomConsumablesManager NREs on room join
/// otherwise.
/// </summary>
public sealed class RoomConsumablesShopTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public RoomConsumablesShopTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_shop_loop_create_purchase_inventory_consume_delete()
    {
        using var setupClient = ApiClient();
        var owner = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);
        var buyer = await GameClientSessionFactory.CreateAsync(setupClient, _factory.ApexDomain);

        var roomId = 9_200_001L;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = $"ShopRoom_{Guid.NewGuid():N}"[..24],
                Description = "shop test room",
                CreatorPlayerId = owner.PlayerId,
                ImageName = RoomService.DefaultRoomImageName,
                State = 0,
                Accessibility = 1,
                IsAGRoom = true,
                TagsCsv = "community",
            });
            await db.SaveChangesAsync();

            var level = scope.ServiceProvider.GetRequiredService<LevelService>();
            await level.GrantCurrencyAsync(buyer.PlayerId, 2, 100, "test-seed");
        }

        using var ownerClient = ApiClient(owner);
        using var buyerClient = ApiClient(buyer);

        // ── Create (client body = the desc DTO itself) ───────────────────
        var created = await PostJsonAsync(ownerClient, "/api/roomconsumables/v1/roomConsumable", $$"""
            {
                "RoomId": {{roomId}},
                "Name": "Cola",
                "Description": "A refreshing test beverage",
                "ImageName": "",
                "PriceAndCurrency": { "Price": 10, "CurrencyId": null }
            }
            """);
        Assert.Equal(0, created.GetProperty("Status").GetInt32());
        var consumableId = created.GetProperty("Consumable").GetProperty("RoomConsumableId").GetGuid();
        Assert.NotEqual(Guid.Empty, consumableId);
        Assert.Equal(roomId, created.GetProperty("Consumable").GetProperty("RoomId").GetInt64());

        // ── Non-owner cannot create in someone else's room ───────────────
        var forbidden = await PostJsonAsync(buyerClient, "/api/roomconsumables/v1/roomConsumable", $$"""
            { "RoomId": {{roomId}}, "Name": "Hax", "Description": "", "ImageName": "",
              "PriceAndCurrency": { "Price": 1, "CurrencyId": null } }
            """);
        Assert.Equal(7, forbidden.GetProperty("Status").GetInt32()); // PlayerDoesntHavePermission

        // ── Room catalog ─────────────────────────────────────────────────
        var catalog = await GetJsonAsync(buyerClient, $"/api/roomconsumables/v1/roomConsumable/room/{roomId}");
        Assert.Equal(1, catalog.GetArrayLength());
        // Responses carry the price FLAT. The client's consumable formatter
// (RecNet.Runtime/FCIBLPCOODP) reads RoomConsumableId/RoomId/Name/
// Description/ImageName/Price/PurchaseCurrencyId/ModifiedAt and has no
// "PriceAndCurrency" key at all — only REQUEST bodies nest it.
        Assert.Equal(10, catalog[0].GetProperty("Price").GetInt64());

        // ── Purchase at the wrong price is rejected ──────────────────────
        var codeA = Guid.NewGuid();
        var badPrice = await PostJsonAsync(buyerClient,
            $"/api/roomconsumables/v1/roomconsumable/{consumableId}/purchase/tokens", $$"""
            {
                "ConcurrencyCodes": { "CurrentConcurrencyCode": null, "NewConcurrencyCode": "{{codeA}}" },
                "ExpectedPriceAndCurrency": { "Price": 5, "CurrencyId": null }
            }
            """);
        Assert.Equal(6, badPrice.GetProperty("OperationResult").GetInt32()); // RequestedPriceDoesNotMatch
        var balanceBefore = badPrice.GetProperty("TokenBalanceResponse").GetProperty("Balance").GetInt64();

        // ── Purchase (client stores NewConcurrencyCode locally) ──────────
        var purchase = await PostJsonAsync(buyerClient,
            $"/api/roomconsumables/v1/roomconsumable/{consumableId}/purchase/tokens", $$"""
            {
                "ConcurrencyCodes": { "CurrentConcurrencyCode": null, "NewConcurrencyCode": "{{codeA}}" },
                "ExpectedPriceAndCurrency": { "Price": 10, "CurrencyId": null }
            }
            """);
        Assert.Equal(0, purchase.GetProperty("OperationResult").GetInt32());
        var tokenBalance = purchase.GetProperty("TokenBalanceResponse");
        Assert.Equal(2, tokenBalance.GetProperty("CurrencyType").GetInt32());
        Assert.Equal(balanceBefore - 10, tokenBalance.GetProperty("Balance").GetInt64());

        // ── Inventory on room join: Consumable must NEVER be null ────────
        var inventory = await GetJsonAsync(buyerClient, $"/api/roomconsumables/v1/roomConsumable/room/{roomId}/me");
        Assert.Equal(1, inventory.GetArrayLength());
        var row = inventory[0];
        Assert.Equal(consumableId, row.GetProperty("RoomConsumableId").GetGuid());
        Assert.Equal((int)buyer.PlayerId, row.GetProperty("AccountId").GetInt32());
        Assert.Equal(1, row.GetProperty("Count").GetInt32());
        Assert.Equal(codeA, row.GetProperty("ConcurrencyCode").GetGuid());
        Assert.Equal(JsonValueKind.Object, row.GetProperty("Consumable").ValueKind);
        Assert.Equal(roomId, row.GetProperty("Consumable").GetProperty("RoomId").GetInt64());

        // ── isOwned: a non-creator holds stock ───────────────────────────
        using var isOwnedResponse = await ownerClient.GetAsync(
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}/isOwned");
        Assert.Equal("true", await isOwnedResponse.Content.ReadAsStringAsync());

        // ── Consume with a stale concurrency code resyncs the client ─────
        var codeB = Guid.NewGuid();
        var staleConsume = await PostJsonAsync(buyerClient,
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}/consume", $$"""
            { "CurrentConcurrencyCode": "{{Guid.NewGuid()}}", "NewConcurrencyCode": "{{codeB}}" }
            """);
        Assert.Equal(32, staleConsume.GetProperty("Status").GetInt32()); // ConcurrencyCodeMismatch
        Assert.Equal(1, staleConsume.GetProperty("InventoryItem").GetProperty("Count").GetInt32());

        // ── Consume with the right code decrements and rotates the code ──
        var consume = await PostJsonAsync(buyerClient,
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}/consume", $$"""
            { "CurrentConcurrencyCode": "{{codeA}}", "NewConcurrencyCode": "{{codeB}}" }
            """);
        Assert.Equal(0, consume.GetProperty("Status").GetInt32());
        Assert.Equal(0, consume.GetProperty("InventoryItem").GetProperty("Count").GetInt32());
        Assert.Equal(codeB, consume.GetProperty("InventoryItem").GetProperty("ConcurrencyCode").GetGuid());

        // ── Consuming an empty stack is refused ──────────────────────────
        var emptyConsume = await PostJsonAsync(buyerClient,
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}/consume", $$"""
            { "CurrentConcurrencyCode": "{{codeB}}", "NewConcurrencyCode": "{{Guid.NewGuid()}}" }
            """);
        Assert.Equal(33, emptyConsume.GetProperty("Status").GetInt32()); // PlayerDoesNotOwnConsumable

        // ── Update via POST with RoomConsumableId in the body ────────────
        var updated = await PostJsonAsync(ownerClient, "/api/roomconsumables/v1/roomConsumable", $$"""
            {
                "RoomConsumableId": "{{consumableId}}",
                "RoomId": {{roomId}},
                "Name": "Diet Cola",
                "Description": "Now with fewer tokens",
                "ImageName": "",
                "PriceAndCurrency": { "Price": 5, "CurrencyId": null }
            }
            """);
        Assert.Equal(0, updated.GetProperty("Status").GetInt32());
        Assert.Equal("Diet Cola", updated.GetProperty("Consumable").GetProperty("Name").GetString());
        Assert.Equal(5, updated.GetProperty("Consumable").GetProperty("Price").GetInt64());

        // ── Delete: buyer refused, owner succeeds, catalog empties ───────
        using var buyerDelete = await buyerClient.DeleteAsync(
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}");
        Assert.Equal("7", await buyerDelete.Content.ReadAsStringAsync()); // PlayerDoesntHavePermission

        using var ownerDelete = await ownerClient.DeleteAsync(
            $"/api/roomconsumables/v1/roomConsumable/{consumableId}");
        Assert.Equal("0", await ownerDelete.Content.ReadAsStringAsync());

        var emptied = await GetJsonAsync(buyerClient, $"/api/roomconsumables/v1/roomConsumable/room/{roomId}");
        Assert.Equal(0, emptied.GetArrayLength());
    }

    [Fact]
    public async Task Unprefixed_2023_commerce_probes_respond()
    {
        // The 2023 client calls these WITHOUT the api/ prefix on startup;
        // a 404 here surfaces as "CleanupPendingTransactions failed" +
        // unobserved-HTTP-404 crash reports in Player.log.
        using var client = ApiClient();

        using var cleanup = await client.PostAsync("/purchase/v1/cleanuppending",
            new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.True(cleanup.IsSuccessStatusCode);

        using var hasSpent = await client.GetAsync("/purchase/v1/hasspentmoney");
        Assert.Equal("false", await hasSpent.Content.ReadAsStringAsync());

        var bundles = await GetJsonAsync(client, "/reminder/currentTokenBundles/v2");
        Assert.Equal(JsonValueKind.Array, bundles.ValueKind);
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

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, string json)
    {
        using var response = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
