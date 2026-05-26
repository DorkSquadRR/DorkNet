using SkiaSharp;

namespace DorkNet.Server.Services;

/// <summary>
/// img.* CDN transforms. The 2020 watch posts a full-frame capture
/// (e.g. the avatar standing in their dorm) and then re-requests the
/// same blob with <c>?cropSquare=1&amp;width=256</c> on the profile
/// thumbnail surface. We honour <c>cropSquare</c> server-side so the
/// profile pic is face-zoomed, but we deliberately ignore the watch's
/// <c>width=256</c> hint and return the cropped image at the source's
/// native resolution: the watch's UI renders the same blob across
/// multiple sizes (small thumb chip and the big profile frame), and a
/// pre-shrunk 256² source gets stretched across a ~500px UI element and
/// looks blurry. Keeping the source resolution lets the watch's image
/// pipeline downsample for display without losing detail.
/// </summary>
public static class ImageTransformService
{
    public readonly record struct Result(byte[] Bytes, string ContentType);

    public static Result? TryTransform(
        byte[] sourceBytes,
        string sourceContentType,
        bool cropSquare,
        int? widthHint)
    {
        // widthHint is intentionally unused — see class comment. Kept on the
        // signature so the controller can pass the parsed value without the
        // call site having to know which params we currently respect.
        _ = widthHint;
        if (!cropSquare) return null;
        if (sourceBytes.Length == 0) return null;

        using var input = SKBitmap.Decode(sourceBytes);
        if (input is null) return null;
        if (input.Width == input.Height) return null; // already square, pass through

        using var cropped = CenterCropSquare(input);
        using var image = SKImage.FromBitmap(cropped);

        // Re-encode in the same family the source advertised, so an uploaded
        // JPEG stays JPEG (size budget for profile pic round-trips) and
        // anything PNG-shaped stays PNG (alpha preserved for UI overlays).
        var (format, contentType, quality) = sourceContentType switch
        {
            "image/jpeg" or "image/jpg" => (SKEncodedImageFormat.Jpeg, "image/jpeg", 92),
            "image/webp"               => (SKEncodedImageFormat.Webp, "image/webp", 92),
            _                          => (SKEncodedImageFormat.Png,  "image/png",  100),
        };
        using var encoded = image.Encode(format, quality);
        return new Result(encoded.ToArray(), contentType);
    }

    private static SKBitmap CenterCropSquare(SKBitmap input)
    {
        var side = Math.Min(input.Width, input.Height);
        var x = (input.Width - side) / 2;
        var y = (input.Height - side) / 2;
        var dst = new SKBitmap(new SKImageInfo(side, side, input.ColorType, input.AlphaType));
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(input, new SKRect(x, y, x + side, y + side), new SKRect(0, 0, side, side));
        return dst;
    }
}
