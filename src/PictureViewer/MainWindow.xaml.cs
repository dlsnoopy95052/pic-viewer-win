using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace PictureViewer;

public partial class MainWindow : Window
{
    const double ZoomStep = 1.25;
    static readonly TimeSpan SlideshowInterval = TimeSpan.FromSeconds(3);

    readonly FolderBrowser _folder = new();
    readonly DispatcherTimer _slideshow = new() { Interval = SlideshowInterval };

    BitmapSource? _current;
    int _loadTicket;        // newest load wins; stale ones are dropped
    int _infoTicket;
    bool _syncingStrip;     // true while code (not the user) changes the strip selection
    bool _stripDirty;       // folder changed while the strip was hidden
    bool _fullScreen;
    WindowState _stateBeforeFullScreen;

    public MainWindow()
    {
        InitializeComponent();
        View.ViewChanged += (_, _) => UpdateZoomUi();
        _slideshow.Tick += (_, _) =>
        {
            if (!_edited && _folder.Advance()) _ = ShowCurrentAsync();
        };
        InitAnnotations();
        InitEditing();
        UpdateUi();
    }

    /// <summary>Opens a Picture file, or the first Picture in a folder.</summary>
    public void OpenPath(string path)
    {
        if (!ConfirmDiscard()) return;
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
        RebuildStrip();
        _ = ShowCurrentAsync();
    }

    async Task ShowCurrentAsync()
    {
        if (_folder.Current is not { } path) return;
        int ticket = ++_loadTicket;
        UpdateUi();

        try
        {
            var (bitmap, animation) = await Task.Run(() => ImageLoader.LoadWithAnimation(path));
            if (ticket != _loadTicket) return;

            ResetEditing();
            _current = bitmap;
            View.SetImage(bitmap);
            if (animation != null) View.PlayAnimation(animation);
            EmptyText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (ticket != _loadTicket) return;
            ResetEditing();
            _current = null;
            View.SetImage(null);
            ShowMessage($"Can't open {Path.GetFileName(path)}\n{ex.Message}");
        }
        UpdateUi();
        RefreshInfoPanel();
    }

