using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PictureViewer;

/// <summary>Layout, bounds and hit-testing maths for annotations (Picture pixels, y down, angles clockwise).</summary>
public static class AnnotationGeometry
{
    const double HeadSpread = Math.PI * 5 / 6;

    /// <summary>Stroke width / font size multiplier so marks look similar on small and huge pictures.</summary>
    public static double StrokeScale(int pixelWidth, int pixelHeight) =>
        Math.Max(1, Math.Max(pixelWidth, pixelHeight) / 800.0);

    public static Point Rotate(Point p, Point about, double angle)
    {
        double dx = p.X - about.X, dy = p.Y - about.Y;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        return new Point(about.X + dx * cos - dy * sin, about.Y + dx * sin + dy * cos);
    }

    public static Rect Bounds(Annotation a)
    {
        if (a.Tool == AnnotationTool.Text) return TextRect(a);
        if (a.Points.Count == 0) return Rect.Empty;
        double x0 = a.Points.Min(p => p.X), x1 = a.Points.Max(p => p.X);
        double y0 = a.Points.Min(p => p.Y), y1 = a.Points.Max(p => p.Y);
        var r = new Rect(x0, y0, x1 - x0, y1 - y0);
        r.Inflate(a.Width / 2, a.Width / 2);
        return r;
    }

    public static Point Center(Annotation a)
    {
        var r = Bounds(a);
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    // ---- Text ----

    public static FormattedText MakeText(Annotation a)
    {
        var typeface = new Typeface(
            new FontFamily(string.IsNullOrEmpty(a.FontName) ? "Segoe UI" : a.FontName),
            a.Italic ? FontStyles.Italic : FontStyles.Normal,
            a.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var brush = new SolidColorBrush(a.Color);
        brush.Freeze();
        var text = new FormattedText(a.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, Math.Max(1, a.FontSize), brush, 1.0);
        if (a.BoxWidth is { } w) text.MaxTextWidth = Math.Max(1, w);
        return text;
    }

    public static double TextPadding(Annotation a) => a.FontSize * 0.25;

    /// <summary>The text box (including padding); the anchor point is its top-left corner.</summary>
    public static Rect TextRect(Annotation a)
    {
        if (a.Points.Count == 0) return Rect.Empty;
        var text = MakeText(a);
        double pad = TextPadding(a);
        double w = a.BoxWidth ?? Math.Ceiling(text.Width) + 2;
        double h = Math.Ceiling(text.Height);
        return new Rect(a.Points[0].X, a.Points[0].Y, w + 2 * pad, h + 2 * pad);
    }

    /// <summary>Wrap-width handle (right edge, vertically centered) with rotation applied.</summary>
    public static Point WidthHandle(Annotation a)
    {
        var r = TextRect(a);
        return Rotate(new Point(r.Right, r.Y + r.Height / 2), Center(a), a.Angle);
    }

    /// <summary>Sets the wrap width from a mouse position, keeping the box's top-left corner fixed in the Picture.</summary>
    public static void SetWrapWidth(Annotation a, Point mouse)
    {
        if (a.Points.Count == 0) return;
        var top = Rotate(a.Points[0], Center(a), a.Angle);
        double along = (mouse.X - top.X) * Math.Cos(a.Angle) + (mouse.Y - top.Y) * Math.Sin(a.Angle);
        a.BoxWidth = Math.Max(along - 2 * TextPadding(a), a.FontSize);

        var r = TextRect(a); // size under the new width, still at the old anchor
        var d = new Vector(r.Width / 2, r.Height / 2); // top-left to center
        var rd = Rotate(new Point(d.X, d.Y), new Point(0, 0), a.Angle);
        a.Points[0] = new Point(top.X - d.X + rd.X, top.Y - d.Y + rd.Y);
    }

    // ---- Outlines and hit testing ----

    /// <summary>Straight pieces that make up the outline of a stroked shape (before rotation).</summary>
    public static IEnumerable<(Point A, Point B)> Segments(Annotation a)
    {
        if (a.Points.Count == 0) yield break;
        Point p = a.Points[0], q = a.Points[^1];
        switch (a.Tool)
        {
            case AnnotationTool.Line:
                yield return (p, q);
                break;
            case AnnotationTool.Arrow:
                yield return (p, q);
                foreach (var head in ArrowHead(a)) yield return (q, head);
                break;
            case AnnotationTool.Rect:
            {
                var box = new Rect(p, q);
                var tl = box.TopLeft; var tr = box.TopRight; var br = box.BottomRight; var bl = box.BottomLeft;
                yield return (tl, tr); yield return (tr, br); yield return (br, bl); yield return (bl, tl);
                break;
            }
            case AnnotationTool.Ellipse:
            {
                var box = new Rect(p, q);
                const int steps = 64;
                Point Pt(int i)
                {
                    double t = 2 * Math.PI * i / steps;
                    return new Point(box.X + box.Width / 2 * (1 + Math.Cos(t)), box.Y + box.Height / 2 * (1 + Math.Sin(t)));
                }
                for (int i = 0; i < steps; i++) yield return (Pt(i), Pt(i + 1));
                break;
            }
            case AnnotationTool.Pen:
                for (int i = 1; i < a.Points.Count; i++) yield return (a.Points[i - 1], a.Points[i]);
                break;
        }
    }

    /// <summary>The two barbs of an arrow head, as end points around the tip.</summary>
    public static IEnumerable<Point> ArrowHead(Annotation a)
    {
        Point from = a.Points[0], tip = a.Points[^1];
        double angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
        double len = a.Width * 4 + 6;
        foreach (double d in new[] { HeadSpread, -HeadSpread })
            yield return new Point(tip.X + len * Math.Cos(angle + d), tip.Y + len * Math.Sin(angle + d));
    }

    /// <summary>Smallest distance (Picture pixels) from <paramref name="point"/> to the annotation; infinity if it misses.</summary>
    public static double Distance(Point point, Annotation a)
    {
        var center = Center(a);
        switch (a.Tool)
        {
            case AnnotationTool.Image:
                return Bounds(a).Contains(Rotate(point, center, -a.Angle)) ? 0 : double.PositiveInfinity;
            case AnnotationTool.Mosaic:
                return Bounds(a).Contains(point) ? 0 : double.PositiveInfinity;
        }

        var p = a.Angle == 0 ? point : Rotate(point, center, -a.Angle);
        if (a.Tool == AnnotationTool.Text)
            return TextRect(a).Contains(p) ? 0 : double.PositiveInfinity;

        double best = double.PositiveInfinity;
        foreach (var (s, e) in Segments(a))
            best = Math.Min(best, SegmentDistance(p, s, e));
        return best;
    }

    public static double Length(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    static double SegmentDistance(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Length(p.X - (a.X + t * dx), p.Y - (a.Y + t * dy));
    }
}
