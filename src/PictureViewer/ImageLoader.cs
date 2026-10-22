using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace PictureViewer;

/// <summary>Decodes a Picture into a frozen <see cref="BitmapSource"/> that is safe to hand to the UI thread.</summary>
public static class ImageLoader
{
    public static BitmapSource Load(string path)
    {
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

    static BitmapSource LoadWithMagick(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();

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
