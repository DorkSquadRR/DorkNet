using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DorkNet.Server.Controllers.API.Images.V2;

[ApiController]
[Route("api/[controller]/v2")]
[Authorize]
public class ImagesController : ControllerBase
{
    private static readonly string ImageDir =
        Path.Combine(AppContext.BaseDirectory, "data", "images");

    [HttpPost("upload")]
    public async Task<ActionResult<UploadImageResponse>> Upload(IFormFile file)
    {
        Directory.CreateDirectory(ImageDir);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
            return BadRequest("Only PNG/JPG accepted");

        var name = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(ImageDir, name);

        await using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new UploadImageResponse { ImageName = name, Url = $"{baseUrl}/img/{name}" });
    }
}

public class UploadImageResponse
{
    public string ImageName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
