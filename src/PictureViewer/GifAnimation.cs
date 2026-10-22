using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>The fully composed frames of an animated GIF and how long each one shows.</summary>
public sealed record GifFrames(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays);

public static class GifAnimation
{
    // Frames are kept in memory as full pictures, so very large animations stay as a still.
    const long MaxBytes = 400L * 1024 * 1024;

    /// <summary>Returns the composed frames, or null if the file has a single frame (or is too big to animate).</summary>
    public static GifFrames? Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count < 2) return null;

        int width = Query<ushort>(decoder.Metadata, "/logscrdesc/Width", (ushort)decoder.Frames[0].PixelWidth);
        int height = Query<ushort>(decoder.Metadata, "/logscrdesc/Height", (ushort)decoder.Frames[0].PixelHeight);
        if ((long)width * height * 4 * decoder.Frames.Count > MaxBytes) return null;

        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        BitmapSource? canvas = null; // what the screen shows after the previous frame was disposed

        foreach (var frame in decoder.Frames)
        {
            var meta = frame.Metadata;
            int left = Query<ushort>(meta, "/imgdesc/Left", 0), top = Query<ushort>(meta, "/imgdesc/Top", 0);
            var area = new Rect(left, top, frame.PixelWidth, frame.PixelHeight);
            int disposal = Query<byte>(meta, "/grctlext/Disposal", 0);

            // Browsers treat delays under 20 ms as 100 ms.
            int centiseconds = Query<ushort>(meta, "/grctlext/Delay", 10);
            delays.Add(TimeSpan.FromMilliseconds(centiseconds < 2 ? 100 : centiseconds * 10));

            var shown = Compose(canvas, frame, area, width, height, clearArea: null);
            frames.Add(shown);

            canvas = disposal switch
            {
                2 => Compose(shown, null, area, width, height, clearArea: area), // restore to background
                3 => canvas,                                                      // restore to previous
                _ => shown,
            };
        }
        return new GifFrames(frames, delays);
    }

    static BitmapSource Compose(BitmapSource? below, BitmapSource? frame, Rect area, int width, int height, Rect? clearArea)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (below != null)
            {
                if (clearArea is { } hole)
                {
                    // Keep everything except the rectangle being cleared.
                    dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(0, 0, width, height)), new RectangleGeometry(hole)));
                    dc.DrawImage(below, new Rect(0, 0, width, height));
                    dc.Pop();
                }
                else
                {
                    dc.DrawImage(below, new Rect(0, 0, width, height));
                }
            }
            if (frame != null) dc.DrawImage(frame, area);
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    static T Query<T>(ImageMetadata? metadata, string query, T fallback)
    {
        try
        {
            if (metadata is BitmapMetadata m && m.GetQuery(query) is T value) return value;
        }
        catch (Exception)
        {
            // Missing block: use the default.
        }
        return fallback;
    }
}
