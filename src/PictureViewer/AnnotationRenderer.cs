using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>Draws annotations, both live on screen and when flattening into a Picture's pixels.</summary>
public static class AnnotationRenderer
{
    sealed record MosaicKey(BitmapSource Base, Rect Region, double Block);

    sealed class MosaicEntry(MosaicKey key, BitmapSource tiny)
    {
        public MosaicKey Key { get; } = key;
        public BitmapSource Tiny { get; } = tiny;
    }

    static readonly ConditionalWeakTable<Annotation, MosaicEntry> MosaicCache = new();

    /// <summary>Draws one annotation in Picture pixel coordinates. <paramref name="baseImage"/> feeds the Mosaic.</summary>
    public static void Draw(DrawingContext dc, Annotation a, BitmapSource? baseImage)
    {
        if (a.Tool == AnnotationTool.Mosaic)
        {
            if (baseImage != null) DrawMosaic(dc, a, baseImage);
            return;
        }

        bool rotated = a.Angle != 0;
        if (rotated)
        {
            var c = AnnotationGeometry.Center(a);
            dc.PushTransform(new RotateTransform(a.Angle * 180 / Math.PI, c.X, c.Y));
        }

        switch (a.Tool)
        {
            case AnnotationTool.Image:
                if (a.Image != null && a.Points.Count == 2)
                    dc.DrawImage(a.Image, new Rect(a.Points[0], a.Points[1]));
                break;
            case AnnotationTool.Text:
                DrawText(dc, a);
                break;
            default:
                DrawStroked(dc, a);
                break;
        }

        if (rotated) dc.Pop();
    }

    static void DrawText(DrawingContext dc, Annotation a)
    {
        if (a.Points.Count == 0 || a.Text.Length == 0) return;
        var rect = AnnotationGeometry.TextRect(a);
        double pad = AnnotationGeometry.TextPadding(a);
        if (a.Background is { } bg)
        {
            var brush = new SolidColorBrush(bg);
            brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, rect, pad, pad);
        }
        dc.DrawText(AnnotationGeometry.MakeText(a), new Point(rect.X + pad, rect.Y + pad));
    }

    static void DrawStroked(DrawingContext dc, Annotation a)
    {
        if (a.Points.Count < 2) return;
        var brush = new SolidColorBrush(a.Color);
        brush.Freeze();
        var pen = new Pen(brush, a.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        Point p = a.Points[0], q = a.Points[^1];
        switch (a.Tool)
        {
            case AnnotationTool.Line:
                dc.DrawLine(pen, p, q);
                break;
            case AnnotationTool.Arrow:
                dc.DrawLine(pen, p, q);
                foreach (var barb in AnnotationGeometry.ArrowHead(a)) dc.DrawLine(pen, q, barb);
                break;
            case AnnotationTool.Rect:
                dc.DrawRectangle(null, pen, new Rect(p, q));
                break;
            case AnnotationTool.Ellipse:
            {
                var box = new Rect(p, q);
                dc.DrawEllipse(null, pen, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2);
                break;
            }
            case AnnotationTool.Pen:
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(a.Points[0], false, false);
                    g.PolyLineTo(a.Points.Skip(1).ToList(), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
                break;
            }
        }
    }

    // ---- Mosaic ----

    static void DrawMosaic(DrawingContext dc, Annotation a, BitmapSource baseImage)
    {
        if (a.Points.Count != 2) return;
        var region = Rect.Intersect(new Rect(a.Points[0], a.Points[1]), new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
        if (region.IsEmpty || region.Width < 1 || region.Height < 1) return;

        double block = Math.Max(2, a.Width * 3);
        var key = new MosaicKey(baseImage, region, block);
        if (!MosaicCache.TryGetValue(a, out var entry) || entry.Key != key)
        {
            entry = new MosaicEntry(key, Pixelate(baseImage, region, block));
            MosaicCache.AddOrUpdate(a, entry);
        }

        // Scale the tiny picture back up without smoothing so the blocks stay crisp.
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        using (var gdc = group.Open())
            gdc.DrawImage(entry.Tiny, region);
        dc.DrawDrawing(group);
    }

    /// <summary>Averages the region into blocks of about <paramref name="block"/> pixels; one pixel per block.</summary>
    static BitmapSource Pixelate(BitmapSource source, Rect region, double block)
    {
        int x = (int)Math.Floor(region.X), y = (int)Math.Floor(region.Y);
        int w = Math.Min(source.PixelWidth - x, (int)Math.Ceiling(region.Right) - x);
        int h = Math.Min(source.PixelHeight - y, (int)Math.Ceiling(region.Bottom) - y);

        var crop = new FormatConvertedBitmap(new CroppedBitmap(source, new Int32Rect(x, y, w, h)), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[w * h * 4];
        crop.CopyPixels(pixels, w * 4, 0);

        int sw = Math.Max(1, (int)Math.Ceiling(region.Width / block));
        int sh = Math.Max(1, (int)Math.Ceiling(region.Height / block));
        var tiny = new byte[sw * sh * 4];

        for (int j = 0; j < sh; j++)
        {
            int y0 = j * h / sh, y1 = Math.Max(y0 + 1, (j + 1) * h / sh);
            for (int i = 0; i < sw; i++)
            {
                int x0 = i * w / sw, x1 = Math.Max(x0 + 1, (i + 1) * w / sw);
                long b = 0, g = 0, r = 0, al = 0;
                int count = 0;
                for (int yy = y0; yy < y1 && yy < h; yy++)
                {
                    for (int xx = x0; xx < x1 && xx < w; xx++)
                    {
                        int o = (yy * w + xx) * 4;
                        b += pixels[o]; g += pixels[o + 1]; r += pixels[o + 2]; al += pixels[o + 3];
                        count++;
                    }
                }
                if (count == 0) continue;
                int t = (j * sw + i) * 4;
                tiny[t] = (byte)(b / count); tiny[t + 1] = (byte)(g / count);
                tiny[t + 2] = (byte)(r / count); tiny[t + 3] = (byte)(al / count);
            }
        }

        var result = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, tiny, sw * 4);
        result.Freeze();
        return result;
    }

    // ---- Flatten ----

    /// <summary>
    /// Burns the annotations and the View rotation (clockwise quarter turns) into the pixels of a new Picture.
    /// </summary>
    public static BitmapSource Flatten(BitmapSource image, IReadOnlyList<Annotation> annotations, int quarterTurns)
    {
        int turns = (quarterTurns % 4 + 4) % 4;
        if (turns == 0 && annotations.Count == 0) return image;

        int w = image.PixelWidth, h = image.PixelHeight;
        int outW = turns % 2 == 1 ? h : w, outH = turns % 2 == 1 ? w : h;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            if (turns != 0)
            {
                var m = Matrix.Identity;
                m.Rotate(90 * turns);
                m.Translate(turns == 1 ? h : turns == 2 ? w : 0, turns == 2 ? h : turns == 3 ? w : 0);
                dc.PushTransform(new MatrixTransform(m));
            }
            dc.DrawImage(image, new Rect(0, 0, w, h));
            foreach (var a in annotations) Draw(dc, a, image);
            if (turns != 0) dc.Pop();
        }

        var target = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
