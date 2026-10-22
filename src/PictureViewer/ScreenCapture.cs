using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace PictureViewer;

/// <summary>
/// Area capture: freezes every screen, dims it, and lets the user drag out the part to keep.
/// Works from the pixels grabbed up front, so what you select is exactly what you saw.
/// </summary>
public static class ScreenCapture
{
    public static bool IsActive { get; private set; }

    /// <summary>The selected area as a Picture, or null if cancelled (Esc or right-click).</summary>
    public static Task<BitmapSource?> SelectAreaAsync()
    {
        var done = new TaskCompletionSource<BitmapSource?>();
        if (IsActive)
        {
            done.SetResult(null);
            return done.Task;
        }
        IsActive = true;

        var overlays = new List<CaptureOverlay>();
        void Finish(BitmapSource? result)
        {
            if (done.Task.IsCompleted) return;
            IsActive = false;
            foreach (var overlay in overlays) overlay.Close();
            done.SetResult(result);
        }

        try
        {
            // Grab everything first, then cover each screen with its own overlay (each has its own DPI).
            var screens = System.Windows.Forms.Screen.AllScreens;
            var shots = screens.Select(s => (Bounds: s.Bounds, Shot: Grab(s.Bounds))).ToList();
            foreach (var (bounds, shot) in shots)
                overlays.Add(new CaptureOverlay(shot, bounds, showHint: bounds == screens.First(s => s.Primary).Bounds, Finish));
            foreach (var overlay in overlays) overlay.Present();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Finish(null);
        }
        return done.Task;
    }

    const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int x1, int y1, uint rop);

    static BitmapSource Grab(System.Drawing.Rectangle area)
    {
        using var bitmap = new System.Drawing.Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            // BitBlt rather than Graphics.CopyFromScreen: only it can add CAPTUREBLT, which also picks up
            // layered / transparent windows.
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr targetDc = g.GetHdc();
            try
            {
                if (!BitBlt(targetDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SrcCopy | CaptureBlt))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                g.ReleaseHdc(targetDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, area.Width, area.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null,
                data.Scan0, data.Stride * area.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

/// <summary>A borderless window over one screen showing its frozen picture; drag to choose the area.</summary>
sealed class CaptureOverlay : Window
{
    const uint SwpNoActivate = 0x0010;
    static readonly IntPtr HwndTopmost = new(-1);

    readonly BitmapSource _shot;
    readonly System.Drawing.Rectangle _screen;
    readonly Action<BitmapSource?> _finish;

    readonly Grid _root = new();
    readonly Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), IsHitTestVisible = false };
    readonly Shapes.Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = [5, 3], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Border _label = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(3),
        Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    readonly TextBlock _labelText = new() { Foreground = Brushes.White, FontSize = 12 };
    readonly Canvas _canvas = new() { IsHitTestVisible = false };
    readonly Border? _hint;

    Point? _start;
    Rect? _selection;

    public CaptureOverlay(BitmapSource shot, System.Drawing.Rectangle screen, bool showHint, Action<BitmapSource?> finish)
    {
        _shot = shot;
        _screen = screen;
        _finish = finish;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.X; Top = screen.Y; Width = 200; Height = 200; // real size is forced in physical pixels below

        _root.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
        _root.Children.Add(_dim);
        _label.Child = _labelText;
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_label);
        _root.Children.Add(_canvas);
        if (showHint)
        {
            _hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 28, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Child = new TextBlock { Text = "Drag to select an area  ·  Esc or right-click to cancel", Foreground = Brushes.White, FontSize = 14 },
            };
            _root.Children.Add(_hint);
        }
        Content = _root;

        SizeChanged += (_, _) => UpdateVisuals();
        DpiChanged += (_, _) => ForceBounds();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) _finish(null);
        };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => _finish(null);
    }

    public void Present()
    {
        Show();
        ForceBounds();
        Activate();
        Focus();
    }

    /// <summary>Covers the screen exactly, in physical pixels, whatever the DPI scaling does to DIP sizes.</summary>
    void ForceBounds()
    {
        // Through WPF first, so its layout follows the new size (resizing the HWND alone leaves the layout stale)...
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _screen.X / dpi.DpiScaleX;
        Top = _screen.Y / dpi.DpiScaleY;
        Width = _screen.Width / dpi.DpiScaleX;
        Height = _screen.Height / dpi.DpiScaleY;

        // ...then exactly, in case rounding the DIPs left it a pixel off.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, HwndTopmost, _screen.X, _screen.Y, _screen.Width, _screen.Height, SwpNoActivate);
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        BeginSelection(e.GetPosition(_root));
        CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e) => UpdateSelection(e.GetPosition(_root));

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        EndSelection();
    }

    // The selection steps work on plain points so they can be driven without a mouse.

    void BeginSelection(Point p)
    {
        _start = p;
        _selection = null;
        if (_hint != null) _hint.Visibility = Visibility.Collapsed;
        UpdateVisuals();
    }

    void UpdateSelection(Point p)
    {
        if (_start is not { } start) return;
        _selection = Rect.Intersect(new Rect(start, p), new Rect(0, 0, _root.ActualWidth, _root.ActualHeight));
        UpdateVisuals();
    }

    void EndSelection()
    {
        if (_start is null) return;
        _start = null;

        if (_selection is { } sel && ToPixels(sel) is { } area)
        {
            var crop = new CroppedBitmap(_shot, area);
            crop.Freeze();
            _finish(crop);
        }
        else
        {
            _selection = null; // a click without a drag: let the user try again
            UpdateVisuals();
        }
    }

    /// <summary>The selection as whole pixels of the frozen screenshot (independent of DPI), or null if too small.</summary>
    Int32Rect? ToPixels(Rect dips)
    {
        if (_root.ActualWidth <= 0 || _root.ActualHeight <= 0) return null;
        double kx = _shot.PixelWidth / _root.ActualWidth, ky = _shot.PixelHeight / _root.ActualHeight;
        int x = (int)Math.Floor(dips.X * kx), y = (int)Math.Floor(dips.Y * ky);
        int w = Math.Min(_shot.PixelWidth, (int)Math.Ceiling(dips.Right * kx)) - x;
        int h = Math.Min(_shot.PixelHeight, (int)Math.Ceiling(dips.Bottom * ky)) - y;
        return w >= 3 && h >= 3 ? new Int32Rect(x, y, w, h) : null;
    }

    void UpdateVisuals()
    {
        var full = new RectangleGeometry(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight));
        if (_selection is { } sel && sel.Width > 0 && sel.Height > 0)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(sel));

            _frame.Visibility = Visibility.Visible;
            _frame.Width = sel.Width; _frame.Height = sel.Height;
            Canvas.SetLeft(_frame, sel.X); Canvas.SetTop(_frame, sel.Y);

            if (ToPixels(sel) is { } px) _labelText.Text = $"{px.Width} × {px.Height}";
            _label.Visibility = Visibility.Visible;
            Canvas.SetLeft(_label, sel.X);
            Canvas.SetTop(_label, sel.Y >= 28 ? sel.Y - 28 : sel.Y + 4);
        }
        else
        {
            _dim.Data = full;
            _frame.Visibility = _label.Visibility = Visibility.Collapsed;
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
