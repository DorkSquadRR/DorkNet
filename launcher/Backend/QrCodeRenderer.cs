using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace DorkNet.Launcher.Backend;

/// <summary>Turns a join-code string into a WPF <see cref="BitmapSource"/>
/// using QRCoder's raw module matrix. We render the matrix directly to a
/// <see cref="WriteableBitmap"/> rather than going through PNG bytes so we
/// avoid pulling in System.Drawing.Common (Windows-only, slated for
/// trimming in cross-platform .NET).
///
/// <para>Colour choice matches the launcher palette: white (Ink) modules
/// on the deep purple-navy (BgDeeper) background so the QR is legible
/// against the dark panel.</para></summary>
public static class QrCodeRenderer
{
    public static BitmapSource Render(string text, int pixelSize = 220)
    {
        // QRCodeGenerator -> QRCodeData (matrix of bools) -> WriteableBitmap.
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix;
        var moduleCount = modules.Count;
        if (moduleCount == 0)
            throw new InvalidOperationException("QR generation produced an empty matrix.");

        // Scale up to the requested pixel size — int division so each
        // module is an exact pixel count (avoids subpixel blur). Quiet
        // zone (4-module margin) is part of QRCodeData already.
        var moduleSize = Math.Max(1, pixelSize / moduleCount);
        var dim = moduleCount * moduleSize;

        var bmp = new WriteableBitmap(dim, dim, 96, 96, PixelFormats.Bgra32, null);
        var stride = dim * 4;
        var pixels = new byte[dim * stride];

        // Light pixel = Ink (#F7F3FF), dark pixel = BgDeeper (#0E0A1F).
        // Order is BGRA in the buffer.
        var light = new byte[] { 0xFF, 0xF3, 0xF7, 0xFF };
        var dark = new byte[] { 0x1F, 0x0A, 0x0E, 0xFF };

        for (int y = 0; y < moduleCount; y++)
        for (int x = 0; x < moduleCount; x++)
        {
            var on = modules[y][x];
            var src = on ? dark : light;
            for (int py = 0; py < moduleSize; py++)
            for (int px = 0; px < moduleSize; px++)
            {
                var offset = ((y * moduleSize + py) * stride) + ((x * moduleSize + px) * 4);
                Buffer.BlockCopy(src, 0, pixels, offset, 4);
            }
        }

        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, dim, dim), pixels, stride, 0);
        bmp.Freeze();
        return bmp;
    }
}
