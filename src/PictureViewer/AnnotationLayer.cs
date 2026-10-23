using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>
/// Sits on top of the Picture in the same coordinate space (Picture pixels), so zoom, pan and View rotation
/// apply to it automatically. Draws the annotations and handles drawing, selecting, moving, resizing and rotating.
/// </summary>
public sealed partial class AnnotationLayer : FrameworkElement
{
    enum Drag { None, Move, Handle, Rotate, Width }

    static readonly Color AccentColor = Color.FromRgb(0x00, 0x78, 0xD4);

    BitmapSource? _base;
    AnnotationTool _tool;
    int? _selected;
    List<Point> _drawPoints = [];
    Drag _drag;
    Point _lastPoint;
    int _handle;
    bool _dragStarted;
    bool _active;           // a draw or drag gesture is in progress

    public List<Annotation> Items { get; } = [];

    public AnnotationLayer()
    {
        ClipToBounds = true; // anything outside the Picture is lost when saving, so it isn't shown either
    }

    // Style for new annotations (the owner keeps these in sync with its controls).
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    public double NominalWidth { get; set; } = 4;
    public Color? Background { get; set; }
    public string FontName { get; set; } = "";
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }

    /// <summary>Picture pixels to on-screen DIPs; keeps handles the same size at any zoom.</summary>
    public double ViewScale { get; set; } = 1;

    public event EventHandler? ToolChanged;
    public event EventHandler? SelectionChanged;

    /// <summary>Raised once, right before an interactive change mutates the items (push undo here).</summary>
    public event EventHandler? BeginChange;

    /// <summary>A drawing gesture finished; the owner adds it (after pushing undo).</summary>
    public event EventHandler<Annotation>? ShapeCreated;

    public event EventHandler<Point>? TextRequested;
    public event EventHandler<int>? EditTextRequested;

    public BitmapSource? Base
    {
        get => _base;
        set
        {
            _base = value;
            Width = value?.PixelWidth ?? 0;
            Height = value?.PixelHeight ?? 0;
            InvalidateVisual();
        }
    }

    public AnnotationTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            if (value != AnnotationTool.None)
            {
                SelectedIndex = null;
                CropMode = false;
            }
            _drawPoints = [];
            Cursor = value == AnnotationTool.None ? null : Cursors.Cross;
            InvalidateVisual();
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int? SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Annotation? Selected => _selected is { } i && i < Items.Count ? Items[i] : null;

    double StrokeScale => _base is null ? 1 : AnnotationGeometry.StrokeScale(_base.PixelWidth, _base.PixelHeight);

    // ---- Changing the items ----

    public void Refresh() => InvalidateVisual();

    public void Add(Annotation annotation)
    {
        Items.Add(annotation);
        _selected = null; // force the change notification below
        SelectedIndex = Items.Count - 1;
        InvalidateVisual();
    }

    public void ReplaceItems(IEnumerable<Annotation> items)
    {
        Items.Clear();
        Items.AddRange(items);
        SelectedIndex = null;
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (_selected is not { } i || i >= Items.Count) return;
        BeginChange?.Invoke(this, EventArgs.Empty);
        Items.RemoveAt(i);
        SelectedIndex = null;
        InvalidateVisual();
    }

    public Annotation NewShape(IEnumerable<Point> points) => new()
    {
        Tool = _tool,
        Points = [.. points],
        Width = NominalWidth * StrokeScale,
        Color = Color,
    };

    public Annotation NewText(Point at, string text) => new()
    {
        Tool = AnnotationTool.Text,
        Points = [at],
        Width = NominalWidth * StrokeScale,
        Color = Color,
        Text = text,
        FontSize = NominalWidth * 6 * StrokeScale,
        Background = Background,
        FontName = FontName,
        Bold = Bold,
        Italic = Italic,
    };

    // ---- Hit testing ----

    double Tolerance(double screenPixels) => screenPixels / Math.Max(ViewScale, 0.0001);

    int? ItemAt(Point p)
    {
        double tol = Tolerance(7);
        for (int i = Items.Count - 1; i >= 0; i--)
            if (AnnotationGeometry.Distance(p, Items[i]) <= Math.Max(Items[i].Width / 2, tol)) return i;
        return null;
    }

    int? HandleAt(Point p)
    {
        if (Selected is not { HasHandles: true } a) return null;
        double tol = Tolerance(9);
        var c = AnnotationGeometry.Center(a);
        for (int i = 0; i < a.Points.Count; i++)
        {
            var w = AnnotationGeometry.Rotate(a.Points[i], c, a.Angle);
            if (AnnotationGeometry.Length(w.X - p.X, w.Y - p.Y) <= tol) return i;
        }
        return null;
    }

    Point RotationKnob(Annotation a)
    {
        var b = AnnotationGeometry.Bounds(a);
        var c = AnnotationGeometry.Center(a);
        return AnnotationGeometry.Rotate(new Point(c.X, b.Y - Tolerance(26)), c, a.Angle);
    }

    bool IsOnRotationKnob(Point p) =>
        Selected is { CanRotate: true } a && Distance(RotationKnob(a), p) <= Tolerance(9);

    bool IsOnWidthHandle(Point p) =>
        Selected is { Tool: AnnotationTool.Text } a && Distance(AnnotationGeometry.WidthHandle(a), p) <= Tolerance(9);

    static double Distance(Point a, Point b) => AnnotationGeometry.Length(a.X - b.X, a.Y - b.Y);

    // ---- Pointer input ----
    // The WPF overrides below only translate events; PointerDown / PointerMove / PointerUp hold the behavior
    // so it can be driven with plain coordinates.

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!PointerDown(e.GetPosition(this), e.ClickCount)) return;
        e.Handled = true;
        if (_active) CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_active && PointerMove(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_active) return;
        ReleaseMouseCapture();
        PointerUp();
        e.Handled = true;
    }

    /// <summary>
    /// A press at <paramref name="p"/> (Picture pixels). Returns true if the layer used it; false lets the view pan.
    /// </summary>
    public bool PointerDown(Point p, int clickCount = 1)
    {
        if (_base is null) return false;

        if (_cropMode) return CropPointerDown(p);

        if (_tool != AnnotationTool.None)
        {
            if (_tool == AnnotationTool.Text)
            {
                TextRequested?.Invoke(this, p);
            }
            else
            {
                _drawPoints = [p];
                _active = true;
            }
            return true;
        }

        if (Items.Count == 0) return false;

        if (IsOnWidthHandle(p) && Selected is { } text)
        {
            if (clickCount == 2)
            {
                BeginChange?.Invoke(this, EventArgs.Empty);
                text.BoxWidth = null; // double-click: back to no wrapping
                InvalidateVisual();
            }
            else
            {
                StartDrag(Drag.Width, p);
            }
            return true;
        }
        if (IsOnRotationKnob(p))
        {
            StartDrag(Drag.Rotate, p);
            return true;
        }
        if (HandleAt(p) is { } handle)
        {
            _handle = handle;
            StartDrag(Drag.Handle, p);
            return true;
        }
        if (ItemAt(p) is { } hit)
        {
            SelectedIndex = hit;
            if (clickCount == 2 && Items[hit].Tool == AnnotationTool.Text) EditTextRequested?.Invoke(this, hit);
            else StartDrag(Drag.Move, p);
            return true;
        }
        SelectedIndex = null; // empty spot: deselect, and let the view pan
        return false;
    }

    void StartDrag(Drag kind, Point p)
    {
        _drag = kind;
        _lastPoint = p;
        _dragStarted = false;
        _active = true;
    }

    public bool PointerMove(Point p, bool snapAngle = false)
    {
        if (!_active || _base is null) return false;

        if (_cropDrag != CropDrag.None) return CropPointerMove(p);

        if (_drawPoints.Count > 0)
        {
            if (_tool == AnnotationTool.Pen) _drawPoints.Add(p);
            else _drawPoints = [_drawPoints[0], p];
            InvalidateVisual();
            return true;
        }

        if (Selected is not { } a || _drag == Drag.None) return false;

        switch (_drag)
        {
            case Drag.Move:
            {
                Begin();
                double dx = p.X - _lastPoint.X, dy = p.Y - _lastPoint.Y;
                for (int i = 0; i < a.Points.Count; i++) a.Points[i] = new Point(a.Points[i].X + dx, a.Points[i].Y + dy);
                _lastPoint = p;
                break;
            }
            case Drag.Handle:
                if (a.Points.Count != 2) return false;
                Begin();
                ResizeByHandle(a, _handle, p);
                break;
            case Drag.Width:
                Begin();
                AnnotationGeometry.SetWrapWidth(a, p);
                break;
            case Drag.Rotate:
            {
                Begin();
                var c = AnnotationGeometry.Center(a);
                double angle = Math.Atan2(p.Y - c.Y, p.X - c.X) + Math.PI / 2;
                if (snapAngle)
                {
                    double step = Math.PI / 12;
                    angle = Math.Round(angle / step) * step;
                }
                a.Angle = angle;
                break;
            }
        }
        InvalidateVisual();
        return true;
    }

    public void PointerUp()
    {
        if (!_active) return;
        _active = false;
        _drag = Drag.None;
        _dragStarted = false;
        bool drewCropArea = _cropDrag == CropDrag.New;
        _cropDrag = CropDrag.None;
        if (drewCropArea) RaiseOcrAreaChosen();

        if (_drawPoints.Count >= 2 && _tool != AnnotationTool.None)
        {
            var shape = NewShape(_drawPoints);
            _drawPoints = [];
            InvalidateVisual();
            ShapeCreated?.Invoke(this, shape);
        }
        _drawPoints = [];
    }

    void Begin()
    {
        if (_dragStarted) return;
        _dragStarted = true;
        BeginChange?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves one corner to <paramref name="mouse"/>, keeping the opposite corner fixed in the Picture even when rotated.</summary>
    static void ResizeByHandle(Annotation a, int handle, Point mouse)
    {
        int other = 1 - handle;
        var opposite = AnnotationGeometry.Rotate(a.Points[other], AnnotationGeometry.Center(a), a.Angle);
        var target = mouse;

        if (a.Tool == AnnotationTool.Image && a.Image is { PixelHeight: > 0 } img)
        {
            // Pictures keep their proportions while resizing.
            double aspect = (double)img.PixelWidth / img.PixelHeight;
            double dx = mouse.X - opposite.X, dy = mouse.Y - opposite.Y;
            double cos = Math.Cos(a.Angle), sin = Math.Sin(a.Angle);
            double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
            double w = Math.Abs(lx), h = Math.Abs(ly);
            if (w > h * aspect) h = w / aspect; else w = h * aspect;
            double nx = (lx < 0 ? -1 : 1) * w, ny = (ly < 0 ? -1 : 1) * h;
            target = new Point(opposite.X + nx * cos - ny * sin, opposite.Y + nx * sin + ny * cos);
        }

        var center = new Point((target.X + opposite.X) / 2, (target.Y + opposite.Y) / 2);
        a.Points[handle] = AnnotationGeometry.Rotate(target, center, -a.Angle);
        a.Points[other] = AnnotationGeometry.Rotate(opposite, center, -a.Angle);
    }

    // ---- Drawing ----

    protected override void OnRender(DrawingContext dc)
    {
        if (_base is null) return;
        var bounds = new Rect(0, 0, _base.PixelWidth, _base.PixelHeight);
        dc.DrawRectangle(Brushes.Transparent, null, bounds); // so the empty area is hit-testable
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        foreach (var a in Items) AnnotationRenderer.Draw(dc, a, _base);

        if (_drawPoints.Count >= 2 && _tool is not (AnnotationTool.None or AnnotationTool.Text))
            AnnotationRenderer.Draw(dc, NewShape(_drawPoints), _base);

        if (Selected is { } selected) DrawSelection(dc, selected);
        if (_cropMode) DrawCrop(dc, bounds);
    }

    void DrawSelection(DrawingContext dc, Annotation a)
    {
        double k = Math.Max(ViewScale, 0.0001);
        var accent = new SolidColorBrush(AccentColor);
        accent.Freeze();
        var dashed = new Pen(accent, 1 / k) { DashStyle = new DashStyle([4, 3], 0) };
        var solid = new Pen(accent, 1.5 / k);

        var center = AnnotationGeometry.Center(a);
        if (a.Angle != 0) dc.PushTransform(new RotateTransform(a.Angle * 180 / Math.PI, center.X, center.Y));

        var box = AnnotationGeometry.Bounds(a);
        box.Inflate(4 / k, 4 / k);
        dc.DrawRectangle(null, dashed, box);

        if (a.CanRotate)
        {
            var top = new Point(box.X + box.Width / 2, box.Y);
            var knob = new Point(top.X, top.Y - 22 / k);
            dc.DrawLine(new Pen(accent, 1 / k), top, knob);
            dc.DrawEllipse(Brushes.White, solid, knob, 5 / k, 5 / k);
        }

        if (a.Tool == AnnotationTool.Text)
        {
            var r = AnnotationGeometry.TextRect(a);
            var mid = new Point(r.Right, r.Y + r.Height / 2);
            dc.DrawRectangle(Brushes.White, solid, new Rect(mid.X - 3 / k, mid.Y - 8 / k, 6 / k, 16 / k));
        }

        if (a.HasHandles)
        {
            foreach (var p in a.Points)
                dc.DrawRectangle(Brushes.White, solid, new Rect(p.X - 4 / k, p.Y - 4 / k, 8 / k, 8 / k));
        }

        if (a.Angle != 0) dc.Pop();
    }
}
