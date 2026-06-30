using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Tests;

public sealed class DorkNetServerFactory : WebApplicationFactory<Program>
{
    private const string JwtSecret = "endpoint-contract-secret-000000000000000000000000000000000000000000";
    private readonly string _dataRoot;

    public DorkNetServerFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "dorknet-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
    }

    public string ApexDomain => "dork.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Domain:Apex", ApexDomain);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["Database:Provider"] = "sqlite",
                ["Database:SqlitePath"] = Path.Combine(_dataRoot, "dorknet-test.db"),
                ["Domain:Apex"] = ApexDomain,
                ["Jwt:Secret"] = JwtSecret,
                ["S3:Endpoint"] = "",
                ["S3:AccessKey"] = "",
                ["S3:SecretKey"] = "",
                ["DorkNet:DefaultClientVersion"] = "december_2020_12_18",
                ["DorkNet:SupportedVersions:0"] = "december_2020_12_18",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<HostFilteringOptions>(options =>
            {
                options.AllowedHosts =
                [
                    ApexDomain,
                    $"*.{ApexDomain}",
                    "localhost",
                    "127.0.0.1",
                ];
                options.AllowEmptyHosts = true;
                options.IncludeFailureMessage = true;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch
        {
            // Test cleanup must not hide the original test failure.
        }
    }
}
