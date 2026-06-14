using Microsoft.Maui.Storage;

namespace DorkNet.AdminMobile.Services;

public sealed record AdminConnectionSettings(
    string BaseUrl,
    string CloudflareAccessClientId,
    string CloudflareAccessClientSecret,
    string CloudflareAccessJwt);

public sealed class SecureAdminSettings
{
    private const string BaseUrlKey = "admin.baseUrl";
    private const string JwtKey = "admin.jwt";
    private const string RefreshKey = "admin.refresh";
    private const string CfClientIdKey = "cf.access.clientId";
    private const string CfClientSecretKey = "cf.access.clientSecret";
    private const string CfJwtKey = "cf.access.jwt";

    public async Task<AdminConnectionSettings> LoadConnectionAsync()
    {
        try
        {
            var baseUrl = await SecureStorage.GetAsync(BaseUrlKey) ?? "https://admin.rec.net";
            var cfId = await SecureStorage.GetAsync(CfClientIdKey) ?? string.Empty;
            var cfSecret = await SecureStorage.GetAsync(CfClientSecretKey) ?? string.Empty;
            var cfJwt = await SecureStorage.GetAsync(CfJwtKey) ?? string.Empty;
            return new AdminConnectionSettings(baseUrl, cfId, cfSecret, cfJwt);
        }
        catch
        {
            SecureStorage.Remove(BaseUrlKey);
            SecureStorage.Remove(CfClientIdKey);
            SecureStorage.Remove(CfClientSecretKey);
            SecureStorage.Remove(CfJwtKey);
            return new AdminConnectionSettings("https://admin.rec.net", string.Empty, string.Empty, string.Empty);
        }
    }

    public async Task SaveConnectionAsync(AdminConnectionSettings settings)
    {
        await SecureStorage.SetAsync(BaseUrlKey, settings.BaseUrl.TrimEnd('/'));
        await SecureStorage.SetAsync(CfClientIdKey, settings.CloudflareAccessClientId.Trim());
        await SecureStorage.SetAsync(CfClientSecretKey, settings.CloudflareAccessClientSecret.Trim());
        await SecureStorage.SetAsync(CfJwtKey, settings.CloudflareAccessJwt.Trim());
    }

    public Task<string?> GetJwtAsync() => SecureStorage.GetAsync(JwtKey);

    public async Task SaveSessionAsync(string accessToken, string refreshToken)
    {
        await SecureStorage.SetAsync(JwtKey, accessToken);
        await SecureStorage.SetAsync(RefreshKey, refreshToken);
    }

    public void ClearSession()
    {
        SecureStorage.Remove(JwtKey);
        SecureStorage.Remove(RefreshKey);
    }
}
