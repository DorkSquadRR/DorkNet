using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// The end-of-quest reward chest is the gifts/generate + gifts/consume
/// flow. This covers the load-bearing new behaviour: a gift stamped with
/// a <c>SourceStoreItemId</c> (how quest chests are built) grants exactly
/// that store item to the player's inventory when the box is opened,
/// regardless of item kind. (The room-keyed pick itself is data-driven
/// via data/quest_rewards.json and exercised once real per-quest lists
/// are configured.)
/// </summary>
public sealed class QuestRewardChestTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public QuestRewardChestTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Consuming_quest_chest_grants_backing_item()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        long giftId;
        string wardrobeGuid;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var item = await db.StoreItems.FirstAsync(i => i.IsActive && i.Slug.StartsWith("wardrobe-"));
            wardrobeGuid = item.Slug["wardrobe-".Length..];

            var gift = new GiftPackageEntity
            {
                RecipientPlayerId = player.PlayerId,
                AvatarItemType = 0,
                AvatarItemDescOrHairDyeDesc = wardrobeGuid + ",,,",
                Message = "You earned it!",
                SourceStoreItemId = item.Id,
                IsValid = true,
            };
            db.GiftPackages.Add(gift);
            await db.SaveChangesAsync();
            giftId = gift.Id;
        }

        using var client = ApiClient(player);
        using var consume = await client.PostAsync($"/api/avatar/v2/gifts/consume/{giftId}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        consume.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == player.PlayerId);
            Assert.NotNull(avatar);
            var inv = JsonSerializer.Deserialize<List<string>>(avatar!.InventoryJson) ?? new();
            Assert.Contains(wardrobeGuid, inv);

            var gift = await db.GiftPackages.FirstAsync(g => g.Id == giftId);
            Assert.True(gift.Consumed);
        }
    }

    [Fact]
    public async Task Consuming_is_idempotent_and_does_not_double_grant()
    {
        using var setup = ApiClient();
        var player = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        long giftId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var item = await db.StoreItems.FirstAsync(i => i.IsActive && i.Slug.StartsWith("wardrobe-"));
            var gift = new GiftPackageEntity
            {
                RecipientPlayerId = player.PlayerId,
                AvatarItemType = 0,
                AvatarItemDescOrHairDyeDesc = item.Slug["wardrobe-".Length..] + ",,,",
                SourceStoreItemId = item.Id,
                IsValid = true,
            };
            db.GiftPackages.Add(gift);
            await db.SaveChangesAsync();
            giftId = gift.Id;
        }

        using var client = ApiClient(player);
        for (var i = 0; i < 2; i++)
        {
            using var r = await client.PostAsync($"/api/avatar/v2/gifts/consume/{giftId}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            r.EnsureSuccessStatusCode();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var avatar = await db.Avatars.FirstAsync(a => a.PlayerId == player.PlayerId);
            var inv = JsonSerializer.Deserialize<List<string>>(avatar.InventoryJson) ?? new();
            // The wardrobe guid appears exactly once despite two consumes.
            Assert.Equal(inv.Distinct().Count(), inv.Count);
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
}
