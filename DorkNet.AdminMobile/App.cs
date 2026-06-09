using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Pages;

namespace DorkNet.AdminMobile;

public class App : Application
{
    public App(LoginPage loginPage)
    {
        UserAppTheme = AppTheme.Dark;
        MainPage = new NavigationPage(loginPage)
        {
            BarBackgroundColor = AppDesign.Canvas,
            BarTextColor = AppDesign.Text,
        };
    }

    public static void ShowAdmin()
    {
        Current!.MainPage = new NavigationPage(
            AppServices.Services.GetRequiredService<AdminTabbedPage>())
        {
            BarBackgroundColor = AppDesign.Canvas,
            BarTextColor = AppDesign.Text,
        };
    }

    public static void ShowLogin()
    {
        Current!.MainPage = new NavigationPage(
            AppServices.Services.GetRequiredService<LoginPage>())
        {
            BarBackgroundColor = AppDesign.Canvas,
            BarTextColor = AppDesign.Text,
        };
    }
}
