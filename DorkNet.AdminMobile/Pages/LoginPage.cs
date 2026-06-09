using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile.Pages;

public sealed class LoginPage : ContentPage
{
    private readonly AdminApiClient api;
    private readonly SecureAdminSettings settings;
    private readonly Entry baseUrl = AppDesign.Entry("https://admin.example.com", Keyboard.Url);
    private readonly Entry cfClientId = AppDesign.Entry("Cloudflare Access Client ID");
    private readonly Entry cfClientSecret = AppDesign.Entry("Cloudflare Access Client Secret", secret: true);
    private readonly Entry cfJwt = AppDesign.Entry("Cloudflare Access JWT assertion", secret: true);
    private readonly Entry username = AppDesign.Entry("Admin username");
    private readonly Entry password = AppDesign.Entry("Password", secret: true);
    private readonly Label status = AppDesign.StatusLabel();
    private readonly ActivityIndicator busy = new() { IsVisible = false };
    private bool loaded;

    public LoginPage(AdminApiClient api, SecureAdminSettings settings)
    {
        this.api = api;
        this.settings = settings;
        Title = "DorkNet Admin";
        Background = AppDesign.PageBackground;

        var login = AppDesign.PrimaryButton("Sign in");
        login.Clicked += OnLogin;

        var continueSaved = AppDesign.SecondaryButton("Continue saved session");
        continueSaved.Clicked += async (_, _) =>
        {
            if (await settings.GetJwtAsync() is { Length: > 0 }) App.ShowAdmin();
            else status.Text = "No saved DorkNet admin session is stored.";
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 28),
                Spacing = 14,
                Children =
                {
                    AppDesign.Title("Admin mobile"),
                    new Label
                    {
                        Text = "Connect to a DorkNet admin host. Cloudflare Access service tokens are sent with every request when configured.",
                        TextColor = AppDesign.Muted,
                        FontSize = 14,
                    },
                    AppDesign.GlassCard(new VerticalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            AppDesign.Section("Connection"),
                            baseUrl,
                            cfClientId,
                            cfClientSecret,
                            cfJwt,
                        },
                    }),
                    AppDesign.GlassCard(new VerticalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            AppDesign.Section("DorkNet admin login"),
                            username,
                            password,
                            login,
                            continueSaved,
                        },
                    }),
                    busy,
                    status,
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (loaded) return;
        loaded = true;
        var saved = await settings.LoadConnectionAsync();
        baseUrl.Text = saved.BaseUrl;
        cfClientId.Text = saved.CloudflareAccessClientId;
        cfClientSecret.Text = saved.CloudflareAccessClientSecret;
        cfJwt.Text = saved.CloudflareAccessJwt;
    }

    private async void OnLogin(object? sender, EventArgs e)
    {
        busy.IsVisible = true;
        busy.IsRunning = true;
        status.Text = string.Empty;
        try
        {
            await settings.SaveConnectionAsync(new AdminConnectionSettings(
                baseUrl.Text ?? string.Empty,
                cfClientId.Text ?? string.Empty,
                cfClientSecret.Text ?? string.Empty,
                cfJwt.Text ?? string.Empty));
            await api.LoginAsync(username.Text ?? string.Empty, password.Text ?? string.Empty);
            App.ShowAdmin();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            busy.IsRunning = false;
            busy.IsVisible = false;
        }
    }

}
