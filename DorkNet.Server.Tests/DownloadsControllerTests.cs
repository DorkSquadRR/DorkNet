using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DorkNet.Server.Controllers.Site;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DorkNet.Server.Tests;

/// <summary>The site's downloads proxy: off without config, serves only files
/// the manifest lists, and passes Range through so the browser Quest installer
/// can read an .xapk in place.</summary>
public sealed class DownloadsControllerTests
{
    private const string Manifest = """
        {"releases":[{"id":"2023.03","title":"March 2023","files":[
          {"id":"quest","platform":"quest","name":"DorkNet-2023.03-Quest.xapk","path":"2023.03/DorkNet-2023.03-Quest.xapk","size":10,"sha256":"x"}]}]}
        """;

    [Fact]
    public async Task Without_a_base_url_the_manifest_is_404()
    {
        var (controller, _) = Create(baseUrl: null, _ => throw new InvalidOperationException("no request expected"));

        Assert.IsType<NotFoundObjectResult>(await controller.GetManifest(CancellationToken.None));
    }

    [Fact]
    public void Only_listed_paths_are_found()
    {
        var manifest = (JsonObject)JsonNode.Parse(Manifest)!;

        Assert.NotNull(DownloadsService.FindFile(manifest, "2023.03/DorkNet-2023.03-Quest.xapk"));
        Assert.Null(DownloadsService.FindFile(manifest, "manifest.json"));
        Assert.Null(DownloadsService.FindFile(manifest, "2023.03/../manifest.json"));
    }

    [Fact]
    public async Task Unlisted_file_is_404_and_never_fetched()
    {
        var requested = new List<string>();
        var (controller, ctx) = Create("http://bucket.internal:3902", req =>
        {
            requested.Add(req.RequestUri!.AbsolutePath);
            return Json(Manifest);
        });

        await controller.GetFile("index.html", CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Equal(["/manifest.json"], requested);
    }

    [Fact]
    public async Task Range_is_forwarded_and_the_partial_response_streamed()
    {
        HttpRequestMessage? fileRequest = null;
        var (controller, ctx) = Create("http://bucket.internal:3902/", req =>
        {
            if (req.RequestUri!.AbsolutePath == "/manifest.json") return Json(Manifest);
            fileRequest = req;
            var res = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("abcd"u8.ToArray()) };
            res.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 5, 10);
            return res;
        }, host: "downloads.example.com");
        ctx.Request.Headers.Range = "bytes=2-5";

        await controller.GetFile("2023.03/DorkNet-2023.03-Quest.xapk", CancellationToken.None);

        Assert.NotNull(fileRequest);
        Assert.Equal("/2023.03/DorkNet-2023.03-Quest.xapk", fileRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("bytes=2-5", fileRequest.Headers.Range!.ToString());
        Assert.Equal("downloads.example.com", fileRequest.Headers.Host);
        Assert.Equal(StatusCodes.Status206PartialContent, ctx.Response.StatusCode);
        Assert.Equal("bytes 2-5/10", ctx.Response.Headers.ContentRange.ToString());
        Assert.Equal("bytes", ctx.Response.Headers.AcceptRanges.ToString());
        Assert.Equal("abcd", Encoding.ASCII.GetString(((MemoryStream)ctx.Response.Body).ToArray()));
    }

    private static (DownloadsController, HttpContext) Create(
        string? baseUrl, Func<HttpRequestMessage, HttpResponseMessage> handler, string? host = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DorkNet:Downloads:BaseUrl"] = baseUrl,
            ["DorkNet:Downloads:Host"] = host,
        }).Build();
        var service = new DownloadsService(config, new StubFactory(handler), NullLogger<DownloadsService>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var controller = new DownloadsController(service) { ControllerContext = new ControllerContext { HttpContext = ctx } };
        return (controller, ctx);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(handler));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(handler(request));
    }
}
