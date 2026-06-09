using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;

namespace DorkNet.AdminMobile.Pages;

public sealed class AdminTabbedPage : TabbedPage
{
    public AdminTabbedPage(
        DashboardPage dashboard,
        PlayersPage players,
        RoomsPage rooms,
        SettingsPage settings)
    {
        BarBackgroundColor = AppDesign.Canvas;
        BarTextColor = AppDesign.Text;
        SelectedTabColor = AppDesign.PlatformAccent;
        UnselectedTabColor = AppDesign.Subtle;
        Children.Add(dashboard);
        Children.Add(players);
        Children.Add(rooms);
        Children.Add(settings);
    }
}
