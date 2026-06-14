using Microsoft.Maui.Controls;
using DorkNet.AdminMobile.Design;
using DorkNet.AdminMobile.Models;
using DorkNet.AdminMobile.Services;

namespace DorkNet.AdminMobile.Pages;

public sealed class RoomsPage : ContentPage
{
    private readonly AdminApiClient api;
    private readonly CollectionView rooms = new();
    private readonly CollectionView instances = new();
    private readonly Label status = AppDesign.StatusLabel();
    private readonly Button refresh = AppDesign.SecondaryButton("Refresh");
    private bool loaded;

    public RoomsPage(AdminApiClient api)
    {
        this.api = api;
        Title = "Rooms";
        Background = AppDesign.PageBackground;

        rooms.SelectionMode = SelectionMode.None;
        rooms.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem;
        rooms.EmptyView = EmptyList("No recent rooms");
        rooms.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { TextColor = AppDesign.Text, FontAttributes = FontAttributes.Bold, FontSize = 15 };
            name.SetBinding(Label.TextProperty, nameof(RoomSummary.Name));
            var meta = new Label { TextColor = AppDesign.Muted, FontSize = 12 };
            meta.SetBinding(Label.TextProperty, new Binding(nameof(RoomSummary.Id), stringFormat: "Room #{0}"));
            var row = new VerticalStackLayout
            {
                Padding = new Thickness(12, 9),
                Spacing = 2,
                BackgroundColor = AppDesign.RowSurface,
                Children = { name, meta },
            };
            return new VerticalStackLayout { Spacing = 0, Children = { row, AppDesign.Divider() } };
        });

        instances.SelectionMode = SelectionMode.None;
        instances.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem;
        instances.EmptyView = EmptyList("No active instances");
        instances.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { TextColor = AppDesign.Text, FontAttributes = FontAttributes.Bold, FontSize = 15 };
            name.SetBinding(Label.TextProperty, nameof(InstanceSummary.RoomName));
            var meta = new Label { TextColor = AppDesign.Muted, FontSize = 12 };
            meta.SetBinding(Label.TextProperty, new Binding(nameof(InstanceSummary.PhotonRegionId), stringFormat: "Region {0}"));
            var count = new Label { TextColor = AppDesign.Subtle, FontSize = 12 };
            count.SetBinding(Label.TextProperty, new Binding("Participants.Count", stringFormat: "{0} participants"));
            var row = new VerticalStackLayout
            {
                Padding = new Thickness(12, 9),
                Spacing = 2,
                BackgroundColor = AppDesign.RowSurface,
                Children = { name, meta, count },
            };
            return new VerticalStackLayout { Spacing = 0, Children = { row, AppDesign.Divider() } };
        });

        refresh.Clicked += async (_, _) => await LoadAsync();

        var grid = new Grid
        {
            Padding = 16,
            RowSpacing = 10,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(2, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        grid.Add(AppDesign.Title("Rooms"), 0, 0);
        grid.Add(refresh, 0, 1);
        grid.Add(new Label { Text = "Live instances", TextColor = AppDesign.Muted, FontAttributes = FontAttributes.Bold }, 0, 2);
        grid.Add(instances, 0, 3);
        grid.Add(new Label { Text = "Recent rooms", TextColor = AppDesign.Muted, FontAttributes = FontAttributes.Bold }, 0, 4);
        grid.Add(rooms, 0, 5);
        grid.Add(status, 0, 6);
        Content = grid;
    }

    private static Label EmptyList(string text) => new()
    {
        Text = text,
        TextColor = AppDesign.Subtle,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center,
    };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!loaded)
        {
            loaded = true;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            refresh.IsEnabled = false;
            status.Text = "Loading...";
            var instanceRows = await api.GetInstancesAsync();
            var roomRows = await api.GetRoomsAsync();
            instances.ItemsSource = instanceRows;
            rooms.ItemsSource = roomRows;
            status.Text = $"{instanceRows.Count} active instances, {roomRows.Count} rooms loaded";
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
}
