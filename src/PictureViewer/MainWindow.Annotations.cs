using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

// Annotating: tool bar, styles, undo, text prompts and pasted layers.
public partial class MainWindow
{
    static readonly double[] WidthValues = [2, 4, 7, 12];
    static readonly string[] CuratedFonts = ["Arial", "Times New Roman", "Georgia", "Verdana", "Courier New", "Consolas", "Comic Sans MS", "Impact"];

    /// <summary>What Undo goes back to: the Picture and a copy of its annotations.</summary>
    sealed record Snapshot(BitmapSource? Image, List<Annotation> Items);

    readonly List<Snapshot> _undo = [];
    readonly Dictionary<AnnotationTool, ToggleButton> _toolButtons = [];
    bool _edited;
    bool _lastUndoWasStyle; // consecutive style changes share one undo step
    bool _syncingStyle;     // code, not the user, is changing the style controls
    bool _annotationsReady;

    void InitAnnotations()
    {
        _toolButtons[AnnotationTool.None] = ToolNone;
        _toolButtons[AnnotationTool.Line] = ToolLine;
        _toolButtons[AnnotationTool.Arrow] = ToolArrow;
        _toolButtons[AnnotationTool.Rect] = ToolRect;
        _toolButtons[AnnotationTool.Ellipse] = ToolEllipse;
        _toolButtons[AnnotationTool.Pen] = ToolPen;
        _toolButtons[AnnotationTool.Text] = ToolText;
        _toolButtons[AnnotationTool.Mosaic] = ToolMosaic;

        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FontBox.Items.Add("System");
        foreach (var name in CuratedFonts.Where(installed.Contains)) FontBox.Items.Add(name);
        FontBox.SelectedIndex = 0;

        var layer = View.Layer;
        ColorPick.Value = layer.Color;
        BackgroundPick.Value = null;
        ColorPick.ValueChanged += OnStyleChanged;
        BackgroundPick.ValueChanged += OnStyleChanged;

        layer.ToolChanged += (_, _) => OnToolChanged();
        layer.SelectionChanged += (_, _) => SyncStyleToSelection();
        layer.BeginChange += (_, _) => PushUndo();
        layer.ShapeCreated += (_, shape) =>
        {
            PushUndo();
            layer.Add(shape);
            layer.Tool = AnnotationTool.None; // one shape per pick of a tool
        };
        layer.TextRequested += (_, at) => AddText(at);
        layer.EditTextRequested += (_, index) => EditText(index);

        _annotationsReady = true;
        OnToolChanged();
    }

    // ---- Tools ----

    void OnToolClick(object sender, RoutedEventArgs e)
    {
        var tool = Enum.Parse<AnnotationTool>((string)((FrameworkElement)sender).Tag);
        var layer = View.Layer;
        layer.Tool = tool == AnnotationTool.None || layer.Tool == tool ? AnnotationTool.None : tool;
        OnToolChanged(); // a click on the active tool changes nothing in the layer, but the toggle must show it
    }

    void OnToolChanged()
    {
        foreach (var (tool, button) in _toolButtons)
            button.IsChecked = tool == View.Layer.Tool;
        UpdateTextOptionsVisibility();
    }

    /// <summary>The font and background controls only matter for text.</summary>
    void UpdateTextOptionsVisibility()
    {
        bool show = View.Layer.Tool == AnnotationTool.Text || View.Layer.Selected is { Tool: AnnotationTool.Text };
        TextOptions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Style ----

    double CurrentStrokeScale => _current is null ? 1 : AnnotationGeometry.StrokeScale(_current.PixelWidth, _current.PixelHeight);

    void OnStyleChanged(object? sender, EventArgs e)
    {
        if (!_annotationsReady || _syncingStyle) return;
        var layer = View.Layer;

        layer.Color = ColorPick.Value ?? layer.Color;
        layer.NominalWidth = WidthValues[Math.Max(0, WidthBox.SelectedIndex)];
        layer.Background = BackgroundPick.Value;
        layer.FontName = FontBox.SelectedIndex > 0 ? (string)FontBox.SelectedItem : "";
        layer.Bold = BoldButton.IsChecked == true;
        layer.Italic = ItalicButton.IsChecked == true;

        if (layer.Selected is not { } a || a.Tool == AnnotationTool.Image || _current is null) return;

        PushUndo(style: true);
        double scale = CurrentStrokeScale;
        a.Color = layer.Color;
        a.Width = layer.NominalWidth * scale;
        if (a.Tool == AnnotationTool.Text)
        {
            a.FontSize = layer.NominalWidth * 6 * scale;
            a.Background = layer.Background;
            a.FontName = layer.FontName;
            a.Bold = layer.Bold;
            a.Italic = layer.Italic;
        }
        layer.Refresh();
    }

    /// <summary>Shows the selected annotation's style in the controls.</summary>
    void SyncStyleToSelection()
    {
        _lastUndoWasStyle = false;
        UpdateTextOptionsVisibility();
        if (View.Layer.Selected is not { } a || a.Tool == AnnotationTool.Image || _current is null) return;

        var layer = View.Layer;
        double nominal = (a.Tool == AnnotationTool.Text ? a.FontSize / 6 : a.Width) / CurrentStrokeScale;
        int nearest = Enumerable.Range(0, WidthValues.Length).MinBy(i => Math.Abs(WidthValues[i] - nominal));

        _syncingStyle = true;
        ColorPick.Value = a.Color;
        WidthBox.SelectedIndex = nearest;
        layer.Color = a.Color;
        layer.NominalWidth = WidthValues[nearest];
        if (a.Tool == AnnotationTool.Text)
        {
            BackgroundPick.Value = a.Background;
            int font = a.FontName.Length == 0 ? 0 : FontBox.Items.IndexOf(a.FontName);
            FontBox.SelectedIndex = Math.Max(0, font);
            BoldButton.IsChecked = a.Bold;
            ItalicButton.IsChecked = a.Italic;
            layer.Background = a.Background;
            layer.FontName = a.FontName;
            layer.Bold = a.Bold;
            layer.Italic = a.Italic;
        }
        _syncingStyle = false;
    }

    // ---- Text ----

    void AddText(Point at)
    {
        var layer = View.Layer;
        var dialog = new TextInputDialog("Add text", "") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value.Length == 0)
        {
            layer.Tool = AnnotationTool.None;
            return;
        }
        PushUndo();
        layer.Add(layer.NewText(at, dialog.Value));
        layer.Tool = AnnotationTool.None;
    }