    void ShowMessage(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    void ShowError(string text) =>
        MessageBox.Show(this, text, "Picture Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);

    void Navigate(int delta)
    {
        if (!ConfirmDiscard()) return;
        if (_folder.Move(delta)) _ = ShowCurrentAsync();
    }

    void NavigateTo(int index)
    {
        if (!ConfirmDiscard())
        {
            SyncStrip(); // put the strip back on the picture that is still open
            return;
        }
        if (_folder.MoveTo(index)) _ = ShowCurrentAsync();
    }

    // ---- UI state ----

    void UpdateUi()
    {
        bool hasImage = _current != null;
        bool hasFile = _folder.Current != null;

        PrevButton.IsEnabled = _folder.HasPrevious;
        NextButton.IsEnabled = _folder.HasNext;
        PrevOverlay.Visibility = _folder.HasPrevious ? Visibility.Visible : Visibility.Collapsed;
        NextOverlay.Visibility = _folder.HasNext ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { ZoomInButton, ZoomOutButton, FitButton, ActualButton, CopyButton, PrintButton, RotateLeftButton, RotateRightButton })
            b.IsEnabled = hasImage;
        AnnotBar.IsEnabled = hasImage;
        UndoButton.IsEnabled = _undo.Count > 0;
        RenameButton.IsEnabled = DeleteButton.IsEnabled = hasFile;

        if (_folder.Files.Count < 2) StopSlideshow();
        SlideshowButton.IsEnabled = _folder.Files.Count > 1;

        if (_folder.Current is { } path)
        {
            string name = Path.GetFileName(path);
            Title = $"{(_edited ? "• " : "")}{name} - Picture Viewer";
            NameText.Text = name;
            PositionText.Text = $"{_folder.Index + 1} / {_folder.Files.Count}";
            InfoText.Text = hasImage ? $"{_current!.PixelWidth} × {_current.PixelHeight}  ·  {FileSize(path)}" : "";
        }
        else
        {
            Title = "Picture Viewer";
            NameText.Text = PositionText.Text = InfoText.Text = "";
            EmptyText.Visibility = Visibility.Visible;
        }
        SyncStrip();
        UpdateZoomUi();
    }

    void UpdateZoomUi() =>
        ZoomText.Text = View.HasImage ? $"{View.Zoom * 100:0}%" : "";

    static string FileSize(string path)
    {
        try { return ImageInfo.FormatBytes(new FileInfo(path).Length); }
        catch (IOException) { return ""; }
    }

    // ---- Info panel ----

    // The toolbar toggle owns the state; setting IsChecked runs OnInfoToggled.
    void SetInfoPanel(bool on) => InfoButton.IsChecked = on;

    void OnInfoToggled(object sender, RoutedEventArgs e)
    {
        InfoPanel.Visibility = InfoButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshInfoPanel();
    }

    void RefreshInfoPanel()
    {
        if (InfoPanel.Visibility != Visibility.Visible) return;
        if (_folder.Current is not { } path || _current is not { } bitmap)
        {
            InfoPanelText.Text = "";
            return;
        }

        int ticket = ++_infoTicket;
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        _ = Task.Run(() => ImageInfo.Lines(path, w, h)).ContinueWith(t =>
        {
            if (ticket == _infoTicket && t.IsCompletedSuccessfully)
                InfoPanelText.Text = string.Join("\n", t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Thumbnail strip ----

    void SetThumbnails(bool on) => ThumbsButton.IsChecked = on;

    void OnThumbsToggled(object sender, RoutedEventArgs e)
    {
        bool on = ThumbsButton.IsChecked == true;
        Strip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && _stripDirty) RebuildStrip();
        SyncStrip();
    }

    void RebuildStrip()
    {
        if (Strip.Visibility != Visibility.Visible)
        {
            _stripDirty = true;
            return;
        }
        _stripDirty = false;
        _syncingStrip = true;
        Strip.ItemsSource = _folder.Files.Select(f => new ThumbItem(f)).ToList();
        _syncingStrip = false;
        SyncStrip();
    }

    void SyncStrip()
    {
        if (Strip.Visibility != Visibility.Visible || _folder.Index < 0 || Strip.Items.Count == 0) return;
        _syncingStrip = true;
        Strip.SelectedIndex = _folder.Index;
        Strip.ScrollIntoView(Strip.SelectedItem);
        _syncingStrip = false;
    }

    void OnStripSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingStrip && Strip.SelectedIndex >= 0) NavigateTo(Strip.SelectedIndex);
    }

    /// <summary>The strip scrolls sideways, so the wheel moves it left / right.</summary>
    void OnStripWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer(Strip) is { } sv)
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    // ---- Slideshow / full screen ----

    void ToggleSlideshow()
    {
        if (_slideshow.IsEnabled) StopSlideshow();
        else if (_folder.Files.Count > 1)
        {
            _slideshow.Start();
            SlideshowButton.Content = "";
            SlideshowButton.ToolTip = "Stop slideshow (Space)";
        }
    }

    void StopSlideshow()
    {
        _slideshow.Stop();
        SlideshowButton.Content = "";
        SlideshowButton.ToolTip = "Start slideshow (Space)";
    }

    void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen)
        {
            _stateBeforeFullScreen = WindowState;
            WindowState = WindowState.Normal; // lets the maximized window cover the taskbar
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullScreen;
        }
        Toolbar.Visibility = StatusBar.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- File actions ----

    public void ShowOpenDialog() => OpenDialog();

    void OpenDialog()
    {
        var dlg = new OpenFileDialog { Filter = ImageFormats.DialogFilter, Title = "Open picture" };
        if (_folder.Current is { } cur) dlg.InitialDirectory = Path.GetDirectoryName(cur);
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
    }

    void CopyImage()
    {
        if (_current is null) return;
        // What you see: annotations and View rotation included.
        PutOnClipboard(AnnotationRenderer.Flatten(_current, View.Layer.Items, View.QuarterTurns));
    }

