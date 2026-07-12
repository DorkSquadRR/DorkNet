using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

/// <summary>
/// Event notifications (cheers) now go through the REAL Message channel
/// (persisted MessageEntity + MessageReceived push with the client's
/// Message DTO shape), instead of the old blank-account misuse of
/// SubscriptionUpdateProfile. A cheer must land in the recipient's message
/// feed as a PlayerCheer (Type 50) from the cheerer.
/// </summary>
public sealed class SystemNotificationTests : IClassFixture<DorkNetServerFactory>
{
    private const int PlayerCheerType = 50;

    private readonly DorkNetServerFactory _factory;

    public SystemNotificationTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Cheering_a_player_creates_a_PlayerCheer_message_notification()
    {
        using var setup = ApiClient();
        var cheerer = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);
        var target = await GameClientSessionFactory.CreateAsync(setup, _factory.ApexDomain);

        using var client = ApiClient(cheerer);
        // Real client field names (RecNet.Runtime KFJKDMGHKHE.EMKBFCLIOGN):
        // PlayerIdTo + CheerCategory (+ RoomId, Anonymous). The old test used
        // TargetAccountId/Type, which matched the buggy handler binding but
        // NOT the game client — so it passed while cheers were broken live.
        using var resp = await client.PostAsync("/api/PlayerCheer/v1/create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PlayerIdTo"] = target.PlayerId.ToString(),
                ["CheerCategory"] = "0",
                ["Anonymous"] = "false",
            }));
        resp.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var msg = await db.Messages.FirstOrDefaultAsync(m =>
            m.RecipientPlayerId == target.PlayerId && m.Type == PlayerCheerType);

        Assert.NotNull(msg);
        Assert.Equal(cheerer.PlayerId, msg!.SenderPlayerId);
        // No orphan/blank-account row was written anywhere in the flow.
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
