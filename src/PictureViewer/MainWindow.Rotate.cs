using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

// Rotating the Picture's pixels by any angle, with a live preview.
public partial class MainWindow
{
    const double MaxStraightenAngle = 45;
    const int StraightenPreviewSide = 1400;

    void OnStraightenClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        View.Layer.CropMode = false;
        View.Layer.Tool = AnnotationTool.None;
        FlattenPending();
        var baseImage = _current!;
        var working = ImageEdit.Downscale(baseImage, StraightenPreviewSide); // the preview is drawn from a smaller copy
        bool applied = false, syncing = false;

        var slider = new Slider
        {
            Minimum = -MaxStraightenAngle, Maximum = MaxStraightenAngle, Value = 0, Width = 220,
            SmallChange = 0.1, LargeChange = 1, TickFrequency = 15, TickPlacement = TickPlacement.BottomRight,
            Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new TextBox { Text = "0.0", Width = 52, Padding = new Thickness(3, 2, 3, 2), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = "The whole picture is kept. Use Crop afterwards to trim the empty corners.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Width = 300, Margin = new Thickness(0, 10, 0, 0) };

        // Every preview is the same size, so a zoom the user picked survives while the angle changes.
        void Preview() =>
            View.ReplaceImage(ImageEdit.RotatePreview(working, slider.Value), keepView: true);

        void SetAngle(double degrees)
        {
            degrees = Math.Round(Math.Clamp(degrees, -MaxStraightenAngle, MaxStraightenAngle), 1);
            syncing = true;
            slider.Value = degrees;
            box.Text = degrees.ToString("0.0", CultureInfo.InvariantCulture);
            syncing = false;
            Preview();
        }

        slider.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            SetAngle(slider.Value);
        };
        box.TextChanged += (_, _) =>
        {
            if (syncing) return;
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed))
            {
                syncing = true;
                slider.Value = Math.Clamp(typed, -MaxStraightenAngle, MaxStraightenAngle);
                syncing = false;
                Preview();
            }
        };

        Button Nudge(string text, double by, string tip)
        {
            var b = new Button { Content = text, Width = 28, Height = 24, Margin = new Thickness(2, 0, 2, 0), Focusable = false, ToolTip = tip };
            b.Click += (_, _) => SetAngle(slider.Value + by);
            return b;
        }

        var reset = new Button { Content = "Reset", Width = 72, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        reset.Click += (_, _) => SetAngle(0);
        var apply = new Button { Content = "Apply", Width = 72, IsDefault = true, Focusable = false };

        var popup = new Popup
        {
            PlacementTarget = StraightenButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        apply.Click += (_, _) =>
        {
            double angle = slider.Value;
            applied = true;
            popup.IsOpen = false;
            if (Math.Abs(angle) < 0.05)
                View.ReplaceImage(baseImage, keepView: false);
            else
                ApplyEdit(ImageEdit.Rotate(baseImage, angle));
        };
        popup.Closed += (_, _) =>
        {
            if (!applied && _current != null) View.ReplaceImage(_current, keepView: false); // cancelled: show the real Picture again
            if (ReferenceEquals(_rotatePopup, popup)) _rotatePopup = null;
        };

        popup.Child = new Border
        {
            Background = SystemColors.ControlBrush,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(6),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Angle (degrees, + is clockwise)", Margin = new Thickness(0, 0, 0, 6) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { Nudge("−", -0.1, "−0.1°"), slider, Nudge("+", 0.1, "+0.1°"), box, new TextBlock { Text = "°", Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } },
                    },
                    hint,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { reset, apply } },
                },
            },
        };
        UsePlainFont(popup);
        _rotatePopup = popup;
        popup.IsOpen = true;
    }

    Popup? _rotatePopup;
}
