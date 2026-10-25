using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Site;

/// <summary>
/// The client builds CI publishes, for the site's Downloads page and the
/// Quest page's "latest build" install. Files are streamed from the downloads
/// bucket (<see cref="DownloadsService"/>) with Range support, so the browser
/// installer can read an .xapk's zip directory and entries without fetching
/// the whole file. Only files the manifest lists are served.
///
/// The builds contain Rec Room's own game files: put the site behind an
/// access proxy (e.g. Cloudflare Access) before turning this on.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/site/v1/downloads")]
public class DownloadsController(DownloadsService downloads) : ControllerBase
{
    /// <summary>GET /api/site/v1/downloads — the manifest as published
    /// (snake_case keys), or 404 when downloads aren't set up.</summary>
    [HttpGet]
    public async Task<IActionResult> GetManifest(CancellationToken ct)
    {
        if (!downloads.IsConfigured) return NotFound(new { error = "Downloads aren't set up on this server." });
        var manifest = await downloads.GetManifestAsync(ct);
        Response.Headers.CacheControl = "no-cache";
        return Content((manifest ?? new System.Text.Json.Nodes.JsonObject { ["releases"] = new System.Text.Json.Nodes.JsonArray() })
            .ToJsonString(), "application/json");
    }

    /// <summary>GET /api/site/v1/downloads/files/{path} — one listed file,
    /// streamed; honours Range (206).</summary>
    [HttpGet("files/{**path}")]
    public async Task GetFile(string path, CancellationToken ct)
    {
        if (!downloads.IsConfigured) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var manifest = await downloads.GetManifestAsync(ct);
        var entry = manifest is null ? null : DownloadsService.FindFile(manifest, path);
        if (entry is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }

        using var upstream = await downloads.GetFileAsync(path, Request.Headers.Range.ToString(), ct);
        Response.StatusCode = (int)upstream.StatusCode;
        if (!upstream.IsSuccessStatusCode) return;

        var content = upstream.Content.Headers;
        if (content.ContentLength is long length) Response.ContentLength = length;
        if (content.ContentRange is not null) Response.Headers.ContentRange = content.ContentRange.ToString();
        Response.Headers.AcceptRanges = "bytes";
        if (upstream.Headers.ETag is not null) Response.Headers.ETag = upstream.Headers.ETag.ToString();
        Response.Headers.CacheControl = "private, no-cache";
        Response.ContentType = "application/octet-stream";
        var name = (string?)entry["name"] ?? Path.GetFileName(path);
        Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { FileName = name }.ToString();

        await using var body = await upstream.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(Response.Body, 1 << 16, ct);
    }
}
