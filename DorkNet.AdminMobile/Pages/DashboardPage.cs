using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Models;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile.Pages;

public sealed class DashboardPage : ContentPage
{
    private readonly AdminApiClient api;
    private readonly Label status = AppDesign.StatusLabel();
    private readonly Grid metrics = new() { ColumnSpacing = 10, RowSpacing = 10 };

    public DashboardPage(AdminApiClient api)
    {
        this.api = api;
        Title = "Dashboard";
        Background = AppDesign.PageBackground;

        var refresh = AppDesign.SecondaryButton("Refresh");
        refresh.Clicked += async (_, _) => await LoadAsync();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 14,
                Children =
                {
                    AppDesign.Title("Operations"),
                    refresh,
                    metrics,
                    status,
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (metrics.Children.Count == 0) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            status.Text = "Loading...";
            var s = await api.GetStatsAsync();
            DrawMetrics(s);
            status.Text = $"Server time {s.ServerTime:g}";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
    }

    private void DrawMetrics(AdminStats s)
    {
        metrics.Children.Clear();
        metrics.RowDefinitions.Clear();
        metrics.ColumnDefinitions.Clear();
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.ColumnDefinitions.Add(new ColumnDefinition());
        AddMetric(0, 0, "Players", $"{s.Players.Total}", $"{s.Players.OnlineNow} online");
        AddMetric(0, 1, "Rooms", $"{s.Rooms.Total}", $"{s.Rooms.ActiveSessionCount} active sessions");
        AddMetric(1, 0, "Moderation", $"{s.Moderation.OpenReports}", "open reports");
        AddMetric(1, 1, "Traffic", $"{s.Rooms.InGamePlayerCount}", $"{s.Rooms.TotalVisits:n0} visits");
        AddMetric(2, 0, "New today", $"{s.Players.NewToday}", "players");
        AddMetric(2, 1, "Photos", $"{s.Photos.Today}", "last 24h");
    }

    private void AddMetric(int row, int col, string label, string value, string caption)
    {
        while (metrics.RowDefinitions.Count <= row) metrics.RowDefinitions.Add(new RowDefinition());
        var tile = AppDesign.GlassCard(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = label, TextColor = AppDesign.Subtle, FontSize = 12 },
                new Label { Text = value, TextColor = AppDesign.Text, FontSize = 28, FontAttributes = FontAttributes.Bold },
                new Label { Text = caption, TextColor = AppDesign.Muted, FontSize = 12 },
            },
        }, new Thickness(14));
        metrics.Add(tile, col, row);
    }
}
