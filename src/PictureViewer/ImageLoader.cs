using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureViewer;

/// <summary>Decodes a Picture into a frozen <see cref="BitmapSource"/> that is safe to hand to the UI thread.</summary>
public static class ImageLoader
{
    /// <summary>Long side, in pixels, that a PDF page is drawn at: sharp enough to zoom into, not huge.</summary>
    const int PdfLongSide = 2400;

    /// <summary>
    /// Loads a Picture. An animated GIF comes back with its frames too (the Picture is then the first composed frame).
    /// </summary>
    public static (BitmapSource Picture, GifFrames? Animation) LoadWithAnimation(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (GifAnimation.Load(path) is { } animation) return (animation.Frames[0], animation);
            }
            catch (Exception)
            {
                // Not decodable as an animation: show it as a still below.
            }
        }
        return (Load(path), null);
    }

    public static BitmapSource Load(string path)
    {
        if (ImageFormats.IsPdf(path))
            return LoadPdf(path, PdfLongSide);

        if (ImageFormats.NeedsMagick(path))
            return LoadWithMagick(path);

        try
        {
            return LoadNative(path);
        }
        catch (Exception)
        {
            // Codec missing or format unsupported by WIC: try Magick.NET.
            return LoadWithMagick(path);
        }
    }

    static BitmapSource LoadNative(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];

        var oriented = ApplyOrientation(frame, ReadOrientation(frame));
        oriented.Freeze();
        return oriented;
    }

    static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.GetQuery("System.Photo.Orientation") is ushort o)
                return o;
        }
        catch (Exception)
        {
            // Formats without metadata throw; treat as upright.
        }
        return 1;
    }

    /// <summary>Applies an EXIF orientation (1-8) so the Picture displays upright.</summary>
    static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? t = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),
            6 => new RotateTransform(90),
            7 => Group(new RotateTransform(270), new ScaleTransform(-1, 1)),
            8 => new RotateTransform(270),
            _ => null,
        };
        return t is null ? source : new TransformedBitmap(source, t);

        static TransformGroup Group(params Transform[] transforms)
        {
            var g = new TransformGroup();
            foreach (var x in transforms) g.Children.Add(x);
            return g;
        }
    }

    /// <summary>Decodes a small preview (longest side at most <paramref name="max"/> pixels), upright.</summary>
    public static BitmapSource LoadThumbnail(string path, int max)
    {
        if (ImageFormats.IsPdf(path))
            return LoadPdf(path, max);

        if (!ImageFormats.NeedsMagick(path))
        {
            try
            {
                return LoadNativeThumbnail(path, max);
            }
            catch (Exception)
            {
                // Fall through to Magick.NET.
            }
        }

        using var image = new MagickImage(path);
        image.AutoOrient();
        image.Thumbnail((uint)max, (uint)max);
        return ToBitmapSource(image);
    }

    static BitmapSource LoadNativeThumbnail(string path, int max)
    {
        int width, height, orientation;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            orientation = ReadOrientation(frame);
        }

        // Decode straight to thumbnail size; setting only the longer side keeps the aspect ratio.
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // a file we just saved must not show its old thumbnail
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(max, width);
        else bitmap.DecodePixelHeight = Math.Min(max, height);
        bitmap.EndInit();

        var oriented = ApplyOrientation(bitmap, orientation);
        oriented.Freeze();
        return oriented;
    }

    /// <summary>Draws the first page of a PDF on white, using the PDF renderer that ships with Windows.</summary>
    static BitmapSource LoadPdf(string path, int longSide)
    {
        // Runs on a worker thread, so waiting on the WinRT calls is fine.
        var file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
        var document = PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        using var page = document.GetPage(0);

        double scale = longSide / Math.Max(page.Size.Width, page.Size.Height);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };

        using var stream = new InMemoryRandomAccessStream();
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
        BitmapSource frame = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

        // The renderer doesn't always honour the size it was asked for (here it came out 1.4x larger), so settle it.
        double actual = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (actual > longSide * 1.02)
        {
            double k = longSide / actual;
            var scaled = new WriteableBitmap(new TransformedBitmap(frame, new ScaleTransform(k, k)));
            scaled.Freeze();
            return scaled;
        }
        frame.Freeze();
        return frame;
    }

    static BitmapSource LoadWithMagick(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        return ToBitmapSource(image);
    }

    static BitmapSource ToBitmapSource(MagickImage image)
    {
        int w = (int)image.Width, h = (int)image.Height;
        bool alpha = image.HasAlpha;
        using var pixels = image.GetPixels();
        byte[] data = pixels.ToByteArray(alpha ? PixelMapping.BGRA : PixelMapping.BGR)
                      ?? throw new InvalidDataException("Could not read pixels.");

        int channels = alpha ? 4 : 3;
        var format = alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
        var bitmap = BitmapSource.Create(w, h, 96, 96, format, null, data, w * channels);
        bitmap.Freeze();
        return bitmap;
    }
}
