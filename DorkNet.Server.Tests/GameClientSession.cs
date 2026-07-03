using System.Net.Http.Headers;
using System.Text.Json;

namespace DorkNet.Server.Tests;

public sealed record GameClientSession(
    long PlayerId,
    string AccessToken,
    string DeviceId,
    string PlatformId);

public static class GameClientSessionFactory
{
    public static async Task<GameClientSession> CreateAsync(
        HttpClient client,
        string apexDomain)
    {
        var deviceId = $"endpoint-contract-device-{Guid.NewGuid():N}";
        var platformId = $"endpoint-contract-platform-{Guid.NewGuid():N}";
        var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"http://auth.{apexDomain}/connect/token"));
        tokenRequest.Headers.Accept.ParseAdd("application/json");
        tokenRequest.Headers.UserAgent.ParseAdd("RecRoom/2023.03.21");
        tokenRequest.Headers.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "recroom",
            ["client_secret"] = "VxZ53kgbbEaRoZAeMe00MagtgD12GLL2",
            ["username"] = "EndpointContract",
            ["device_id"] = deviceId,
            ["platform"] = "0",
            ["platform_id"] = platformId,
        });

        using var tokenResponse = await client.SendAsync(tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var body = await tokenResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Test player token request failed with {(int)tokenResponse.StatusCode} {tokenResponse.StatusCode}: {body}");
        }

        using var tokenJson = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync());
        var accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response did not include access_token.");

        var userInfoRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"http://auth.{apexDomain}/connect/userinfo"));
        userInfoRequest.Headers.Accept.ParseAdd("application/json");
        userInfoRequest.Headers.UserAgent.ParseAdd("RecRoom/2023.03.21");
        userInfoRequest.Headers.TryAddWithoutValidation("X-DorkNet-Version", "march_2023_03_21");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var userInfoResponse = await client.SendAsync(userInfoRequest);
        userInfoResponse.EnsureSuccessStatusCode();

        using var userInfoJson = await JsonDocument.ParseAsync(
            await userInfoResponse.Content.ReadAsStreamAsync());
        var playerId = long.Parse(userInfoJson.RootElement.GetProperty("sub").GetString()!);

        return new GameClientSession(playerId, accessToken, deviceId, platformId);
    }
}
