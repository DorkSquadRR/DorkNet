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
    private bool loaded;

    public RoomsPage(AdminApiClient api)
    {
        this.api = api;
        Title = "Rooms";
        Background = AppDesign.PageBackground;

        rooms.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { TextColor = AppDesign.Text, FontAttributes = FontAttributes.Bold, FontSize = 15 };
            name.SetBinding(Label.TextProperty, nameof(RoomSummary.Name));
            var meta = new Label { TextColor = AppDesign.Muted, FontSize = 12 };
            meta.SetBinding(Label.TextProperty, new Binding(nameof(RoomSummary.Id), stringFormat: "Room #{0}"));
            return AppDesign.GlassCard(new VerticalStackLayout
            {
                Children = { name, meta },
            }, new Thickness(12, 9));
        });

        instances.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { TextColor = AppDesign.Text, FontAttributes = FontAttributes.Bold, FontSize = 15 };
            name.SetBinding(Label.TextProperty, nameof(InstanceSummary.RoomName));
            var meta = new Label { TextColor = AppDesign.Muted, FontSize = 12 };
            meta.SetBinding(Label.TextProperty, new Binding(nameof(InstanceSummary.PhotonRegionId), stringFormat: "Region {0}"));
            var count = new Label { TextColor = AppDesign.Subtle, FontSize = 12 };
            count.SetBinding(Label.TextProperty, new Binding("Participants.Count", stringFormat: "{0} participants"));
            return AppDesign.GlassCard(new VerticalStackLayout
            {
                Children = { name, meta, count },
            }, new Thickness(12, 9));
        });

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
                    AppDesign.Title("Rooms"),
                    refresh,
                    new Label { Text = "Live instances", TextColor = AppDesign.Muted, FontAttributes = FontAttributes.Bold },
                    instances,
                    new Label { Text = "Recent rooms", TextColor = AppDesign.Muted, FontAttributes = FontAttributes.Bold },
                    rooms,
                    status,
                },
            },
        };
    }

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
    }
}
