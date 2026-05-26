using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers;

[ApiController]
public class RootController(ConfigService configService) : ControllerBase
{
    // Legacy MOTD for pre-Dorm Room builds
    [HttpGet("motd")]
    public ActionResult<string> GetMotd()
    {
        var cfg = configService.GetConfig($"{Request.Scheme}://{Request.Host}");
        return Ok(cfg.MessageOfTheDay);
    }

    // Serve images from data/images/
    [HttpGet("img/{name}")]
    public ActionResult GetImage(string name)
    {
        var safeFileName = Path.GetFileName(name); // prevent path traversal
        var path = Path.Combine(AppContext.BaseDirectory, "data", "images", safeFileName);

        if (!System.IO.File.Exists(path))
            return NotFound();

        var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = ext == ".png" ? "image/png" : "image/jpeg";
        return PhysicalFile(path, contentType);
    }
}
