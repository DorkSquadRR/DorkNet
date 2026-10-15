using System.Net.Http.Headers;
using System.Text.Json;
using DorkNet.Server.Services;

namespace DorkNet.Server.Tests;

/// <summary>
/// A Quest client patched without its own Meta app has no platform session,
/// so every headset reports the same placeholder platform user id ("1").
/// Accounts must stay keyed by device id: two headsets get two accounts, a
/// headset gets its own account back on the next boot, and the placeholder is
/// never stored or matched by the remembered-logins lookup — otherwise every
/// Quest player would be offered every other Quest player's account.
/// </summary>
public sealed class QuestPlaceholderPlatformIdTests : IClassFixture<DorkNetServerFactory>
{
    // RecNet platform value the 2023 Quest client sends on /connect/token.
    // The fix does not depend on it; any platform with a placeholder id is
    // handled the same way.
    private const string QuestPlatform = "1";
    private const string PlaceholderPlatformId = "1";

    private readonly DorkNetServerFactory _factory;

    public QuestPlaceholderPlatformIdTests(DorkNetServerFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("10", false)]
    [InlineData("2173678582678296", false)]
    public void Placeholder_platform_ids_are_recognised(string? platformId, bool expected)
        => Assert.Equal(expected, PlayerService.IsPlaceholderPlatformId(platformId));

    [Fact]
    public async Task Two_headsets_with_the_placeholder_platform_id_get_separate_accounts()
    {
        using var client = Client("auth");
        var headsetA = $"quest-headset-a-{Guid.NewGuid():N}";
        var headsetB = $"quest-headset-b-{Guid.NewGuid():N}";

        var a = await CreateAccountAsync(client, headsetA);
        var b = await CreateAccountAsync(client, headsetB);
        Assert.NotEqual(a, b);

        // Same headset, next boot: its own account comes back.
        var aAgain = await CreateAccountAsync(client, headsetA);
        Assert.Equal(a, aAgain);
    }

    [Fact]
    public async Task Placeholder_platform_id_never_lists_remembered_logins()
    {
        using var client = Client("auth");
        await CreateAccountAsync(client, $"quest-headset-{Guid.NewGuid():N}");
        await CreateAccountAsync(client, $"quest-headset-{Guid.NewGuid():N}");

        using var response = await client.GetAsync(
            $"/cachedlogin/forplatformid/{QuestPlatform}/{PlaceholderPlatformId}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"GET cachedlogin/forplatformid -> {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task A_real_platform_id_still_lists_its_remembered_login()
    {
        using var client = Client("auth");
        var realPlatformId = $"{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var id = await CreateAccountAsync(client, $"quest-meta-{Guid.NewGuid():N}", realPlatformId);

        using var response = await client.GetAsync(
            $"/cachedlogin/forplatformid/{QuestPlatform}/{realPlatformId}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"GET cachedlogin/forplatformid -> {(int)response.StatusCode}: {body}");
        Assert.Contains(id.ToString(), body);
    }

    // The 2023 client's account-creation request (LBNJFPOLCDL: grant_type,
    // client_id, platform, platform_id, device_id, ...). No username/password,
    // so the server takes the device-id path.
    private async Task<long> CreateAccountAsync(
        HttpClient client, string deviceId, string platformId = PlaceholderPlatformId)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "create_account",
            ["client_id"] = "recroom",
            ["platform"] = QuestPlatform,
            ["platform_id"] = platformId,
            ["device_id"] = deviceId,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("RecRoom/2023.03.17");
        request.Headers.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"POST /connect/token -> {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        var accessToken = json.RootElement.GetProperty("access_token").GetString()!;

        using var userInfo = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var userInfoResponse = await client.SendAsync(userInfo);
        userInfoResponse.EnsureSuccessStatusCode();
        using var userJson = JsonDocument.Parse(await userInfoResponse.Content.ReadAsStringAsync());
        return long.Parse(userJson.RootElement.GetProperty("sub").GetString()!);
    }

    private HttpClient Client(string host)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://{host}.{_factory.ApexDomain}");
        return client;
    }
}