    void EditText(int index)
    {
        var layer = View.Layer;
        if (index >= layer.Items.Count) return;
        var dialog = new TextInputDialog("Edit text", layer.Items[index].Text) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value.Length == 0 || dialog.Value == layer.Items[index].Text) return;
        PushUndo();
        layer.Items[index].Text = dialog.Value;
        layer.Refresh();
    }

    // ---- Undo and unsaved changes ----

    void PushUndo(bool style = false)
    {
        if (_current is null) return;
        if (style && _lastUndoWasStyle) return;
        _lastUndoWasStyle = style;
        _undo.Add(new Snapshot(_current, [.. View.Layer.Items.Select(a => a.Clone())]));
        SetEdited(true);
    }

    void Undo()
    {
        if (_undo.Count == 0)
        {
            SystemSounds.Beep.Play();
            return;
        }
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _lastUndoWasStyle = false;
        View.Layer.ReplaceItems(previous.Items.Select(a => a.Clone()));
        SetEdited(_undo.Count > 0);
    }

    void SetEdited(bool edited)
    {
        _edited = edited;
        UpdateUi();
    }

    /// <summary>A new Picture starts with a clean slate.</summary>
    void ResetEditing()
    {
        _undo.Clear();
        _edited = false;
        _lastUndoWasStyle = false;
        View.Layer.ReplaceItems([]);
        View.Layer.Tool = AnnotationTool.None;
    }

    bool ConfirmDiscard()
    {
        if (!_edited) return true;
        return MessageBox.Show(this, "This picture has annotations that have not been saved.", "Discard unsaved edits?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    // ---- Paste ----

    /// <summary>Ctrl+V: opens a copied file, adds a copied picture as a layer, or opens it if nothing is open.</summary>
    void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string? file in Clipboard.GetFileDropList())
                {
                    if (file != null && (File.Exists(file) || Directory.Exists(file)))
                    {
                        OpenPath(file);
                        return;
                    }
                }
            }

            if (ReadClipboardImage() is not { } picture)
            {
                SystemSounds.Beep.Play();
                return;
            }
            if (_current != null) AddPastedLayer(picture);
            else OpenPastedPicture(picture);
        }
        catch (COMException)
        {
            ShowError("The clipboard is in use by another program. Try again.");
        }
    }

    static BitmapSource? ReadClipboardImage()
    {
        // The PNG format keeps transparency; the plain bitmap format usually doesn't.
        if (Clipboard.GetData("PNG") is MemoryStream png)
        {
            try
            {
                var frame = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                frame.Freeze();
                return frame;
            }
            catch (Exception)
            {
                // Not a valid PNG after all: fall back to the bitmap format.
            }
        }
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        return null;
    }

    void AddPastedLayer(BitmapSource picture)
    {
        if (_current is null || picture.PixelWidth == 0 || picture.PixelHeight == 0) return;

        // Start at a sensible size: never larger than 60% of the picture underneath.
        double k = Math.Min(1, Math.Min(_current.PixelWidth * 0.6 / picture.PixelWidth, _current.PixelHeight * 0.6 / picture.PixelHeight));
        double w = picture.PixelWidth * k, h = picture.PixelHeight * k;
        var center = View.VisibleCenterInImage() ?? new Point(_current.PixelWidth / 2.0, _current.PixelHeight / 2.0);

        View.Layer.Tool = AnnotationTool.None;
        PushUndo();
        View.Layer.Add(new Annotation
        {
            Tool = AnnotationTool.Image,
            Points = [new Point(center.X - w / 2, center.Y - h / 2), new Point(center.X + w / 2, center.Y + h / 2)],
            Color = Colors.Transparent,
            Image = picture,
        });
    }

    /// <summary>Nothing is open, so the pasted picture becomes a file in a temp folder and opens like any other.</summary>
    void OpenPastedPicture(BitmapSource picture)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "PictureViewerPastes");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"Pasted {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using (var stream = File.Create(path)) encoder.Save(stream);
            OpenPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
    }
}
