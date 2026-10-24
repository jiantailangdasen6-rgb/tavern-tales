using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>Resizing and JPEG export for imported art (Windows only, like the game).</summary>
static class ImageTools
{
    /// <summary>
    /// Scales to cover width x height (centre crop) and saves a quality-90 JPEG. trimBottom cuts that share of the source's
    /// bottom first: FLUX often paints a signature-like scribble in the bottom corners.
    /// </summary>
    public static void SaveJpeg(string srcPng, string destJpg, int width, int height, float trimBottom = 0)
    {
        using var src = Image.FromFile(srcPng);
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float sw = src.Width, sh = src.Height * (1 - trimBottom);
            var scale = Math.Max(width / sw, height / sh);
            // the part of the source that covers the target: centred across, from the top down
            float cw = width / scale, ch = height / scale;
            var from = new RectangleF((sw - cw) / 2, (sh - ch) / 2, cw, ch);
            g.DrawImage(src, new RectangleF(0, 0, width, height), from, GraphicsUnit.Pixel);
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        bmp.Save(destJpg, codec, ps);
    }
}
