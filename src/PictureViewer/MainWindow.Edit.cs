using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PictureViewer;

// Editing pixels (crop, flip, adjust), flattening, and saving.
public partial class MainWindow
{
    const string PasteFolderName = "PictureViewerPastes";

    Popup? _adjustPopup;
    int _adjustTicket;
    Size? _cropOutput;          // exact pixel size the crop is scaled to ("Custom size"); null = keep the cropped pixels
    bool _updatingCropUi;

    void InitEditing()
    {
        View.Layer.CropModeChanged += (_, _) =>
        {
            CropButton.IsChecked = View.Layer.CropMode;
            CropOptions.Visibility = View.Layer.CropMode ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    /// <summary>
    /// Replaces the Picture with <paramref name="edited"/>, which must already contain any annotations
    /// (they are cleared). The previous state goes on the undo stack.
    /// </summary>
    void ApplyEdit(BitmapSource edited, bool keepView = false)
    {
        PushUndo();
        _current = edited;
        View.ReplaceImage(edited, keepView);
        View.Layer.ReplaceItems([]);
        UpdateUi();
        RefreshInfoPanel();
    }

    /// <summary>Burns annotations and the View rotation into the pixels (the Flatten that edits start with).</summary>
    void FlattenPending()
    {
        if (_current is null) return;
        if (View.QuarterTurns == 0 && View.Layer.Items.Count == 0) return;
        ApplyEdit(AnnotationRenderer.Flatten(_current, View.Layer.Items, View.QuarterTurns));
    }

    // ---- Crop / flip ----

    void OnCropClick(object sender, RoutedEventArgs e) => CropAction();

    /// <summary>First use enters crop mode; using it again with an area selected applies the crop.</summary>
    void CropAction()
    {
        if (_current is null) return;
        var layer = View.Layer;

        if (!layer.CropMode)
        {
            FlattenPending();
            ApplyCropRatio(); // before entering, so the area starts out in the chosen shape
            layer.CropMode = true;
        }
        else if (layer.CropPixelRect is { } area)
        {
            layer.CropMode = false;
            var cropped = ImageEdit.Crop(_current, area);
            if (_cropOutput is { } size && ((int)size.Width != cropped.PixelWidth || (int)size.Height != cropped.PixelHeight))
                cropped = ImageEdit.Resize(cropped, (int)size.Width, (int)size.Height);
            ApplyEdit(cropped);
        }
        else
        {
            layer.CropMode = false;
        }
        CropButton.IsChecked = layer.CropMode;
    }

    void OnCropRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _updatingCropUi || CropRatioBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (tag == "custom")
        {
            AskCropSize();
            return;
        }
        _cropOutput = null;
        CropCustomItem.Content = "Custom size…";
        ApplyCropRatio();
    }

    /// <summary>Hands the layer the aspect ratio the toolbar currently describes.</summary>
    void ApplyCropRatio()
    {
        double? aspect = null;
        if (_current != null && CropRatioBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (tag == "original") aspect = (double)_current.PixelWidth / _current.PixelHeight;
            else if (tag == "custom") aspect = _cropOutput is { } size ? size.Width / size.Height : null;
            else if (tag.Split(':') is [var w, var h] && double.TryParse(w, out double wd) && double.TryParse(h, out double hd)) aspect = wd / hd;
        }
        View.Layer.CropAspect = aspect;
    }

    void OnCropSwapClick(object sender, RoutedEventArgs e)
    {
        if (View.Layer.CropAspect is not { } aspect) return;
        if (_cropOutput is { } size) SetCropOutput(new Size(size.Height, size.Width));
        else View.Layer.CropAspect = 1 / aspect;
    }

    void OnCropSizeClick(object sender, RoutedEventArgs e) => AskCropSize();

    void AskCropSize()
    {
        if (_current is null) return;
        var start = _cropOutput is { } s ? new Int32Rect(0, 0, (int)s.Width, (int)s.Height)
            : View.Layer.CropPixelRect ?? new Int32Rect(0, 0, _current.PixelWidth, _current.PixelHeight);
        var dialog = new CropSizeDialog(start.Width, start.Height) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SetCropOutput(new Size(dialog.PixelWidth, dialog.PixelHeight));
            return;
        }
        if (_cropOutput is null) SelectCropRatio("free"); // nothing was chosen, so don't sit on an empty "Custom"
    }

    void SetCropOutput(Size size)
    {
        _cropOutput = size;
        CropCustomItem.Content = $"{(int)size.Width} × {(int)size.Height}";
        SelectCropRatio("custom");
    }

