using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PictureViewer;

/// <summary>
/// Shows one Picture with fit / actual size / zoom / pan / View rotation, and the annotation layer on top.
/// Zoom is device pixels per Picture pixel, so 1.0 is true actual size on any display scaling.
/// </summary>
public sealed class ZoomPanView : Border
{
    const double MinZoom = 0.02;
    const double MaxZoom = 32;

    readonly Image _image;
    readonly Grid _surface;                 // Picture + annotation layer, in Picture pixels
    readonly MatrixTransform _matrix = new();

    BitmapSource? _source;
    double _zoom = 1;
    Point _offset;          // top-left of the displayed (rotated) Picture in view DIPs
    bool _fit = true;
    int _turns;             // clockwise quarter turns of the View rotation
    Point? _dragStart;
    Point _dragOrigin;
    DispatcherTimer? _animationTimer;
    GifFrames? _animation;
    int _animationIndex;

    public event EventHandler? ViewChanged;

    public AnnotationLayer Layer { get; } = new();

    public ZoomPanView()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent; // needed so the empty area receives mouse input
        Focusable = false;

        _image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _surface = new Grid { RenderTransformOrigin = new Point(0, 0), RenderTransform = _matrix };
        _surface.Children.Add(_image);
        _surface.Children.Add(Layer);
        Child = new Canvas { Children = { _surface } };

