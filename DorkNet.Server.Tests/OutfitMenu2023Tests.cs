using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Controllers.API.Avatar.V4;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Wire contract of the 2023-03-21 client's outfit / wardrobe menu, as read
/// from RecNet.Runtime ISIL: saved-outfit slots (BEPMJICDNDD via converter
/// IMMOJHBENFI), the custom-avatar-item gates the menu queries when it opens,
/// the locked-item lookup, the owned list's per-entry ids, and the gift
/// generate call the menu's gift flow makes.
/// </summary>
public sealed class OutfitMenu2023Tests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public OutfitMenu2023Tests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Creation_allowed_gate_is_a_success_error_envelope()
    {
        // MPBLNLMCEDL.GGMDLMLJDDK parses DJMHAFPGLLN {Success, Error}
        // (LMCCNLLHLCJ.txt:191-254); a bare boolean is what logged
        // "Failed to query if local account can create Custom Avatar Items".
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        var body = await GetJsonAsync(client, "/api/customAvatarItems/v1/isCreationAllowedForAccount");
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.Equal(JsonValueKind.True, body.GetProperty("Success").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("Error").ValueKind);
    }

    [Fact]
    public async Task Sibling_gates_and_min_price_are_bare_scalars()
    {
        using var client = ApiClient();
        Assert.Equal(JsonValueKind.True, (await GetJsonAsync(client, "/api/customAvatarItems/v1/isCreationEnabled")).ValueKind);
        Assert.Equal(JsonValueKind.True, (await GetJsonAsync(client, "/api/customAvatarItems/v1/isRenderingEnabled")).ValueKind);

        // <GetPricingConfig>b__69_0(Int32 minimumPrice) — a bare integer.
        var min = await GetJsonAsync(client, "/api/customAvatarItems/v1/minPriceForPublicItem");
        Assert.Equal(JsonValueKind.Number, min.ValueKind);
        Assert.True(min.GetInt32() > 0);
    }

    [Fact]
    public async Task Saved_outfit_round_trips_every_client_key()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        var shirt = Guid.NewGuid();
        // Exactly what the client's writer (IMMOJHBENFI.txt:781-910) emits.
        var outfit = new Dictionary<string, object?>
        {
            ["Slot"] = 3,
            ["PreviewImageName"] = "preview-3.jpg",
            ["OutfitSelections"] = "",
            ["OutfitSelectionsV2"] = "{\"v\":2,\"items\":[\"a\",\"b\"]}",
            ["FaceFeatures"] = "{\"ver\":3}",
            ["SkinColor"] = "skin-guid",
            ["HairColor"] = "hair-guid",
            ["CustomAvatarItems"] = new[] { new { CustomAvatarItemId = shirt, BodyPart = 2 } },
        };
        await PostJsonAsync(client, "/api/avatar/v3/saved/set", outfit);

        var list = await GetJsonAsync(client, "/api/avatar/v3/saved");
        var saved = list.EnumerateArray().Single(o => o.GetProperty("Slot").GetInt32() == 3);
        Assert.Equal("preview-3.jpg", saved.GetProperty("PreviewImageName").GetString());
        Assert.Equal("{\"v\":2,\"items\":[\"a\",\"b\"]}", saved.GetProperty("OutfitSelectionsV2").GetString());
        Assert.Equal("{\"ver\":3}", saved.GetProperty("FaceFeatures").GetString());
        Assert.Equal("skin-guid", saved.GetProperty("SkinColor").GetString());
        Assert.Equal("hair-guid", saved.GetProperty("HairColor").GetString());
        var items = saved.GetProperty("CustomAvatarItems");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(shirt, items[0].GetProperty("CustomAvatarItemId").GetGuid());
        Assert.Equal(2, items[0].GetProperty("BodyPart").GetInt32());

        // Overwriting the slot replaces it rather than adding a second row.
        outfit["OutfitSelectionsV2"] = "{\"v\":2,\"items\":[\"c\"]}";
        outfit["CustomAvatarItems"] = Array.Empty<object>();
        await PostJsonAsync(client, "/api/avatar/v3/saved/set", outfit);
        list = await GetJsonAsync(client, "/api/avatar/v3/saved");
        saved = list.EnumerateArray().Single(o => o.GetProperty("Slot").GetInt32() == 3);
        Assert.Equal("{\"v\":2,\"items\":[\"c\"]}", saved.GetProperty("OutfitSelectionsV2").GetString());
        Assert.Equal(0, saved.GetProperty("CustomAvatarItems").GetArrayLength());
    }

    [Fact]
    public async Task Gift_generate_accepts_the_clients_form_body()
    {
        // Live trace: this exact form body was answered 415 under [FromBody].
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        using var response = await client.PostAsync("/api/avatar/v2/gifts/generate",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["GiftContext"] = "12000",
                ["IsGameGift"] = "True",
                ["Message"] = "Activity completed!",
            }));
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("Id").GetInt64() > 0);
    }

    [Fact]
    public async Task Locked_items_carry_the_full_owned_item_shape()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        using var client = ApiClient(player);

        HashSet<string> owned;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var inv = await db.Avatars.Where(a => a.PlayerId == player.PlayerId)
                .Select(a => a.InventoryJson).FirstAsync();
            owned = (JsonSerializer.Deserialize<List<string>>(inv) ?? new())
                .Select(d => d.Replace("wardrobe-", "").Split(',')[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var notOwned = AvatarItemsController.Catalog.Keys
            .Where(g => Guid.TryParse(g, out _) && !owned.Contains(g)).Take(3).ToList();
        Assert.Equal(3, notOwned.Count);

        var query = string.Join("&", notOwned.Select(g => "desc=" + Uri.EscapeDataString(g + ",,,")));
        var rows = await GetJsonAsync(client, "/api/avatar/v1/lockeditems?" + query);
        Assert.Equal(3, rows.GetArrayLength());
        var ids = new List<int>();
        foreach (var row in rows.EnumerateArray())
        {
            // Same IFNGBPJGINL keys v4/items carries (CLIANCNHNIE reader).
            Assert.Equal(JsonValueKind.String, row.GetProperty("TagList").ValueKind);
            Assert.True(row.GetProperty("IsBaseAvatarItem").ValueKind is JsonValueKind.True or JsonValueKind.False);
            ids.Add(row.GetProperty("AvatarItemId").GetInt32());
        }
        Assert.DoesNotContain(0, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Owning_a_base_item_and_its_colorways_yields_unique_ids()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        // Two colorways of one base item that the runtime catalog knows.
        var group = StoreService.ColorVariantCatalog.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.descriptor)
                        && AvatarItemsController.Catalog.ContainsKey(v.item_guid))
            .GroupBy(v => v.item_guid, StringComparer.OrdinalIgnoreCase)
            .First(g => g.Select(v => v.descriptor).Distinct().Count() >= 2);
        var baseGuid = group.Key;
        var variants = group.Select(v => v.descriptor).Distinct().Take(2).ToList();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var avatar = await db.Avatars.FirstAsync(a => a.PlayerId == player.PlayerId);
            var inv = JsonSerializer.Deserialize<List<string>>(avatar.InventoryJson) ?? new();
            inv.Add(baseGuid);
            inv.AddRange(variants);
            avatar.InventoryJson = JsonSerializer.Serialize(inv);
            await db.SaveChangesAsync();
        }

        using var client = ApiClient(player);
        var items = await GetJsonAsync(client, "/api/avatar/v4/items");
        var sameBase = items.EnumerateArray()
            .Where(x => x.GetProperty("AvatarItemDesc").GetString()!.Split(',')[0]
                .Equals(baseGuid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(3, sameBase.Count);

        var ids = items.EnumerateArray().Select(x => x.GetProperty("AvatarItemId").GetInt32()).ToList();
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

    private static async Task PostJsonAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsync(path,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
