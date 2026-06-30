using System.Text.Json;
using DorkNet.Server.Compat;
using DorkNet.Server.Controllers.Auth;
using DorkNet.Server.Versions.Late2020;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DorkNet.Server.Tests;

public sealed class VersionCheckControllerTests
{
    [Fact]
    public void March_2023_build_is_valid_with_minimal_service_config()
    {
        var controller = CreateController();

        var result = Assert.IsType<OkObjectResult>(controller.Check("20230321", "0"));

        Assert.Equal(0, ReadVersionStatus(result.Value));
    }

    [Fact]
    public void Unknown_build_still_requires_update()
    {
        var controller = CreateController();

        var result = Assert.IsType<OkObjectResult>(controller.Check("unknown", "0"));

        Assert.Equal(2, ReadVersionStatus(result.Value));
    }

    private static VersionCheckController CreateController()
    {
        var config = new ConfigurationBuilder().Build();
        var registry = new VersionRegistry(
            [new Late2020VersionPlugin()],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "march_2023_03_21",
            },
            "march_2023_03_21");

        return new VersionCheckController(
            config,
            registry,
            NullLogger<VersionCheckController>.Instance);
    }

    private static int ReadVersionStatus(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("VersionStatus").GetInt32();
    }
}
