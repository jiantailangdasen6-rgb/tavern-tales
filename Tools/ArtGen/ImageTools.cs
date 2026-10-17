using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>Resizing and JPEG export for imported art (Windows only, like the game).</summary>
static class ImageTools
{
    /// <summary>Scales to cover width x height (centre crop) and saves a quality-90 JPEG.</summary>
    public static void SaveJpeg(string srcPng, string destJpg, int width, int height)
    {
        using var src = Image.FromFile(srcPng);
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var scale = Math.Max((float)width / src.Width, (float)height / src.Height);
            var w = src.Width * scale; var h = src.Height * scale;
            g.DrawImage(src, (width - w) / 2, (height - h) / 2, w, h);
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        bmp.Save(destJpg, codec, ps);
    }
}
