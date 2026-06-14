using Android.App;
using Android.Content.PM;
using Microsoft.Maui;

namespace DorkNet.AdminMobile;

[Activity(
    Label = "DorkNet Admin",
    Icon = "@mipmap/appicon",
    RoundIcon = "@mipmap/appicon_round",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Maui.SplashTheme",
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
