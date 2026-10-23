using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PictureViewer;

/// <summary>Asks for the exact pixel size a crop is scaled to.</summary>
public sealed class CropSizeDialog : Window
{
    const int MaxSide = 30000;

    readonly TextBox _width, _height;

    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    public CropSizeDialog(int width, int height)
    {
        Title = "Crop to size";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _width = new TextBox { Text = width.ToString(CultureInfo.InvariantCulture), Width = 80, Padding = new Thickness(4, 3, 4, 3) };
        _height = new TextBox { Text = height.ToString(CultureInfo.InvariantCulture), Width = 80, Padding = new Thickness(4, 3, 4, 3) };
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

        var ok = new Button { Content = "OK", Width = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 84, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!TryParse(_width.Text, out int w) || !TryParse(_height.Text, out int h))
            {
                error.Text = $"Enter whole numbers from 1 to {MaxSide}.";
                return;
            }
            PixelWidth = w;
            PixelHeight = h;
            DialogResult = true;
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "The cropped area keeps this shape and is scaled to exactly this many pixels.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        _width,
                        new TextBlock { Text = "×", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center },
                        _height,
                        new TextBlock { Text = "px", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel },
                },
            },
        };

        Loaded += (_, _) =>
        {
            _width.Focus();
            _width.SelectAll();
        };
    }

    static bool TryParse(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 1 and <= MaxSide;
}
