using System.Windows;
using System.Windows.Media.Imaging;

namespace PictureViewer;

public enum PrintScaleMode { ShrinkToFit, FitToPage, ActualSize, FillPage }

/// <summary>Where a Picture lands on the printable area; shared by the preview and the real print so they always agree.</summary>
public static class PrintLayout
{
    public static string Title(PrintScaleMode mode) => mode switch
    {
        PrintScaleMode.ShrinkToFit => "Shrink to Fit (only if too large)",
        PrintScaleMode.FitToPage => "Fit to Page (enlarge if needed)",
        PrintScaleMode.ActualSize => "Actual Size",
        _ => "Fill Page (crop edges)",
    };

    /// <summary>The Picture's size at its own resolution, in 1/96 inch units (what "actual size" prints).</summary>
    public static Size NaturalSize(BitmapSource picture)
    {
        double dpiX = picture.DpiX is >= 36 and <= 1200 ? picture.DpiX : 96;
        double dpiY = picture.DpiY is >= 36 and <= 1200 ? picture.DpiY : 96;
        return new Size(picture.PixelWidth * 96 / dpiX, picture.PixelHeight * 96 / dpiY);
    }

    /// <summary>The rectangle the Picture occupies inside an area of <paramref name="area"/> size (origin at the area's top-left).</summary>
    public static Rect Place(Size picture, Size area, PrintScaleMode mode, bool centered)
    {
        if (picture.Width <= 0 || picture.Height <= 0 || area.Width <= 0 || area.Height <= 0) return Rect.Empty;

        double fit = Math.Min(area.Width / picture.Width, area.Height / picture.Height);
        double scale = mode switch
        {
            PrintScaleMode.ShrinkToFit => Math.Min(1, fit),
            PrintScaleMode.FitToPage => fit,
            PrintScaleMode.ActualSize => 1,
            _ => Math.Max(area.Width / picture.Width, area.Height / picture.Height),
        };

        double w = picture.Width * scale, h = picture.Height * scale;
        return centered
            ? new Rect((area.Width - w) / 2, (area.Height - h) / 2, w, h)
            : new Rect(0, 0, w, h);
    }
}