    /// <summary>Puts a picture on the clipboard as a bitmap plus PNG (which keeps transparency). False if the clipboard was busy.</summary>
    bool PutOnClipboard(BitmapSource picture)
    {
        try
        {
            var data = new DataObject();
            data.SetImage(picture);

            // The plain bitmap format has no transparency; PNG-aware apps (browsers, Office) use this one.
            var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            encoder.Save(png);
            data.SetData("PNG", png);

            Clipboard.SetDataObject(data, copy: true); // copy: true keeps it after the app exits
            return true;
        }
        catch (COMException)
        {
            ShowError("The clipboard is in use by another program. Try again.");
            return false;
        }
    }

    /// <summary>Prints what is on screen: annotations and View rotation included.</summary>
    void PrintCurrent()
    {
        if (_folder.Current is not { } path || CurrentPicture() is not { } picture) return;
        View.Layer.CropMode = false;
        new PrintWindow(picture, Path.GetFileName(path)) { Owner = this }.ShowDialog();
    }

    void RenameCurrent()
    {
        if (_folder.Current is not { } path) return;

        var dialog = new RenameDialog(Path.GetFileName(path)) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        string name = dialog.NewName;
        if (name.Length == 0 || name == Path.GetFileName(path)) return;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowError("A file name can't contain any of these characters:\n\\ / : * ? \" < > |");
            return;
        }

        string dest = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (File.Exists(dest) && !string.Equals(dest, path, StringComparison.OrdinalIgnoreCase))
        {
            ShowError("A file with that name already exists.");
            return;
        }

        try
        {
            File.Move(path, dest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return;
        }

        _folder.OpenFile(dest);
        RebuildStrip();
        UpdateUi();
        RefreshInfoPanel();
    }

    void DeleteCurrent()
    {
        if (_folder.Current is not { } path) return;
        if (!ConfirmDiscard()) return;

        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return;
        }