    void SelectCropRatio(string tag)
    {
        _updatingCropUi = true;
        CropRatioBox.SelectedItem = CropRatioBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == tag);
        _updatingCropUi = false;
        ApplyCropRatio();
    }

    void OnFlipHClick(object sender, RoutedEventArgs e) => Flip(horizontal: true);
    void OnFlipVClick(object sender, RoutedEventArgs e) => Flip(horizontal: false);

    void Flip(bool horizontal)
    {
        if (_current is null) return;
        View.Layer.CropMode = false;
        FlattenPending();
        ApplyEdit(ImageEdit.Flip(_current!, horizontal), keepView: true);
    }

    // ---- Adjust ----

    void OnAdjustClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        View.Layer.CropMode = false;
        View.Layer.Tool = AnnotationTool.None;
        FlattenPending();
        var baseImage = _current!;

        var session = new AdjustSession(baseImage);
        bool applied = false;

        var brightness = SliderRow("Brightness", -0.5, 0.5, 0);
        var contrast = SliderRow("Contrast", 0.5, 1.5, 1);
        var saturation = SliderRow("Saturation", 0, 2, 1);

        double[] Values() => [brightness.Slider.Value, contrast.Slider.Value, saturation.Slider.Value];

        // Live preview: the Picture itself isn't touched until Apply, and stale renders are dropped.
        void Preview()
        {
            int ticket = ++_adjustTicket;
            var v = Values();
            _ = Task.Run(() => session.Render(v[0], v[1], v[2])).ContinueWith(t =>
            {
                if (ticket == _adjustTicket && t.IsCompletedSuccessfully && !applied)
                    View.ReplaceImage(t.Result, keepView: true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        foreach (var row in new[] { brightness, contrast, saturation })
            row.Slider.ValueChanged += (_, _) => Preview();

        var reset = new Button { Content = "Reset", Width = 72, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        reset.Click += (_, _) =>
        {
            brightness.Slider.Value = 0;
            contrast.Slider.Value = 1;
            saturation.Slider.Value = 1;
        };
        var apply = new Button { Content = "Apply", Width = 72, IsDefault = true, Focusable = false };

        var popup = new Popup
        {
            PlacementTarget = AdjustButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        apply.Click += (_, _) =>
        {
            applied = true;
            var v = Values();
            popup.IsOpen = false;
            ApplyEdit(session.Render(v[0], v[1], v[2]), keepView: true);
        };
        popup.Closed += (_, _) =>
        {
            _adjustTicket++;
            if (!applied && _current != null) View.ReplaceImage(_current, keepView: true); // cancelled: show the real Picture again
            if (ReferenceEquals(_adjustPopup, popup)) _adjustPopup = null;
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
                    brightness.Row, contrast.Row, saturation.Row,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { reset, apply } },
                },
            },
        };
        UsePlainFont(popup);
        _adjustPopup = popup;
        popup.IsOpen = true;
    }

    /// <summary>
    /// A popup takes its text style from the button it hangs from, which here is a big icon font; use the normal
    /// system font instead.
    /// </summary>
    static void UsePlainFont(Popup popup)
    {
        if (popup.Child is not DependencyObject root) return;
        TextElement.SetFontFamily(root, SystemFonts.MessageFontFamily);
        TextElement.SetFontSize(root, SystemFonts.MessageFontSize);
    }

    static (Slider Slider, UIElement Row) SliderRow(string label, double min, double max, double value)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 180, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(slider);
        return (slider, row);
    }

    // ---- Save ----

    static bool IsPasted(string path) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), PasteFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>What is on screen as pixels: annotations and View rotation included.</summary>
    BitmapSource? CurrentPicture() =>
        _current is null ? null : AnnotationRenderer.Flatten(_current, View.Layer.Items, View.QuarterTurns);

    void OnSaveClick(object sender, RoutedEventArgs e) => SaveInPlace();
    void OnSaveCopyClick(object sender, RoutedEventArgs e) => SaveCopy();

    void SaveInPlace()
    {
        if (_folder.Current is not { } path || _current is null) return;

        // Pasted pictures only exist in a temp folder, and some formats can't be written, so those become a copy.
        if (!ImageEdit.WritableExtensions.Contains(Path.GetExtension(path)) || IsPasted(path))
        {
            SaveCopy();
            return;
        }

        var answer = MessageBox.Show(this, "The original file will be replaced. Use Save a Copy to keep it.",
            $"Overwrite \"{Path.GetFileName(path)}\"?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            ImageEdit.Save(CurrentPicture()!, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError(ex.Message);
            return;
        }

        // Reload from disk so what you see is what was saved.
        _edited = false;
        RebuildStrip();
        _ = ShowCurrentAsync();
    }

    void SaveCopy()
    {
        if (_folder.Current is not { } source || _current is null) return;

        string sourceExt = Path.GetExtension(source).ToLowerInvariant();
        string ext = ImageEdit.WritableExtensions.Contains(sourceExt) ? sourceExt : ".png";
        bool pasted = IsPasted(source);
        string baseName = Path.GetFileNameWithoutExtension(source);

        var dialog = new SaveFileDialog
        {
            Title = "Save a copy",
            Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|TIFF|*.tif;*.tiff",
            FilterIndex = ext is ".jpg" or ".jpeg" ? 2 : ext is ".tif" or ".tiff" ? 3 : 1,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = pasted ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : Path.GetDirectoryName(source),
            FileName = pasted ? baseName + ext : baseName + " edited" + ext,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageEdit.Save(CurrentPicture()!, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError(ex.Message);
            return;
        }

        _edited = false; // the copy has everything, so leaving this picture loses nothing
        OpenPath(dialog.FileName);
    }
}
