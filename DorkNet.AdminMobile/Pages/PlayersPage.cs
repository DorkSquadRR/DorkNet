using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Models;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile.Pages;

public sealed class PlayersPage : ContentPage
{
    private readonly AdminApiClient api;
    private readonly Entry search = AppDesign.Entry("Search players");
    private readonly CollectionView list = new();
    private readonly Label status = AppDesign.StatusLabel();
    private List<PlayerSummary> players = [];

    public PlayersPage(AdminApiClient api)
    {
        this.api = api;
        Title = "Players";
        Background = AppDesign.PageBackground;

        var refresh = AppDesign.SecondaryButton("Refresh");
        refresh.Clicked += async (_, _) => await LoadAsync();
        search.Completed += async (_, _) => await LoadAsync();

        list.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { TextColor = AppDesign.Text, FontAttributes = FontAttributes.Bold, FontSize = 16 };
            name.SetBinding(Label.TextProperty, nameof(PlayerSummary.DisplayName));
            var handle = new Label { TextColor = AppDesign.Muted, FontSize = 13 };
            handle.SetBinding(Label.TextProperty, new Binding(nameof(PlayerSummary.Username), stringFormat: "@{0}"));
            var detail = new Label { TextColor = AppDesign.Subtle, FontSize = 12 };
            detail.SetBinding(Label.TextProperty, new Binding(nameof(PlayerSummary.Level), stringFormat: "Level {0}"));
            var online = new Label { TextColor = AppDesign.AndroidAccent, FontSize = 12, FontAttributes = FontAttributes.Bold };
            online.SetBinding(Label.IsVisibleProperty, nameof(PlayerSummary.Online));
            online.Text = "ONLINE";

            var kick = MiniButton("Kick");
            kick.Clicked += async (s, _) =>
            {
                if ((s as Button)?.BindingContext is PlayerSummary p)
                    await ConfirmAction($"Kick @{p.Username}?", () => api.KickPlayerAsync(p.Id, "Kicked from mobile admin."));
            };
            var resetAvatar = MiniButton("Reset avatar");
            resetAvatar.Clicked += async (s, _) =>
            {
                if ((s as Button)?.BindingContext is PlayerSummary p)
                    await ConfirmAction($"Reset @{p.Username}'s avatar?", () => api.ResetAvatarAsync(p.Id));
            };

            var actions = new HorizontalStackLayout { Spacing = 8, Children = { kick, resetAvatar } };
            return AppDesign.GlassCard(new VerticalStackLayout
            {
                Spacing = 6,
                Children = { name, handle, detail, online, actions },
            }, new Thickness(12, 10));
        });

        Content = new VerticalStackLayout
        {
            Padding = 16,
            Spacing = 12,
            Children =
            {
                AppDesign.Title("Players"),
                new HorizontalStackLayout { Spacing = 8, Children = { search, refresh } },
                list,
                status,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (players.Count == 0) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            status.Text = "Loading...";
            players = await api.GetPlayersAsync(search.Text);
            list.ItemsSource = players;
            status.Text = $"{players.Count} players loaded";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
    }

    private async Task ConfirmAction(string message, Func<Task> action)
    {
        if (!await DisplayAlert("Confirm", message, "Run", "Cancel")) return;
        try
        {
            await action();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Request failed", ex.Message, "OK");
        }
    }

    private static Button MiniButton(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Padding = new Thickness(10, 6),
        CornerRadius = 6,
        BackgroundColor = AppDesign.SurfaceLifted,
        TextColor = AppDesign.Text,
    };
}
