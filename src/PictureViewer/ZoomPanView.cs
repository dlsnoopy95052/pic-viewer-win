using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>
/// Shows one Picture with fit / actual size / zoom / pan.
/// Zoom is device pixels per Picture pixel, so 1.0 is true actual size on any display scaling.
/// </summary>
public sealed class ZoomPanView : Border
{
    const double MinZoom = 0.02;
    const double MaxZoom = 32;

    readonly Image _image;
    readonly ScaleTransform _scale = new();
    readonly TranslateTransform _move = new();

    BitmapSource? _source;
    double _zoom = 1;
    Point _offset;          // top-left of the Picture in view DIPs
    bool _fit = true;
    Point? _dragStart;
    Point _dragOrigin;

    public event EventHandler? ViewChanged;

    public ZoomPanView()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent; // needed so the empty area receives mouse input
        Focusable = false;

        _image = new Image
        {
            Stretch = Stretch.Fill,
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = new TransformGroup { Children = { _scale, _move } },
            SnapsToDevicePixels = false,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Child = new Canvas { Children = { _image } };

        SizeChanged += (_, _) => Layout();
    }

    public bool HasImage => _source != null;
    public bool IsFit => _fit;
    public double Zoom => _zoom;

    public void SetImage(BitmapSource? source)
    {
        _source = source;
        _image.Source = source;
        _image.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        if (source is not null)
        {
            _image.Width = source.PixelWidth;
            _image.Height = source.PixelHeight;
        }
        _fit = true;
        Layout();
    }

    public void FitToWindow()
    {
        _fit = true;
        Layout();
    }

    public void ActualSize() => ZoomTo(1, Center);

    public void ZoomBy(double factor) => ZoomTo(_zoom * factor, Center);

    Point Center => new(ActualWidth / 2, ActualHeight / 2);

    double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    double FitZoom()
    {
        if (_source is null || ActualWidth <= 0 || ActualHeight <= 0) return 1;
        double dpi = DpiScale;
        double z = Math.Min(ActualWidth * dpi / _source.PixelWidth, ActualHeight * dpi / _source.PixelHeight);
        return Math.Min(z, 1); // fit never enlarges a small Picture
    }

    /// <summary>Zooms to <paramref name="zoom"/> keeping the Picture point under <paramref name="pivot"/> fixed.</summary>
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
        double w = _source.PixelWidth * s, h = _source.PixelHeight * s;
        double vw = ActualWidth, vh = ActualHeight;

        // Center when smaller than the view; otherwise keep the edges from leaving it.
        double x = w <= vw ? (vw - w) / 2 : Math.Clamp(_offset.X, vw - w, 0);
        double y = h <= vh ? (vh - h) / 2 : Math.Clamp(_offset.Y, vh - h, 0);
        _offset = new Point(x, y);

        _scale.ScaleX = _scale.ScaleY = s;
        _move.X = x;
        _move.Y = y;

        // Show real pixels when zoomed far in.
        RenderOptions.SetBitmapScalingMode(_image, _zoom >= 3 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Cursor = CanPan ? Cursors.Hand : Cursors.Arrow;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    bool CanPan =>
        _source is not null &&
        (_source.PixelWidth * _zoom / DpiScale > ActualWidth || _source.PixelHeight * _zoom / DpiScale > ActualHeight);

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
        if (_dragStart is not { } start) return;
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
