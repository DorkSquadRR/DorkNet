using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// <c>GET api/incentivizedreferrals/referrals?take=&amp;continuationToken=</c> is
/// assembled by the client's URL builder (RecNet.Runtime/LINKIECPDML.txt:677-737),
/// so it never showed up as a route literal and had no handler. The reply is the
/// GPJIMAHMAIO page: <c>{Referrals:[{InviteeAccountId, CreatedAt, VerifiedAt}],
/// ContinuationToken}</c> (KOEEACLMAFE.txt:203-238, ADAFNGNADFH.txt:279-346).
/// </summary>
public sealed class IncentivizedReferralsTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public IncentivizedReferralsTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Referrals_page_lists_credited_invitees_with_a_continuation_token()
    {
        using var setup = Client();
        var referrer = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var inviteeA = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var inviteeB = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            // Same row LinkController.CreditReferralAsync writes on the invitee.
            foreach (var invitee in new[] { inviteeA, inviteeB })
                db.PlayerSettings.Add(new PlayerSettingEntity
                {
                    PlayerId = invitee.PlayerId,
                    Key = $"referral:credited:{referrer.PlayerId}:{invitee.PlayerId}",
                    Value = DateTime.UtcNow.ToString("O"),
                });
            await db.SaveChangesAsync();
        }

        using var client = Client(referrer);
        var first = JsonDocument.Parse(
            await client.GetStringAsync("/api/incentivizedreferrals/referrals?take=1")).RootElement;
        var firstPage = Get(first, "Referrals");
        Assert.Equal(1, firstPage.GetArrayLength());
        var token = Get(first, "ContinuationToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var second = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/incentivizedreferrals/referrals?take=1&continuationToken={token}")).RootElement;
        var secondPage = Get(second, "Referrals");
        Assert.Equal(1, secondPage.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, Get(second, "ContinuationToken").ValueKind);

        var ids = new[] { firstPage[0], secondPage[0] }
            .Select(e => Get(e, "InviteeAccountId").GetInt64())
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(new[] { inviteeA.PlayerId, inviteeB.PlayerId }.OrderBy(x => x), ids);
        // CreatedAt is a non-nullable DateTime on the client: it must be present.
        Assert.Equal(JsonValueKind.String, Get(firstPage[0], "CreatedAt").ValueKind);
    }

    // The client formatter registers PascalCase, camelCase and lowercase.
    private static JsonElement Get(JsonElement e, string pascal) =>
        e.TryGetProperty(pascal, out var v) ? v : e.GetProperty(char.ToLowerInvariant(pascal[0]) + pascal[1..]);

    private HttpClient Client(GameClientSession? session = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        if (session is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }
}
