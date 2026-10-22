using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PictureViewer;

/// <summary>A color swatch that opens a small palette (with an optional "none" and a custom color dialog).</summary>
public sealed class ColorButton : Button
{
    static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0x95, 0x00), Color.FromRgb(0xFF, 0xCC, 0x00),
        Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x00, 0xC7, 0xBE), Color.FromRgb(0x00, 0x7A, 0xFF),
        Color.FromRgb(0x58, 0x56, 0xD6), Color.FromRgb(0xAF, 0x52, 0xDE), Color.FromRgb(0xFF, 0x2D, 0x55),
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x8E, 0x8E, 0x93), Color.FromRgb(0x00, 0x00, 0x00),
    ];

    readonly Border _swatch;
    readonly Popup _popup;
    Color? _value;

    /// <summary>Whether the palette offers "No color" (used for the text background).</summary>
    public bool AllowNone { get; init; }

    /// <summary>Raised when the user picks a color (not when the value is set from code).</summary>
    public event EventHandler? ValueChanged;

    public Color? Value
    {
        get => _value;
        set
        {
            _value = value;
            UpdateSwatch();
        }
    }

    public ColorButton()
    {
        Width = 38;
        Height = 28;
        Margin = new Thickness(2, 0, 2, 0);
        Focusable = false;

        _swatch = new Border
        {
            Width = 22, Height = 14, CornerRadius = new CornerRadius(2),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Content = _swatch;

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        Click += (_, _) =>
        {
            _popup.Child = BuildPopup();
            _popup.IsOpen = true;
        };
        UpdateSwatch();
    }

    void UpdateSwatch()
    {
        if (_value is { } c)
        {
            _swatch.Background = new SolidColorBrush(c);
            _swatch.Child = null;
        }
        else
        {
            _swatch.Background = Brushes.White;
            _swatch.Child = new Line { X1 = 0, Y1 = 12, X2 = 20, Y2 = 0, Stroke = Brushes.Red, StrokeThickness = 1.5 };
        }
    }

    UIElement BuildPopup()
    {
        var grid = new UniformGrid { Columns = 6 };
        foreach (var color in Palette)
            grid.Children.Add(Swatch(color));

        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(grid);
        if (AllowNone) panel.Children.Add(TextButton("No color", () => Pick(null)));
        panel.Children.Add(TextButton("Custom…", PickCustom));

        return new Border
        {
            Background = SystemColors.ControlBrush,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(6), // room for the shadow-less edge
            Child = panel,
        };
    }

    Button Swatch(Color color)
    {
        var button = new Button
        {
            Width = 24, Height = 24, Margin = new Thickness(2), Focusable = false,
            Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
        };
        button.Click += (_, _) => Pick(color);
        return button;
    }

    Button TextButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(2, 6, 2, 0), Padding = new Thickness(6, 3, 6, 3), Focusable = false };
        button.Click += (_, _) => action();
        return button;
    }

    void PickCustom()
    {
        _popup.IsOpen = false;
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (_value is { } c) dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            Pick(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    void Pick(Color? color)
    {
        _popup.IsOpen = false;
        Value = color;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
