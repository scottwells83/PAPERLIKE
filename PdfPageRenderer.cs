using SkiaSharp;

namespace PaperwhiteReader;

public static class PdfPageRenderer
{
    public static byte[] Render(string path, int page, SKColor paper, SKColor ink)
    {
        using var file = File.OpenRead(path);
        using var image = PDFtoImage.Conversion.ToImage(file, page: page);
        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var high = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
            var low = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
            if (high - low > 35) continue;
            var luminance = (pixel.Red * 0.2126 + pixel.Green * 0.7152 + pixel.Blue * 0.0722) / 255.0;
            byte Mix(byte text, byte background) => (byte)Math.Clamp(
                Math.Round(text * (1 - luminance) + background * luminance), 0, 255);
            pixels[i] = new SKColor(Mix(ink.Red, paper.Red), Mix(ink.Green, paper.Green),
                Mix(ink.Blue, paper.Blue), pixel.Alpha);
        }
        image.Pixels = pixels;
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
}
