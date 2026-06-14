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
    private readonly Button refresh = AppDesign.SecondaryButton("Refresh");
    private List<PlayerSummary> players = [];

    public PlayersPage(AdminApiClient api)
    {
        this.api = api;
        Title = "Players";
        Background = AppDesign.PageBackground;

        refresh.Clicked += async (_, _) => await LoadAsync();
        search.Completed += async (_, _) => await LoadAsync();

        list.SelectionMode = SelectionMode.Single;
        list.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem;
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is PlayerSummary player)
            {
                list.SelectedItem = null;
                await PlayerActionsAsync(player);
            }
        };
        list.EmptyView = new Label
        {
            Text = "No players loaded",
            TextColor = AppDesign.Subtle,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
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
            var chevron = new Label
            {
                Text = ">",
                TextColor = AppDesign.Subtle,
                FontSize = 18,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var row = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition(),
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Padding = new Thickness(12, 10),
                RowSpacing = 2,
                ColumnSpacing = 10,
                BackgroundColor = AppDesign.RowSurface,
            };
            row.Add(name, 0, 0);
            row.Add(online, 1, 0);
            row.Add(handle, 0, 1);
            row.Add(detail, 0, 2);
            row.Add(chevron, 2, 0);
            Grid.SetRowSpan(chevron, 3);

            return new VerticalStackLayout
            {
                Spacing = 0,
                Children = { row, AppDesign.Divider() },
            };
        });

        var searchRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        searchRow.Add(search, 0, 0);
        searchRow.Add(refresh, 1, 0);

        var grid = new Grid
        {
            Padding = 16,
            RowSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition(),
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        grid.Add(AppDesign.Title("Players"), 0, 0);
        grid.Add(searchRow, 0, 1);
        grid.Add(list, 0, 2);
        grid.Add(status, 0, 3);
        Content = grid;
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
            refresh.IsEnabled = false;
            status.Text = "Loading...";
            players = await api.GetPlayersAsync(search.Text);
            list.ItemsSource = players;
            status.Text = $"{players.Count} players loaded";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            refresh.IsEnabled = true;
        }
    }

    private async Task PlayerActionsAsync(PlayerSummary player)
    {
        var action = await DisplayActionSheet(
            $"@{player.Username}",
            "Cancel",
            "Remove account",
            "Kick",
            "Reset avatar");

        switch (action)
        {
            case "Kick":
                await ConfirmAction($"Kick @{player.Username}?", () => api.KickPlayerAsync(player.Id, "Kicked from mobile admin."));
                break;
            case "Reset avatar":
                await ConfirmAction($"Reset @{player.Username}'s avatar?", () => api.ResetAvatarAsync(player.Id));
                break;
            case "Remove account":
                await RemoveAccountAsync(player);
                break;
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

    private async Task RemoveAccountAsync(PlayerSummary player)
    {
        var username = await DisplayPromptAsync(
            "Verify username",
            $"Type {player.Username} exactly to remove @{player.Username}.",
            "Next",
            "Cancel",
            maxLength: Math.Max(32, player.Username.Length),
            keyboard: Keyboard.Text);
        if (!string.Equals(username?.Trim(), player.Username, StringComparison.Ordinal)) return;

        var phrase = $"DELETE {player.Id}";
        var typedPhrase = await DisplayPromptAsync(
            "Verify removal",
            $"Type {phrase} exactly. This cannot be undone.",
            "Remove",
            "Cancel",
            maxLength: phrase.Length,
            keyboard: Keyboard.Text);
        if (!string.Equals(typedPhrase?.Trim(), phrase, StringComparison.Ordinal)) return;

        if (!await DisplayAlert("Remove account", $"Permanently remove @{player.Username}?", "Remove", "Cancel")) return;

        try
        {
            status.Text = $"Removing @{player.Username}...";
            await api.DeletePlayerAsync(player.Id, player.Username, phrase, "Removed from mobile admin.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Removal failed", ex.Message, "OK");
        }
    }
}
