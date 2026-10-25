using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DorkNet.Server.Tests;

/// <summary>
/// Website / admin-panel sign-in with an external OpenID Connect provider.
/// The provider round-trip can't run in-process, so each test starts where the
/// provider callback leaves off: a <c>DorkNet.External</c> cookie holding the
/// provider identity, encrypted with the app's own cookie format. Everything
/// from there (link, session, admin SSO, unlink, limits) is the real code.
/// </summary>
public sealed class ExternalSignInTests : IClassFixture<DorkNetServerFactory>
{
    private const string Issuer = "https://idp.example.com";
    private readonly DorkNetServerFactory _factory;

    public ExternalSignInTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Linking_with_the_account_password_signs_in_and_the_next_sign_in_needs_no_password()
    {
        var (player, password) = await PlayerAsync();
        var subject = Guid.NewGuid().ToString("N");

        using var site = Client();
        var link = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password }, ExternalCookie(subject, "player@example.com"));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var session = SessionCookie(link);
        Assert.NotNull(session);

        var me = await GetJsonAsync(site, "/api/site/v1/account/me", session);
        Assert.True(me.GetProperty("signedIn").GetBoolean());
        Assert.Equal(player.Username, me.GetProperty("player").GetProperty("username").GetString());
        Assert.Equal("player@example.com", me.GetProperty("player").GetProperty("email").GetString());

        // Next time the provider identity alone is enough.
        await using var scope = _factory.Services.CreateAsyncScope();
        var signIn = scope.ServiceProvider.GetRequiredService<ExternalSignInService>();
        var stored = await signIn.FindAsync(Issuer, subject);
        Assert.Equal(player.Id, stored?.PlayerId);
    }

    [Fact]
    public async Task Wrong_passwords_are_refused_then_rate_limited()
    {
        var (player, _) = await PlayerAsync();
        var external = ExternalCookie(Guid.NewGuid().ToString("N"), "guess@example.com");
        using var site = Client();
        for (var i = 0; i < 5; i++)
        {
            using var r = await PostJsonAsync(site, "/api/site/v1/account/link",
                new { username = player.Username, password = "wrong-" + i }, external);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        using var blocked = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password = "still-wrong" }, external);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task An_identity_linked_to_one_account_cannot_be_linked_to_another()
    {
        var (first, firstPassword) = await PlayerAsync();
        var (second, secondPassword) = await PlayerAsync();
        var subject = Guid.NewGuid().ToString("N");
        using var site = Client();

        using var ok = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = first.Username, password = firstPassword }, ExternalCookie(subject, "a@example.com"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var refused = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = second.Username, password = secondPassword }, ExternalCookie(subject, "a@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Admin_sso_issues_tokens_only_to_admins()
    {
        var (admin, adminPassword) = await PlayerAsync(isAdmin: true);
        var (regular, regularPassword) = await PlayerAsync();
        using var admins = Client("admin");

        var adminSession = SessionCookie(await PostJsonAsync(admins, "/api/site/v1/account/link",
            new { username = admin.Username, password = adminPassword }, ExternalCookie(Guid.NewGuid().ToString("N"), "")));
        using var tokens = await SendAsync(admins, HttpMethod.Get, "/api/admin/v1/sso", adminSession);
        Assert.Equal(HttpStatusCode.OK, tokens.StatusCode);
        var body = JsonDocument.Parse(await tokens.Content.ReadAsStringAsync()).RootElement;
        Assert.False(string.IsNullOrEmpty(body.GetProperty("access_token").GetString()));
        Assert.Equal(admin.Id, body.GetProperty("account_id").GetInt64());

        var regularSession = SessionCookie(await PostJsonAsync(admins, "/api/site/v1/account/link",
            new { username = regular.Username, password = regularPassword }, ExternalCookie(Guid.NewGuid().ToString("N"), "")));
        using var refused = await SendAsync(admins, HttpMethod.Get, "/api/admin/v1/sso", regularSession);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var anonymous = await SendAsync(admins, HttpMethod.Get, "/api/admin/v1/sso", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Unlinking_ends_the_session()
    {
        var (player, password) = await PlayerAsync();
        using var site = Client();
        var session = SessionCookie(await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password }, ExternalCookie(Guid.NewGuid().ToString("N"), "")));

        using var unlink = await SendAsync(site, HttpMethod.Post, "/api/site/v1/account/unlink", session);
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);

        // The old cookie is still in the browser, but the link behind it is gone.
        var me = await GetJsonAsync(site, "/api/site/v1/account/me", session);
        Assert.False(me.GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task A_banned_player_cannot_link_and_loses_an_existing_session()
    {
        var (player, password) = await PlayerAsync();
        using var site = Client();
        var session = SessionCookie(await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password }, ExternalCookie(Guid.NewGuid().ToString("N"), "")));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            await db.Players.Where(p => p.Id == player.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.BannedUntil, DateTime.UtcNow.AddDays(1)));
        }

        var me = await GetJsonAsync(site, "/api/site/v1/account/me", session);
        Assert.False(me.GetProperty("signedIn").GetBoolean());
        using var relink = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password }, ExternalCookie(Guid.NewGuid().ToString("N"), ""));
        Assert.Equal(HttpStatusCode.Forbidden, relink.StatusCode);
    }

    [Fact]
    public async Task Cookie_keys_are_stored_encrypted()
    {
        var (player, password) = await PlayerAsync();
        using var site = Client();
        using var link = await PostJsonAsync(site, "/api/site/v1/account/link",
            new { username = player.Username, password }, ExternalCookie(Guid.NewGuid().ToString("N"), ""));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        var stored = await db.DataProtectionKeys.Select(k => k.Xml).ToListAsync();
        Assert.NotEmpty(stored);
        Assert.All(stored, xml =>
        {
            Assert.Contains("dorknetEncryptedKey", xml);
            Assert.DoesNotContain("masterKey", xml);
        });
    }

    [Fact]
    public void Key_encryption_round_trips_and_rejects_another_secret()
    {
        var element = new System.Xml.Linq.XElement("key", new System.Xml.Linq.XElement("masterKey", "secret-bytes"));
        var encrypted = new DataProtectionKeyEncryption("first-secret").Encrypt(element).EncryptedElement;
        Assert.DoesNotContain("secret-bytes", encrypted.ToString());
        Assert.Equal(element.ToString(), new DataProtectionKeyEncryption("first-secret").DecryptElement(encrypted).ToString());
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => new DataProtectionKeyEncryption("other-secret").DecryptElement(encrypted));
    }

    [Fact]
    public async Task Downloads_can_require_sign_in()
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("DorkNet:Downloads:BaseUrl", "http://127.0.0.1:9");
            b.UseSetting("DorkNet:Downloads:RequireSignIn", "true");
        });
        using var site = factory.CreateClient(new() { BaseAddress = new Uri($"http://{_factory.ApexDomain}") });
        using var manifest = await site.GetAsync("/api/site/v1/downloads");
        Assert.Equal(HttpStatusCode.Unauthorized, manifest.StatusCode);
        using var file = await site.GetAsync("/api/site/v1/downloads/files/2023.03/DorkNet-2023.03-Quest.xapk");
        Assert.Equal(HttpStatusCode.Unauthorized, file.StatusCode);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<(PlayerEntity Player, string Password)> PlayerAsync(bool isAdmin = false)
    {
        var password = "pw-" + Guid.NewGuid().ToString("N")[..12];
        var player = new PlayerEntity
        {
            Id = 9_400_000 + Random.Shared.Next(1, 99_999),
            Username = "sso" + Guid.NewGuid().ToString("N")[..10],
            DisplayName = "SSO test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = isAdmin,
        };
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return (player, password);
    }

    /// <summary>The cookie the provider callback leaves behind: the provider
    /// identity in the DorkNet.External scheme, in the app's cookie format.</summary>
    private string ExternalCookie(string subject, string email)
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(ExternalSignIn.ExternalScheme);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("iss", Issuer), new Claim("sub", subject), new Claim("email", email),
        }, "test");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ExternalSignIn.ExternalScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    private static string? SessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("dorknet_session=") && v.Length > "dorknet_session=".Length)
            : null;

    private HttpClient Client(string? subdomain = null)
    {
        var host = subdomain is null ? _factory.ApexDomain : $"{subdomain}.{_factory.ApexDomain}";
        return _factory.CreateClient(new() { BaseAddress = new Uri($"http://{host}"), HandleCookies = false });
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? cookie)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, string? cookie)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path, cookie);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
