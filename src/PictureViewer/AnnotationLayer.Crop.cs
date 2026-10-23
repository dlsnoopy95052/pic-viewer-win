using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PictureViewer;

// Crop mode: choose the area to keep, optionally locked to an aspect ratio. The area can be moved and
// resized by its eight handles after it is drawn.
public sealed partial class AnnotationLayer
{
    enum CropDrag { None, New, Move, Handle }

    const double MinCropSize = 4;

    bool _cropMode;
    Rect? _cropRect;
    CropDrag _cropDrag;
    int _cropHandle;        // 0…7 = top-left, top, top-right, right, bottom-right, bottom, bottom-left, left
    Point _cropAnchor;      // the fixed corner while drawing a new area or dragging a corner
    Vector _cropGrab;       // where inside the area the pointer picked it up, while moving
    double? _cropAspect;

    public event EventHandler? CropModeChanged;

    /// <summary>While on, dragging selects the area to keep instead of drawing or selecting annotations.</summary>
    public bool CropMode
    {
        get => _cropMode;
        set
        {
            if (_cropMode == value) return;
            _cropMode = value;
            _cropRect = null;
            _cropDrag = CropDrag.None;
            if (value)
            {
                Tool = AnnotationTool.None;
                SelectedIndex = null;
                if (_cropAspect is { } a) FitCropTo(a);
            }
            Cursor = value ? Cursors.Cross : (_tool == AnnotationTool.None ? null : Cursors.Cross);
            InvalidateVisual();
            CropModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Width ÷ height the crop area is locked to; null leaves it free. Changing it refits the area around its centre.</summary>
    public double? CropAspect
    {
        get => _cropAspect;
        set
        {
            if (_cropAspect == value) return;
            _cropAspect = value;
            if (value is { } a && _cropMode) FitCropTo(a);
            InvalidateVisual();
        }
    }

    /// <summary>The area chosen in crop mode, in whole Picture pixels; null until something usable is selected.</summary>
    public Int32Rect? CropPixelRect
    {
        get
        {
            if (_cropRect is not { } r || _base is null) return null;
            int bw = _base.PixelWidth, bh = _base.PixelHeight;
            int x, y, w, h;
            if (_cropAspect is { } a)
            {
                // Keep the ratio exact to the pixel instead of rounding each edge outwards.
                w = Math.Min(bw, (int)Math.Round(r.Width));
                h = (int)Math.Round(w / a);
                if (h > bh)
                {
                    h = bh;
                    w = Math.Min(bw, (int)Math.Round(h * a));
                }
                x = Math.Clamp((int)Math.Round(r.X), 0, bw - w);
                y = Math.Clamp((int)Math.Round(r.Y), 0, bh - h);
            }
            else
            {
                x = (int)Math.Floor(r.X);
                y = (int)Math.Floor(r.Y);
                // Round outwards so the selection never loses its edge pixels.
                w = Math.Min(bw, (int)Math.Ceiling(r.Right)) - x;
                h = Math.Min(bh, (int)Math.Ceiling(r.Bottom)) - y;
            }
            return w >= 2 && h >= 2 ? new Int32Rect(x, y, w, h) : null;
        }
    }

    Rect PictureBounds => new(0, 0, _base?.PixelWidth ?? 0, _base?.PixelHeight ?? 0);

    // ---- Pointer ----

    bool CropPointerDown(Point p)
    {
        _active = true;
        if (_cropRect is { } r)
        {
            if (CropHandleAt(r, p) is { } handle)
            {
                _cropDrag = CropDrag.Handle;
                _cropHandle = handle;
                _cropAnchor = CropHandlePoint(r, (handle + 4) % 8); // corners keep the opposite corner fixed
                return true;
            }
            if (r.Contains(p))
            {
                _cropDrag = CropDrag.Move;
                _cropGrab = p - r.TopLeft;
                return true;
            }
        }

        var bounds = PictureBounds;
        _cropDrag = CropDrag.New;
        _cropAnchor = new Point(Math.Clamp(p.X, bounds.Left, bounds.Right), Math.Clamp(p.Y, bounds.Top, bounds.Bottom));
        _cropRect = null;
        InvalidateVisual();
        return true;
    }

    bool CropPointerMove(Point p)
    {
        var bounds = PictureBounds;
        switch (_cropDrag)
        {
            case CropDrag.New:
                _cropRect = CropFromAnchor(_cropAnchor, p, bounds);
                break;
            case CropDrag.Move when _cropRect is { } r:
                _cropRect = new Rect(
                    Math.Clamp(p.X - _cropGrab.X, bounds.Left, bounds.Right - r.Width),
                    Math.Clamp(p.Y - _cropGrab.Y, bounds.Top, bounds.Bottom - r.Height),
                    r.Width, r.Height);
                break;
            case CropDrag.Handle when _cropRect is { } r:
                _cropRect = _cropHandle % 2 == 0 ? CropFromAnchor(_cropAnchor, p, bounds) : CropResizeEdge(r, _cropHandle, p, bounds);
                break;
        }
        InvalidateVisual();
        return true;
    }

    int? CropHandleAt(Rect r, Point p)
    {
        double tolerance = Tolerance(9);
        for (int i = 0; i < 8; i++)
            if (Distance(CropHandlePoint(r, i), p) <= tolerance) return i;
        return null;
    }

    static Point CropHandlePoint(Rect r, int i) => i switch
    {
        0 => r.TopLeft,
        1 => new Point(r.X + r.Width / 2, r.Top),
        2 => r.TopRight,
        3 => new Point(r.Right, r.Y + r.Height / 2),
        4 => r.BottomRight,
        5 => new Point(r.X + r.Width / 2, r.Bottom),
        6 => r.BottomLeft,
        _ => new Point(r.Left, r.Y + r.Height / 2),
    };

    // ---- Geometry ----

    /// <summary>The area spanned by a fixed corner and the pointer, held to the aspect ratio and the Picture.</summary>
    Rect CropFromAnchor(Point anchor, Point pointer, Rect bounds)
    {
        pointer = new Point(Math.Clamp(pointer.X, bounds.Left, bounds.Right), Math.Clamp(pointer.Y, bounds.Top, bounds.Bottom));
        double sx = pointer.X >= anchor.X ? 1 : -1, sy = pointer.Y >= anchor.Y ? 1 : -1;
        double w = Math.Abs(pointer.X - anchor.X), h = Math.Abs(pointer.Y - anchor.Y);

        if (_cropAspect is { } a)
        {
            w = Math.Max(w, h * a);
            h = w / a;
            double maxW = sx > 0 ? bounds.Right - anchor.X : anchor.X - bounds.Left;
            double maxH = sy > 0 ? bounds.Bottom - anchor.Y : anchor.Y - bounds.Top;
            if (w > 0)
            {
                double fit = Math.Min(1, Math.Min(maxW / w, maxH / h));
                w *= fit;
                h *= fit;
            }
        }
        return new Rect(sx > 0 ? anchor.X : anchor.X - w, sy > 0 ? anchor.Y : anchor.Y - h, w, h);
    }

    /// <summary>Drags one side; with an aspect ratio the other dimension follows, centred on the old area.</summary>
    Rect CropResizeEdge(Rect r, int handle, Point pointer, Rect bounds)
    {
        if (handle is 3 or 7)
        {
            double edge = handle == 3 ? r.Left : r.Right, dir = handle == 3 ? 1 : -1;
            double room = dir > 0 ? bounds.Right - edge : edge - bounds.Left;
            double w = Math.Clamp(dir * (pointer.X - edge), MinCropSize, Math.Max(room, MinCropSize));
            double h = r.Height, y = r.Y;
            if (_cropAspect is { } a)
            {
                h = w / a;
                if (h > bounds.Height)
                {
                    h = bounds.Height;
                    w = h * a;
                }
                y = Math.Clamp(r.Y + r.Height / 2 - h / 2, bounds.Top, bounds.Bottom - h);
            }
            return new Rect(dir > 0 ? edge : edge - w, y, w, h);
        }
        else
        {
            double edge = handle == 5 ? r.Top : r.Bottom, dir = handle == 5 ? 1 : -1;
            double room = dir > 0 ? bounds.Bottom - edge : edge - bounds.Top;
            double h = Math.Clamp(dir * (pointer.Y - edge), MinCropSize, Math.Max(room, MinCropSize));
            double w = r.Width, x = r.X;
            if (_cropAspect is { } a)
            {
                w = h * a;
                if (w > bounds.Width)
                {
                    w = bounds.Width;
                    h = w / a;
                }
                x = Math.Clamp(r.X + r.Width / 2 - w / 2, bounds.Left, bounds.Right - w);
            }
            return new Rect(x, dir > 0 ? edge : edge - h, w, h);
        }
    }

    /// <summary>Puts an area of this aspect ratio around the current one's centre (or a default one if there is none).</summary>
    void FitCropTo(double aspect)
    {
        if (_base is null) return;
        var bounds = PictureBounds;
        double w, h;
        Point centre;
        if (_cropRect is { } r)
        {
            // Keep roughly the same amount of picture, so switching 4:3 ↔ 3:4 turns the area on its side.
            w = Math.Sqrt(r.Width * r.Height * aspect);
            h = w / aspect;
            centre = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }
        else
        {
            w = Math.Min(bounds.Width * 0.8, bounds.Height * 0.8 * aspect);
            h = w / aspect;
            centre = new Point(bounds.Width / 2, bounds.Height / 2);
        }
        double fit = Math.Min(1, Math.Min(bounds.Width / w, bounds.Height / h));
        w *= fit;
        h *= fit;
        _cropRect = new Rect(
            Math.Clamp(centre.X - w / 2, bounds.Left, bounds.Right - w),
            Math.Clamp(centre.Y - h / 2, bounds.Top, bounds.Bottom - h), w, h);
    }

    // ---- Drawing ----

    void DrawCrop(DrawingContext dc, Rect bounds)
    {
        double k = Math.Max(ViewScale, 0.0001);
        Geometry dimmed = new RectangleGeometry(bounds);
        if (_cropRect is { } keep)
            dimmed = new CombinedGeometry(GeometryCombineMode.Exclude, dimmed, new RectangleGeometry(keep));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), null, dimmed);

        if (_cropRect is not { } r) return;

        dc.DrawRectangle(null, new Pen(Brushes.White, 1 / k), r);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1 / k);
        for (int i = 1; i <= 2; i++)
        {
            dc.DrawLine(grid, new Point(r.X + r.Width * i / 3, r.Top), new Point(r.X + r.Width * i / 3, r.Bottom));
            dc.DrawLine(grid, new Point(r.Left, r.Y + r.Height * i / 3), new Point(r.Right, r.Y + r.Height * i / 3));
        }

        var accent = new Pen(new SolidColorBrush(AccentColor), 1.5 / k);
        double half = 4.5 / k;
        for (int i = 0; i < 8; i++)
        {
            var c = CropHandlePoint(r, i);
            dc.DrawRectangle(Brushes.White, accent, new Rect(c.X - half, c.Y - half, half * 2, half * 2));
        }

        if (CropPixelRect is { } area)
        {
            var label = new FormattedText($"{area.Width} × {area.Height}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12 / k, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var at = new Point(r.X + 6 / k, r.Y + 6 / k);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), null,
                new Rect(at.X - 3 / k, at.Y - 1 / k, label.Width + 6 / k, label.Height + 2 / k), 3 / k, 3 / k);
            dc.DrawText(label, at);
        }
    }
}