        _folder.Refresh();
        RebuildStrip();
        if (_folder.Current is null)
        {
            _current = null;
            View.SetImage(null);
            ShowMessage("No more pictures in this folder");
            UpdateUi();
            RefreshInfoPanel();
        }
        else
        {
            _ = ShowCurrentAsync();
        }
    }

    void RevealCurrent()
    {
        if (_folder.Current is { } path)
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // ---- Event handlers ----

    void OnOpenClick(object sender, RoutedEventArgs e) => OpenDialog();
    void OnPrevClick(object sender, RoutedEventArgs e) => Navigate(-1);
    void OnNextClick(object sender, RoutedEventArgs e) => Navigate(1);
    void OnZoomInClick(object sender, RoutedEventArgs e) => View.ZoomBy(ZoomStep);
    void OnZoomOutClick(object sender, RoutedEventArgs e) => View.ZoomBy(1 / ZoomStep);
    void OnFitClick(object sender, RoutedEventArgs e) => View.FitToWindow();
    void OnActualClick(object sender, RoutedEventArgs e) => View.ActualSize();
    void OnSlideshowClick(object sender, RoutedEventArgs e) => ToggleSlideshow();
    void OnInfoMenuClick(object sender, RoutedEventArgs e) => SetInfoPanel(InfoPanel.Visibility != Visibility.Visible);
    void OnThumbsMenuClick(object sender, RoutedEventArgs e) => SetThumbnails(Strip.Visibility != Visibility.Visible);
    void OnCopyClick(object sender, RoutedEventArgs e) => CopyImage();
    void OnRenameClick(object sender, RoutedEventArgs e) => RenameCurrent();
    void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteCurrent();
    void OnRevealClick(object sender, RoutedEventArgs e) => RevealCurrent();
    void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();
    /// <summary>Set by the tray's Quit; otherwise closing the window only hides it (the tray keeps running).</summary>
    public bool Quitting { get; set; }

    public event EventHandler? HiddenToTray;

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            Quitting = false;
            return;
        }
        if (Quitting) return;

        e.Cancel = true;
        ClearPicture();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the empty state, so a window that comes back later doesn't hold on to the old picture.</summary>
    void ClearPicture()
    {
        StopSlideshow();
        _loadTicket++; // a load still in flight must not bring the picture back
        _folder.Clear();
        ResetEditing();
        _current = null;
        View.SetImage(null);
        RebuildStrip();
        InfoPanelText.Text = "";
        ShowMessage("Drop a picture or folder here, or press Ctrl+O");
        UpdateUi();
    }

    public void StopSlideshowForCapture() => StopSlideshow();

    /// <summary>
    /// A finished Capture. With a Picture already open it stays put (the Capture is on the clipboard to paste onto it);
    /// otherwise the Capture opens like any other Picture.
    /// </summary>
    public void HandleCapture(BitmapSource capture, bool copyToClipboard)
    {
        bool hadPicture = _folder.Current != null;
        if (copyToClipboard && PutOnClipboard(capture) && hadPicture)
        {
            ShowToast("Capture copied — press Ctrl+V to paste");
            return;
        }

        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "PictureViewerCaptures");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"Capture {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            ImageEdit.Save(capture, path);
            OpenPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
    }

    // ---- Toast ----

    int _toastTicket;

    /// <summary>A brief message over the picture that goes away by itself.</summary>
    void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        int ticket = ++_toastTicket;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ticket == _toastTicket) Toast.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }
    void OnClosed(object? sender, EventArgs e) => _slideshow.Stop();
    void OnRotateLeftClick(object sender, RoutedEventArgs e) => View.RotateBy(-1);
    void OnRotateRightClick(object sender, RoutedEventArgs e) => View.RotateBy(1);
    void OnAboutClick(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
    void OnPrintClick(object sender, RoutedEventArgs e) => PrintCurrent();
    void OnPasteClick(object sender, RoutedEventArgs e) => PasteFromClipboard();
    void OnUndoClick(object sender, RoutedEventArgs e) => Undo();

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
            case Key.C when ctrl: CopyImage(); break;
            case Key.I when ctrl: SetInfoPanel(InfoPanel.Visibility != Visibility.Visible); break;
            case Key.T when ctrl: SetThumbnails(Strip.Visibility != Visibility.Visible); break;
            case Key.R when ctrl: RevealCurrent(); break;
            case Key.S when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift): SaveCopy(); break;
            case Key.S when ctrl: SaveInPlace(); break;
            case Key.K when ctrl: CropAction(); break;
            case Key.P when ctrl: PrintCurrent(); break;
            case Key.V when ctrl: PasteFromClipboard(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.OemOpenBrackets when ctrl: View.RotateBy(-1); break;
            case Key.OemCloseBrackets when ctrl: View.RotateBy(1); break;
            case Key.F2: RenameCurrent(); break;
            case Key.Delete:
                // An annotation selected on the picture goes first; otherwise it's the file.
                if (View.Layer.Selected != null) View.Layer.DeleteSelected();
                else DeleteCurrent();
                break;
            case Key.F11: ToggleFullScreen(); break;
            case Key.Escape:
                if (_fullScreen) ToggleFullScreen();
                else if (View.Layer.CropMode) View.Layer.CropMode = false;
                else if (View.Layer.Tool != AnnotationTool.None) View.Layer.Tool = AnnotationTool.None;
                else if (View.Layer.SelectedIndex != null) View.Layer.SelectedIndex = null;
                else if (_slideshow.IsEnabled) StopSlideshow();
                else e.Handled = false;
                break;
            case Key.Space: ToggleSlideshow(); break;
            case Key.Left or Key.PageUp: Navigate(-1); break;
            case Key.Right or Key.PageDown: Navigate(1); break;
            case Key.Home: NavigateTo(0); break;
            case Key.End: NavigateTo(_folder.Files.Count - 1); break;
            case Key.OemPlus or Key.Add: View.ZoomBy(ZoomStep); break;
            case Key.OemMinus or Key.Subtract: View.ZoomBy(1 / ZoomStep); break;
            case Key.D0 or Key.NumPad0: View.FitToWindow(); break;
            case Key.D1 or Key.NumPad1: View.ActualSize(); break;
            default: e.Handled = false; break;
        }
    }
}
