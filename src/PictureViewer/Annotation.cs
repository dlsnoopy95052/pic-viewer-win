using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

public enum AnnotationTool { None, Line, Arrow, Rect, Ellipse, Pen, Text, Mosaic, Image }

/// <summary>
/// A mark drawn over a Picture. Points are in Picture pixels with the origin at the top-left.
/// It stays editable (move / resize / restyle / delete) until the Picture is flattened.
/// </summary>
public sealed class Annotation
{
    public AnnotationTool Tool { get; set; }
    public List<Point> Points { get; set; } = [];
    public double Width { get; set; }
    public Color Color { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; }

    /// <summary>Radians, clockwise on screen, about the center (Rect / Ellipse / Text / Image only).</summary>
    public double Angle { get; set; }

    /// <summary>Text only: wrap width of the text area in Picture pixels (null = no wrapping) and fill behind it.</summary>
    public double? BoxWidth { get; set; }
    public Color? Background { get; set; }

    /// <summary>Font family ("" = system font) and traits.</summary>
    public string FontName { get; set; } = "";
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }

    /// <summary>Pasted picture (Tool == Image); the two points are opposite corners.</summary>
    public BitmapSource? Image { get; set; }

    public Annotation Clone()
    {
        var copy = (Annotation)MemberwiseClone();
        copy.Points = [.. Points];
        return copy;
    }

    /// <summary>Two-point shapes whose end points can be dragged.</summary>
    public bool HasHandles => Tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Rect
        or AnnotationTool.Ellipse or AnnotationTool.Mosaic or AnnotationTool.Image;

    /// <summary>Shapes that get a rotation knob (lines and arrows already point anywhere via their end points).</summary>
    public bool CanRotate => Tool is AnnotationTool.Rect or AnnotationTool.Ellipse or AnnotationTool.Text or AnnotationTool.Image;
}
