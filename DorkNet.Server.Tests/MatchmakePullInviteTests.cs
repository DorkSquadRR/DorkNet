using System.Net;
using DorkNet.Contracts;

namespace DorkNet.Server.Tests;

/// <summary>
/// Regression coverage for the 2023 "pull a player into a private instance"
/// flow. The 2023 client renamed the matchmaking surface from <c>goto/*</c>
/// to <c>matchmake/*</c> and added <c>matchmake/chatinvite/{account}/{instance}</c>
/// for chat party invites; the server had neither the routes nor the gateway
/// ownership entry, so those requests 404'd.
/// </summary>
public sealed class MatchmakePullInviteTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public MatchmakePullInviteTests(DorkNetServerFactory factory) => _factory = factory;

    private HttpClient ApiClient()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    [Theory]
    [InlineData("/matchmake/instance/999")]
    [InlineData("/matchmake/invite/999")]
    [InlineData("/matchmake/chatinvite/2/16777216")] // {accountId}/{roomInstanceId}
    public async Task Matchmake_pull_routes_exist(string path)
    {
        using var client = ApiClient();
        using var res = await client.PostAsync(path, new StringContent(string.Empty));

        // The bug was a hard 404 (no route). These resolve to a dorm-fallback
        // matchmaking response when the instance/invite doesn't exist, so any
        // non-404 status proves the route is now mounted.
        Assert.NotEqual(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Theory]
    [InlineData("/matchmake/instance/999")]
    [InlineData("/matchmake/invite/999")]
    [InlineData("/matchmake/chatinvite/2/16777216")]
    [InlineData("/matchmake/room/5")]
    public void Matchmake_paths_route_to_same_service_as_goto(string path)
    {
        const string apex = "dork.test";
        var gotoOwner = DorkNetRouteOwnership.ResolvePublicService($"api.{apex}", "/goto/instance/999", apex);
        var owner = DorkNetRouteOwnership.ResolvePublicService($"api.{apex}", path, apex);

        // matchmake/* must land on the same service that owns goto/* — before
        // the fix it fell through to the Monolith default owner.
        Assert.Equal(gotoOwner, owner);
    }
}
