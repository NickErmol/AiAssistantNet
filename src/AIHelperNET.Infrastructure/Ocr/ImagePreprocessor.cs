using System.Drawing;
using System.Drawing.Imaging;

namespace AIHelperNET.Infrastructure.Ocr;

public static class ImagePreprocessor
{
    public static Bitmap Enhance(Bitmap source)
    {
        var scaled = new Bitmap(source.Width * 2, source.Height * 2);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
        }

        var gray = new Bitmap(scaled.Width, scaled.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(gray))
        {
            var cm = new ColorMatrix(new float[][]
            {
                [0.299f, 0.299f, 0.299f, 0, 0],
                [0.587f, 0.587f, 0.587f, 0, 0],
                [0.114f, 0.114f, 0.114f, 0, 0],
                [0,      0,      0,      1, 0],
                [0,      0,      0,      0, 1]
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(cm);
            g.DrawImage(scaled, new Rectangle(0, 0, gray.Width, gray.Height),
                0, 0, scaled.Width, scaled.Height, GraphicsUnit.Pixel, attrs);
        }

        scaled.Dispose();
        return gray;
    }
}