        SizeChanged += (_, _) => Layout();
    }

    public bool HasImage => _source != null;
    public bool IsFit => _fit;
    public double Zoom => _zoom;
    public int QuarterTurns => _turns;

    /// <summary>Shows a new Picture; zoom, pan and View rotation start over.</summary>
    public void SetImage(BitmapSource? source)
    {
        StopAnimation();
        _source = source;
        _image.Source = source;
        _surface.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        if (source is not null)
        {
            _surface.Width = source.PixelWidth;
            _surface.Height = source.PixelHeight;
        }
        Layer.Base = source;
        _turns = 0;
        _fit = true;
        Layout();
    }

    /// <summary>
    /// Swaps the pixels. With <paramref name="keepView"/> (and the same size) zoom, pan and View rotation stay as they are;
    /// otherwise it's treated as a new Picture.
    /// </summary>
    public void ReplaceImage(BitmapSource source, bool keepView)
    {
        if (!keepView || _source is null || source.PixelWidth != _source.PixelWidth || source.PixelHeight != _source.PixelHeight)
        {
            SetImage(source);
            return;
        }
        StopAnimation();
        _source = source;
        _image.Source = source;
        Layer.Base = source;
        Layout();
    }

    // ---- Animated GIF ----

    public bool IsAnimating => _animation != null;

    /// <summary>Plays the frames over the Picture (which must be the first frame). Editing the Picture stops it.</summary>
    public void PlayAnimation(GifFrames animation)
    {
        StopAnimation();
        _animation = animation;
        _animationIndex = 0;
        _animationTimer ??= new DispatcherTimer();
        _animationTimer.Tick -= OnAnimationTick;
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Interval = animation.Delays[0];
        _animationTimer.Start();
    }

    public void StopAnimation()
    {
        _animationTimer?.Stop();
        if (_animation == null) return;
        _animation = null;
        if (_source != null) _image.Source = _source; // back to the first frame, which is what edits work on
    }

    void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_animation is not { } animation) return;
        _animationIndex = (_animationIndex + 1) % animation.Frames.Count;
        _image.Source = animation.Frames[_animationIndex];
        _animationTimer!.Interval = animation.Delays[_animationIndex];
    }

    public void FitToWindow()
    {
        _fit = true;
        Layout();
    }

    public void ActualSize() => ZoomTo(1, Center);

    public void ZoomBy(double factor) => ZoomTo(_zoom * factor, Center);

    /// <summary>Turns the View clockwise by quarter turns; the Picture's pixels don't change.</summary>
    public void RotateBy(int turns)
    {
        if (_source is null) return;
        _turns = ((_turns + turns) % 4 + 4) % 4;
        double s = _zoom / DpiScale;
        _offset = new Point((ActualWidth - DisplayWidth * s) / 2, (ActualHeight - DisplayHeight * s) / 2);
        Layout();
    }

    /// <summary>The middle of what's on screen, in Picture pixels (clamped to the Picture).</summary>
    public Point? VisibleCenterInImage()
    {
        if (_source is null) return null;
        var m = _matrix.Matrix;
        if (!m.HasInverse) return null;
        m.Invert();
        var p = m.Transform(Center);
        return new Point(Math.Clamp(p.X, 0, _source.PixelWidth), Math.Clamp(p.Y, 0, _source.PixelHeight));
    }

    Point Center => new(ActualWidth / 2, ActualHeight / 2);

    double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    // Size of the Picture as displayed, after View rotation.
    double DisplayWidth => _source is null ? 0 : _turns % 2 == 1 ? _source.PixelHeight : _source.PixelWidth;
    double DisplayHeight => _source is null ? 0 : _turns % 2 == 1 ? _source.PixelWidth : _source.PixelHeight;

    double FitZoom()
    {
        if (_source is null || ActualWidth <= 0 || ActualHeight <= 0) return 1;
        double dpi = DpiScale;
        double z = Math.Min(ActualWidth * dpi / DisplayWidth, ActualHeight * dpi / DisplayHeight);
        return Math.Min(z, 1); // fit never enlarges a small Picture
    }

    /// <summary>Zooms to <paramref name="zoom"/> keeping the displayed point under <paramref name="pivot"/> fixed.</summary>
    void ZoomTo(double zoom, Point pivot)
    {
        if (_source is null) return;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        double oldScale = _zoom / DpiScale, newScale = zoom / DpiScale;
        double px = (pivot.X - _offset.X) / oldScale, py = (pivot.Y - _offset.Y) / oldScale;

        _fit = false;
        _zoom = zoom;
        _offset = new Point(pivot.X - px * newScale, pivot.Y - py * newScale);
        Layout();
    }

    void Layout()
    {
        if (_source is null)
        {
            ViewChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_fit) _zoom = FitZoom();

        double s = _zoom / DpiScale;
        double w = DisplayWidth * s, h = DisplayHeight * s;
        double vw = ActualWidth, vh = ActualHeight;

        // Center when smaller than the view; otherwise keep the edges from leaving it.
        double x = w <= vw ? (vw - w) / 2 : Math.Clamp(_offset.X, vw - w, 0);
        double y = h <= vh ? (vh - h) / 2 : Math.Clamp(_offset.Y, vh - h, 0);
        _offset = new Point(x, y);

        // Scale, rotate about the origin, then slide the rotated corner to the offset.
        var m = Matrix.Identity;
        m.Scale(s, s);
        m.Rotate(90 * _turns);
        var box = new MatrixTransform(m).TransformBounds(new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        m.Translate(x - box.X, y - box.Y);
        _matrix.Matrix = m;

        if (Math.Abs(Layer.ViewScale - s) > 1e-9)
        {
            Layer.ViewScale = s;
            Layer.Refresh(); // handles are drawn at a fixed screen size
        }

        // Show real pixels when zoomed far in.
        RenderOptions.SetBitmapScalingMode(_image, _zoom >= 3 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Cursor = CanPan ? Cursors.Hand : Cursors.Arrow;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    bool CanPan =>
        _source is not null &&
        (DisplayWidth * _zoom / DpiScale > ActualWidth || DisplayHeight * _zoom / DpiScale > ActualHeight);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Layout(); // zoom is in device pixels, so only the DIP scale needs refreshing
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_source is null) return;
        ZoomTo(_zoom * Math.Pow(1.0015, e.Delta), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_source is null) return;

        if (e.ClickCount == 2)
        {
            // Double-click toggles between fit and actual size.
            if (_fit && Math.Abs(_zoom - 1) > 0.001) ZoomTo(1, e.GetPosition(this));
            else FitToWindow();
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragOrigin = _offset;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start || !IsMouseCaptured) return;
        var p = e.GetPosition(this);
        _offset = new Point(_dragOrigin.X + p.X - start.X, _dragOrigin.Y + p.Y - start.Y);
        Layout();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragStart = null;
        ReleaseMouseCapture();
    }
}
