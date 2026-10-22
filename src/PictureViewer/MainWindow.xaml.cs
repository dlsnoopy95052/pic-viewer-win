using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PictureViewer;

public partial class MainWindow : Window
{
    const double ZoomStep = 1.25;

    readonly FolderBrowser _folder = new();
    int _loadTicket;        // newest load wins; stale ones are dropped
    int _pixelWidth, _pixelHeight;

    public MainWindow()
    {
        InitializeComponent();
        View.ViewChanged += (_, _) => UpdateZoomUi();
        UpdateUi();
    }

    /// <summary>Opens a Picture file, or the first Picture in a folder.</summary>
    public void OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            if (!_folder.OpenFolder(path))
            {
                ShowMessage("No pictures in this folder");
                return;
            }
        }
        else if (File.Exists(path))
        {
            _folder.OpenFile(path);
        }
        else
        {
            ShowMessage($"Can't find {path}");
            return;
        }
        _ = ShowCurrentAsync();
    }

    async Task ShowCurrentAsync()
    {
        if (_folder.Current is not { } path) return;
        int ticket = ++_loadTicket;
        UpdateUi();

        try
        {
            var bitmap = await Task.Run(() => ImageLoader.Load(path));
            if (ticket != _loadTicket) return;

            _pixelWidth = bitmap.PixelWidth;
            _pixelHeight = bitmap.PixelHeight;
            View.SetImage(bitmap);
            EmptyText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (ticket != _loadTicket) return;
            _pixelWidth = _pixelHeight = 0;
            View.SetImage(null);
            ShowMessage($"Can't open {Path.GetFileName(path)}\n{ex.Message}");
        }
        UpdateUi();
    }

    void ShowMessage(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    void Navigate(int delta)
    {
        if (_folder.Move(delta)) _ = ShowCurrentAsync();
    }

    void UpdateUi()
    {
        bool hasImage = View.HasImage;

        PrevButton.IsEnabled = _folder.HasPrevious;
        NextButton.IsEnabled = _folder.HasNext;
        PrevOverlay.Visibility = _folder.HasPrevious ? Visibility.Visible : Visibility.Collapsed;
        NextOverlay.Visibility = _folder.HasNext ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { ZoomInButton, ZoomOutButton, FitButton, ActualButton })
            b.IsEnabled = hasImage;

        if (_folder.Current is { } path)
        {
            string name = Path.GetFileName(path);
            Title = $"{name} - Picture Viewer";
            NameText.Text = name;
            PositionText.Text = $"{_folder.Index + 1} / {_folder.Files.Count}";
            InfoText.Text = hasImage ? $"{_pixelWidth} × {_pixelHeight}  ·  {FormatSize(path)}" : "";
        }
        else
        {
            Title = "Picture Viewer";
            NameText.Text = PositionText.Text = InfoText.Text = "";
            EmptyText.Visibility = Visibility.Visible;
        }
        UpdateZoomUi();
    }

    void UpdateZoomUi() =>
        ZoomText.Text = View.HasImage ? $"{View.Zoom * 100:0}%" : "";

    static string FormatSize(string path)
    {
        try
        {
            double bytes = new FileInfo(path).Length;
            string[] units = ["B", "KB", "MB", "GB"];
            int u = 0;
            while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
            return u == 0 ? $"{bytes:0} {units[u]}" : $"{bytes:0.#} {units[u]}";
        }
        catch (IOException)
        {
            return "";
        }
    }

    void OpenDialog()
    {
        var dlg = new OpenFileDialog { Filter = ImageFormats.DialogFilter, Title = "Open picture" };
        if (_folder.Current is { } cur) dlg.InitialDirectory = Path.GetDirectoryName(cur);
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
    }

    void OnOpenClick(object sender, RoutedEventArgs e) => OpenDialog();
    void OnPrevClick(object sender, RoutedEventArgs e) => Navigate(-1);
    void OnNextClick(object sender, RoutedEventArgs e) => Navigate(1);
    void OnZoomInClick(object sender, RoutedEventArgs e) => View.ZoomBy(ZoomStep);
    void OnZoomOutClick(object sender, RoutedEventArgs e) => View.ZoomBy(1 / ZoomStep);
    void OnFitClick(object sender, RoutedEventArgs e) => View.FitToWindow();
    void OnActualClick(object sender, RoutedEventArgs e) => View.ActualSize();

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            OpenPath(paths[0]);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.O when ctrl: OpenDialog(); break;
            case Key.Left or Key.PageUp: Navigate(-1); break;
            case Key.Right or Key.PageDown or Key.Space: Navigate(1); break;
            case Key.Home: if (_folder.MoveTo(0)) _ = ShowCurrentAsync(); break;
            case Key.End: if (_folder.MoveTo(_folder.Files.Count - 1)) _ = ShowCurrentAsync(); break;
            case Key.OemPlus or Key.Add: View.ZoomBy(ZoomStep); break;
            case Key.OemMinus or Key.Subtract: View.ZoomBy(1 / ZoomStep); break;
            case Key.D0 or Key.NumPad0: View.FitToWindow(); break;
            case Key.D1 or Key.NumPad1: View.ActualSize(); break;
            default: e.Handled = false; break;
        }
    }
}
