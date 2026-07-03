using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DorkNet.Server.Data;

namespace DorkNet.Server.Tests;

public sealed class CanonicalRoomBootstrapTests : IClassFixture<DorkNetServerFactory>
{
    private readonly DorkNetServerFactory _factory;

    public CanonicalRoomBootstrapTests(DorkNetServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Bootstrap_seeds_stunt_runner_subroom_and_rec_rally_remote_image_name()
    {
        using var _ = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();

        var stuntRunner = await db.Rooms.SingleAsync(r => r.Name == "StuntRunner");
        var stuntRunnerScenes = await db.RoomScenes
            .Where(s => s.RoomId == stuntRunner.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        Assert.Collection(
            stuntRunnerScenes,
            scene =>
            {
                Assert.Equal(0, scene.OrderIndex);
                Assert.Equal("StuntRunner", scene.Name);
                Assert.Equal("b7281665-a715-4051-826b-8e08e69c6172", scene.RoomSceneLocationId);
                Assert.Equal("", scene.DataBlobName);
            },
            scene =>
            {
                Assert.Equal(1, scene.OrderIndex);
                Assert.Equal("TheMainEvent", scene.Name);
                Assert.Equal("3a636bd2-f896-424c-9225-c184522c0d87", scene.RoomSceneLocationId);
                Assert.Equal("", scene.DataBlobName);
            });

        var recRally = await db.Rooms.SingleAsync(r => r.Name == "RecRally");
        Assert.Equal("image_RecRally.jpg", recRally.ImageName);
    }
}
