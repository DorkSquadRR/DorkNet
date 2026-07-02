using System.Net;
using System.Text;
using System.Text.Json;

namespace DorkNet.Server.Tests;

/// <summary>
/// End-to-end coverage for the 3D Charades word-list feature and the
/// profanity-filter toggle. Boots the full server (which runs the seeder)
/// against SQLite via <see cref="DorkNetServerFactory"/> and exercises the
/// public wire endpoints the March 2023 client actually calls.
/// </summary>
public sealed class CharadesWordListTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public CharadesWordListTests(DorkNetServerFactory factory) => _factory = factory;

    private HttpClient ApiClient()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.BaseAddress = new Uri($"http://api.{_factory.ApexDomain}");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    [Theory]
    [InlineData("Charades")]              // enum-name form
    [InlineData("CharadesAprilFoolsDay")]
    [InlineData("Icebreakers")]
    [InlineData("0")]                     // int form the client may send
    [InlineData("1")]
    [InlineData("2")]
    public async Task Words_endpoint_returns_seeded_deck_in_wire_shape(string source)
    {
        using var client = ApiClient();
        using var res = await client.GetAsync($"/api/activities/charades/v1/words/{source}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0, $"expected a non-empty deck for '{source}'");

        var first = doc.RootElement[0];
        // Exact wire key casing the client's LitJson importer reads.
        Assert.True(first.TryGetProperty("EN_US", out var en) && en.ValueKind == JsonValueKind.String,
            "each card must expose a string EN_US field");
        Assert.True(first.TryGetProperty("Difficulty", out var diff) && diff.ValueKind == JsonValueKind.Number,
            "each card must expose a numeric Difficulty field");
    }

    [Fact]
    public async Task December_noparam_route_serves_the_charades_slot()
    {
        using var client = ApiClient();
        using var res = await client.GetAsync("/api/activities/charades/v1/words");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Unknown_source_falls_back_to_charades_slot()
    {
        using var client = ApiClient();
        using var res = await client.GetAsync("/api/activities/charades/v1/words/totally-bogus");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Profanity_filter_is_active_by_default()
    {
        using var client = ApiClient();
        using var res = await client.PostAsync(
            "/api/sanitize/v1/requestIsStringPure",
            new StringContent("{\"Value\":\"you piece of shit\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadAsStringAsync()).Trim();
        // Default state: filter on → the profane string is NOT pure.
        Assert.Equal("false", body.ToLowerInvariant());
    }
}
