using System.Text.Json;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Tests;

public sealed class RoomServiceTests
{
    [Fact]
    public void ToWireRoom_never_emits_empty_image_name()
    {
        var room = new RoomEntity
        {
            Id = 1001,
            Name = "CustomRoomWithoutImage",
            Description = "Custom room",
            ImageName = "",
            Accessibility = 1,
            State = 0,
        };

        var json = JsonSerializer.Serialize(RoomService.ToWireRoom(room));
        using var document = JsonDocument.Parse(json);

        Assert.Equal(RoomService.DefaultRoomImageName, document.RootElement.GetProperty("ImageName").GetString());
    }

    [Fact]
    public void ResolveDisplayImageName_preserves_existing_image_name()
    {
        var room = new RoomEntity
        {
            Name = "Paintball",
            ImageName = "custom-thumbnail.png",
        };

        Assert.Equal("custom-thumbnail.png", RoomService.ResolveDisplayImageName(room));
    }
}
