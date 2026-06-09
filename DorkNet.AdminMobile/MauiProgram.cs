using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using DorkNet.AdminMobile.Pages;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<SecureAdminSettings>();
        builder.Services.AddSingleton<AdminApiClient>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<AdminTabbedPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<PlayersPage>();
        builder.Services.AddTransient<RoomsPage>();
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();
        AppServices.Services = app.Services;
        return app;
    }
}

public static class AppServices
{
    public static IServiceProvider Services { get; set; } = default!;
}
