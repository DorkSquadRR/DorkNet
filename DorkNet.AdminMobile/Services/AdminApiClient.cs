using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DorkNet.AdminMobile.Models;

namespace DorkNet.AdminMobile.Services;

public sealed class AdminApiClient(SecureAdminSettings settings)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<AdminLoginResponse> LoginAsync(string username, string password)
    {
        var result = await SendAsync<AdminLoginResponse>(
            HttpMethod.Post,
            "login",
            new { Username = username, Password = password },
            includeJwt: false);

        await settings.SaveSessionAsync(result.AccessToken, result.RefreshToken);
        return result;
    }

    public Task<AdminStats> GetStatsAsync() =>
        SendAsync<AdminStats>(HttpMethod.Get, "stats");

    public Task<List<PlayerSummary>> GetPlayersAsync(string? query = null)
    {
        var suffix = string.IsNullOrWhiteSpace(query)
            ? "players?take=100"
            : $"players?take=100&query={Uri.EscapeDataString(query)}";
        return SendAsync<List<PlayerSummary>>(HttpMethod.Get, suffix);
    }

    public Task<List<RoomSummary>> GetRoomsAsync() =>
        SendAsync<List<RoomSummary>>(HttpMethod.Get, "rooms?take=100");

    public Task<List<InstanceSummary>> GetInstancesAsync() =>
        SendAsync<List<InstanceSummary>>(HttpMethod.Get, "instances");

    public Task<ServerSettingsDto> GetSettingsAsync() =>
        SendAsync<ServerSettingsDto>(HttpMethod.Get, "settings");

    public Task<ServerSettingsDto> SetSignupsDisabledAsync(bool disabled) =>
        SendAsync<ServerSettingsDto>(HttpMethod.Post, "settings/signups", new { Disabled = disabled });

    public Task<ServerSettingsDto> SetGlobalFriendsAsync(bool enabled) =>
        SendAsync<ServerSettingsDto>(HttpMethod.Post, "settings/global-friends", new { Enabled = enabled });

    public Task KickPlayerAsync(long playerId, string reason) =>
        SendAsync<object>(HttpMethod.Post, $"players/{playerId}/kick", new { Reason = reason });

    public Task ResetAvatarAsync(long playerId) =>
        SendAsync<object>(HttpMethod.Post, $"players/{playerId}/avatar/reset");

    public Task DeletePlayerAsync(long playerId, string username, string phrase, string? reason) =>
        SendAsync<object>(
            HttpMethod.Delete,
            $"players/{playerId}",
            new { ConfirmUsername = username, ConfirmPhrase = phrase, Reason = reason });

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        bool includeJwt = true)
    {
        var connection = await settings.LoadConnectionAsync();
        var url = $"{connection.BaseUrl.TrimEnd('/')}/api/admin/v1/{path.TrimStart('/')}";
        using var request = new HttpRequestMessage(method, url);
        AddCloudflareAccessHeaders(request, connection);

        if (includeJwt)
        {
            var jwt = await settings.GetJwtAsync();
            if (!string.IsNullOrWhiteSpace(jwt))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, Json);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new AdminApiException((int)response.StatusCode, ExtractError(text, response.ReasonPhrase));

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text))
            return default!;

        return JsonSerializer.Deserialize<T>(text, Json)
            ?? throw new AdminApiException(0, "Server returned an empty response.");
    }

    private static void AddCloudflareAccessHeaders(HttpRequestMessage request, AdminConnectionSettings connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.CloudflareAccessClientId) &&
            !string.IsNullOrWhiteSpace(connection.CloudflareAccessClientSecret))
        {
            request.Headers.SetOrReplace("CF-Access-Client-Id", connection.CloudflareAccessClientId);
            request.Headers.SetOrReplace("CF-Access-Client-Secret", connection.CloudflareAccessClientSecret);
        }

        if (!string.IsNullOrWhiteSpace(connection.CloudflareAccessJwt))
            request.Headers.SetOrReplace("CF-Access-Jwt-Assertion", connection.CloudflareAccessJwt);
    }

    private static string ExtractError(string text, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback ?? "Request failed.";
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? fallback ?? "Request failed.";
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? fallback ?? "Request failed.";
        }
        catch { }
        return text.Length > 300 ? text[..300] : text;
    }
}

public sealed class AdminApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal static class HeaderExtensions
{
    public static void SetOrReplace(this HttpRequestHeaders headers, string name, string value)
    {
        headers.Remove(name);
        headers.Add(name, value);
    }
}
