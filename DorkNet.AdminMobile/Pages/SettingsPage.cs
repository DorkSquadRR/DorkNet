using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile.Pages;

public sealed class SettingsPage : ContentPage
{
    private readonly AdminApiClient api;
    private readonly SecureAdminSettings settings;
    private readonly Switch signupsDisabled = new();
    private readonly Switch globalFriends = new();
    private readonly Label status = AppDesign.StatusLabel();
    private bool loading;

    public SettingsPage(AdminApiClient api, SecureAdminSettings settings)
    {
        this.api = api;
        this.settings = settings;
        Title = "Settings";
        Background = AppDesign.PageBackground;

        signupsDisabled.Toggled += async (_, e) =>
        {
            if (!loading) await SaveToggleAsync(() => api.SetSignupsDisabledAsync(e.Value));
        };
        globalFriends.Toggled += async (_, e) =>
        {
            if (!loading) await SaveToggleAsync(() => api.SetGlobalFriendsAsync(e.Value));
        };

        var refresh = AppDesign.SecondaryButton("Refresh");
        refresh.Clicked += async (_, _) => await LoadAsync();
        var logout = AppDesign.DangerButton("Clear saved session");
        logout.Clicked += (_, _) =>
        {
            settings.ClearSession();
            App.ShowLogin();
        };

        Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 14,
            Children =
            {
                AppDesign.Title("Settings"),
                refresh,
                Row("Disable new account creation", signupsDisabled),
                Row("Global friends mode", globalFriends),
                logout,
                status,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            loading = true;
            status.Text = "Loading...";
            var s = await api.GetSettingsAsync();
            signupsDisabled.IsToggled = s.SignupsDisabled;
            globalFriends.IsToggled = s.GlobalFriendsEnabled;
            status.Text = $"Updated {s.UpdatedAt:g}";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task SaveToggleAsync(Func<Task> save)
    {
        try
        {
            status.Text = "Saving...";
            await save();
            status.Text = "Saved";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            await LoadAsync();
        }
    }

    private static View Row(string label, View control)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Padding = new Thickness(12, 10),
        };
        grid.Add(new Label
        {
            Text = label,
            TextColor = AppDesign.Text,
            VerticalTextAlignment = TextAlignment.Center,
        }, 0, 0);
        grid.Add(control, 1, 0);
        return AppDesign.GlassCard(grid, new Thickness(0));
    }
}
