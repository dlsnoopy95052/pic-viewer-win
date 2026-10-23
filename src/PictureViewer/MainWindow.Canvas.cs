using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PictureViewer;

// Growing the canvas around the Picture, so annotations and pasted pictures can go outside the original.
public partial class MainWindow
{
    const int MaxCanvasSide = 30000;
    const int CanvasPreviewSide = 1400;

    void OnCanvasClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        View.Layer.CropMode = false;
        View.Layer.Tool = AnnotationTool.None;
        FlattenPending();
        var baseImage = _current!;
        var working = ImageEdit.Downscale(baseImage, CanvasPreviewSide); // the preview is drawn from a smaller copy
        double scale = (double)working.PixelWidth / baseImage.PixelWidth;
        bool applied = false, linking = false;

        TextBox Field() => new() { Text = "0", Width = 64, Padding = new Thickness(3, 2, 3, 2), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        var left = Field();
        var top = Field();
        var right = Field();
        var bottom = Field();
        var same = new CheckBox { Content = "Same on every side", Margin = new Thickness(0, 8, 0, 0), Focusable = false };
        var sizeText = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.Gray };

        RadioButton Fill(string text, bool selected) => new() { Content = text, IsChecked = selected, GroupName = "canvasfill", Margin = new Thickness(0, 0, 12, 0), Focusable = false };
        var transparent = Fill("Transparent", false);
        var white = Fill("White", true);
        var black = Fill("Black", false);

        int Value(TextBox box) =>
            int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? Math.Min(v, MaxCanvasSide) : 0;

        Color? FillColor() => transparent.IsChecked == true ? null : black.IsChecked == true ? Colors.Black : Colors.White;

        // Keeps the new picture inside what can be drawn.
        bool Fits(int l, int t, int r, int b) =>
            baseImage.PixelWidth + l + r <= MaxCanvasSide && baseImage.PixelHeight + t + b <= MaxCanvasSide;

        void Preview()
        {
            int l = Value(left), t = Value(top), r = Value(right), b = Value(bottom);
            if (!Fits(l, t, r, b))
            {
                sizeText.Text = $"Too large (the limit is {MaxCanvasSide} pixels per side).";
                return;
            }
            sizeText.Text = $"New size: {baseImage.PixelWidth + l + r} × {baseImage.PixelHeight + t + b} px";
            View.ReplaceImage(ImageEdit.Expand(working,
                (int)Math.Round(l * scale), (int)Math.Round(t * scale), (int)Math.Round(r * scale), (int)Math.Round(b * scale), FillColor()), keepView: false);
        }

        foreach (var box in new[] { left, top, right, bottom })
        {
            var current = box;
            box.TextChanged += (_, _) =>
            {
                if (same.IsChecked == true && !linking)
                {
                    linking = true;
                    foreach (var other in new[] { left, top, right, bottom }) if (!ReferenceEquals(other, current)) other.Text = current.Text;
                    linking = false;
                }
                if (!linking) Preview();
            };
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
        }
        foreach (var option in new[] { transparent, white, black }) option.Click += (_, _) => Preview();

        var reset = new Button { Content = "Reset", Width = 72, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        reset.Click += (_, _) =>
        {
            linking = true;
            left.Text = top.Text = right.Text = bottom.Text = "0";
            linking = false;
            Preview();
        };
        var apply = new Button { Content = "Apply", Width = 72, IsDefault = true, Focusable = false };

        var popup = new Popup
        {
            PlacementTarget = CanvasButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        apply.Click += (_, _) =>
        {
            int l = Value(left), t = Value(top), r = Value(right), b = Value(bottom);
            if (l + t + r + b == 0 || !Fits(l, t, r, b)) return;
            var fill = FillColor();
            applied = true;
            popup.IsOpen = false;
            ApplyEdit(ImageEdit.Expand(baseImage, l, t, r, b, fill));
        };
        popup.Closed += (_, _) =>
        {
            if (!applied && _current != null) View.ReplaceImage(_current, keepView: false); // cancelled: show the real Picture again
            if (ReferenceEquals(_canvasPopup, popup)) _canvasPopup = null;
        };

        var fields = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        fields.RowDefinitions.Add(new RowDefinition());
        fields.RowDefinitions.Add(new RowDefinition());
        void Place(string label, TextBox box, int row, int column)
        {
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            box.Margin = new Thickness(0, 0, 16, 6);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, column + 1);
            fields.Children.Add(text);
            fields.Children.Add(box);
        }
        // Two columns of label + box: left / right on the first, top / bottom on the second.
        for (int i = 0; i < 4; i++) fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place("Left", left, 0, 0);
        Place("Right", right, 1, 0);
        Place("Top", top, 0, 2);
        Place("Bottom", bottom, 1, 2);

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
                    new TextBlock { Text = "Add space around the picture (pixels)", Margin = new Thickness(0, 0, 0, 8) },
                    fields,
                    same,
                    new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { new TextBlock { Text = "Fill  ", VerticalAlignment = VerticalAlignment.Center }, white, black, transparent } },
                    sizeText,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Children = { reset, apply } },
                },
            },
        };
        sizeText.Text = $"New size: {baseImage.PixelWidth} × {baseImage.PixelHeight} px";
        UsePlainFont(popup);
        AlignToRightEdge(popup);
        _canvasPopup = popup;
        popup.IsOpen = true;
    }

    Popup? _canvasPopup;
}
