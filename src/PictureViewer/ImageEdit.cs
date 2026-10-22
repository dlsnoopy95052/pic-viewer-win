using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>Edits that change a Picture's pixels. Each returns a new frozen bitmap; the input is never modified.</summary>
public static class ImageEdit
{
    public static readonly HashSet<string> WritableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff",
    };

    public static BitmapSource Flip(BitmapSource source, bool horizontal) =>
        Materialize(new TransformedBitmap(source, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1)));

    public static BitmapSource Crop(BitmapSource source, Int32Rect area) =>
        Materialize(new CroppedBitmap(source, area));

    /// <summary>Copies a lazy bitmap chain into memory so later edits don't keep the old pixels alive.</summary>
    static BitmapSource Materialize(BitmapSource source)
    {
        var copy = new WriteableBitmap(source);
        copy.Freeze();
        return copy;
    }

    // ---- Encoding ----

    /// <summary>Writes <paramref name="image"/> next to its final name first, then swaps it in, so a failure never leaves a half-written file.</summary>
    public static void Save(BitmapSource image, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        // JPEG has no transparency: put the picture on white instead of letting transparent areas turn black.
        encoder.Frames.Add(BitmapFrame.Create(encoder is JpegBitmapEncoder ? OnWhite(image) : image));

        string temp = path + ".pvtmp";
        try
        {
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static BitmapSource OnWhite(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }
        var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}

/// <summary>
/// Brightness / contrast / saturation for one Picture. The pixels are read once, so dragging a slider only
/// pays for the per-pixel maths. Brightness is -0.5…0.5, contrast and saturation 0…2 (1 = unchanged).
/// </summary>
public sealed class AdjustSession
{
    readonly byte[] _pixels;
    readonly int _width, _height;
    readonly double _dpiX, _dpiY;

    public AdjustSession(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        _width = converted.PixelWidth;
        _height = converted.PixelHeight;
        _dpiX = source.DpiX;
        _dpiY = source.DpiY;
        _pixels = new byte[_width * _height * 4];
        converted.CopyPixels(_pixels, _width * 4, 0);
    }

    public BitmapSource Render(double brightness, double contrast, double saturation)
    {
        var output = new byte[_pixels.Length];
        int stride = _width * 4;
        float b = (float)brightness, c = (float)contrast, s = (float)saturation;

        Parallel.For(0, _height, y =>
        {
            int row = y * stride;
            for (int x = 0; x < _width; x++)
            {
                int i = row + x * 4;
                float blue = _pixels[i] / 255f, green = _pixels[i + 1] / 255f, red = _pixels[i + 2] / 255f;

                // Same order as Core Image's color controls: saturation, then brightness, then contrast.
                float luma = 0.2125f * red + 0.7154f * green + 0.0721f * blue;
                red = luma + s * (red - luma);
                green = luma + s * (green - luma);
                blue = luma + s * (blue - luma);

                output[i] = To8Bit((blue + b - 0.5f) * c + 0.5f);
                output[i + 1] = To8Bit((green + b - 0.5f) * c + 0.5f);
                output[i + 2] = To8Bit((red + b - 0.5f) * c + 0.5f);
                output[i + 3] = _pixels[i + 3];
            }
        });

        var result = BitmapSource.Create(_width, _height, _dpiX, _dpiY, PixelFormats.Bgra32, null, output, stride);
        result.Freeze();
        return result;
    }

    static byte To8Bit(float value) => (byte)Math.Clamp(value * 255f + 0.5f, 0f, 255f);
}
