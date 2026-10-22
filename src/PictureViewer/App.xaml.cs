using System.Reflection;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PictureViewer;

/// <summary>
/// Runs as a tray app: no window until a picture is opened or the user asks for one, so starting at login
/// stays out of the way. The capture hot key keeps working while the window is closed.
/// </summary>
public partial class App : Application
{
    /// <summary>`PictureViewer.exe --quit` asks the running copy to exit (used by the installer).</summary>
    public const string QuitFlag = "--quit";

    /// <summary>`PictureViewer.exe --remove-autostart` removes the Open at Login entry and exits (used by the uninstaller).</summary>
    public const string RemoveAutoStartFlag = "--remove-autostart";

    readonly Settings _settings = Settings.Load();

    MainWindow _window = null!;
    Forms.NotifyIcon? _tray;
    GlobalHotKey? _hotKey;
    Forms.ToolStripMenuItem? _captureItem, _copyItem, _loginItem;
    bool _toldAboutTray;

    void OnStartup(object sender, StartupEventArgs e)
    {
        string? path = e.Args.FirstOrDefault(a => !a.StartsWith("--"));
        bool background = e.Args.Contains(AutoStart.BackgroundFlag);
        bool quit = e.Args.Contains(QuitFlag);

        if (e.Args.Contains(RemoveAutoStartFlag))
        {
            // Run by the uninstaller: "Open at Login" may have been switched on from the tray menu, which it doesn't know about.
            try { AutoStart.Set(false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            Shutdown();
            return;
        }

        if (!SingleInstance.TryBecomePrimary())
        {
            SingleInstance.SendToPrimary(quit ? QuitFlag : path ?? "");
            Shutdown();
            return;
        }
        if (quit)
        {
            Shutdown(); // nothing running to quit
            return;
        }
        SingleInstance.StartServer(message => Dispatcher.Invoke(() => HandleRemote(message)));

        _window = new MainWindow();
        _window.HiddenToTray += (_, _) => TellAboutTray();
        BuildTray();
        RegisterHotKey();

        if (path != null)
        {
            ShowMainWindow();
            _window.OpenPath(path);
        }
        else if (!background)
        {
            ShowMainWindow();
        }
    }

    void HandleRemote(string path)
    {
        if (path == QuitFlag)
        {
            Quit(); // the installer asking us to make room for an update or uninstall
            return;
        }
        ShowMainWindow();
        if (path.Length > 0) _window.OpenPath(path);
    }

    // ---- Window ----

    void ShowMainWindow()
    {
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;  // the usual trick to get in front of other windows...
        _window.Topmost = false; // ...without staying there
        _window.Focus();
    }

    void Quit()
    {
        _window.Quitting = true;
        _window.Close();
        if (_window.IsVisible || !_window.Quitting) return; // the unsaved-changes prompt was answered "no"

        _hotKey?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Shutdown();
    }

    // ---- Capture ----

    async void CaptureArea()
    {
        if (ScreenCapture.IsActive) return;
        _window.StopSlideshowForCapture();

        // The window, if it's showing, stays where it is so it can be part of the shot.
        var capture = await ScreenCapture.SelectAreaAsync();
        if (capture is null) return; // cancelled

        ShowMainWindow();
        _window.HandleCapture(capture, _settings.CopyCaptures);
    }

    /// <summary>Registers the saved hot key. An unusable one (taken by another program, or no longer valid) is reported.</summary>
    void RegisterHotKey()
    {
        var spec = _settings.HotKey.IsAllowed ? _settings.HotKey : HotKeySpec.Default;
        if (!TryRegister(spec))
        {
            _tray?.ShowBalloonTip(6000, "Picture Viewer", $"{spec.Format()} is already used by another program, so the capture hot key is off. Pick another with Set Capture Hot Key in the tray menu.", Forms.ToolTipIcon.Warning);
        }
        ShowHotKeyInMenu();
    }

    bool TryRegister(HotKeySpec spec)
    {
        _hotKey?.Dispose();
        var hotKey = new GlobalHotKey(spec.Modifiers, spec.VirtualKey);
        if (!hotKey.Registered)
        {
            hotKey.Dispose();
            _hotKey = null;
            return false;
        }
        hotKey.Pressed += (_, _) => CaptureArea();
        _hotKey = hotKey;
        return true;
    }

    void ChangeHotKey()
    {
        var current = _settings.HotKey;
        var dialog = new HotKeyDialog(current);
        if (dialog.ShowDialog() != true || dialog.Chosen == current) return;

        var chosen = dialog.Chosen;
        if (TryRegister(chosen))
        {
            _settings.HotKey = chosen;
            _settings.Save();
        }
        else
        {
            // Put the old one back: a failed change must not leave capture without a hot key.
            TryRegister(current);
            MessageBox.Show($"{chosen.Format()} is already used by another program. The hot key stays {current.Format()}.",
                "Capture Hot Key", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ShowHotKeyInMenu();
    }

    void ShowHotKeyInMenu()
    {
        if (_captureItem != null)
            _captureItem.ShortcutKeyDisplayString = _hotKey != null ? _settings.HotKey.Format() : "off";
    }

    // ---- Tray ----

    void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        _captureItem = new Forms.ToolStripMenuItem("Capture Screen Area", null, (_, _) => CaptureArea());
        menu.Items.Add(_captureItem);
        menu.Items.Add("Show Picture Viewer", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Open…", null, (_, _) =>
        {
            ShowMainWindow();
            _window.ShowOpenDialog();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add("Set Capture Hot Key…", null, (_, _) => ChangeHotKey());
        _copyItem = new Forms.ToolStripMenuItem("Copy Captures to Clipboard") { Checked = _settings.CopyCaptures, CheckOnClick = true };
        _copyItem.CheckedChanged += (_, _) =>
        {
            _settings.CopyCaptures = _copyItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_copyItem);

        _loginItem = new Forms.ToolStripMenuItem("Open at Login") { CheckOnClick = true };
        _loginItem.Click += (_, _) =>
        {
            try
            {
                AutoStart.Set(_loginItem.Checked);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _loginItem.Checked = !_loginItem.Checked;
                MessageBox.Show(ex.Message, "Open at Login", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        menu.Items.Add(_loginItem);
        menu.Opening += (_, _) => _loginItem.Checked = AutoStart.IsEnabled; // may have been changed elsewhere

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("About Picture Viewer", null, (_, _) => ShowAbout());
        menu.Items.Add("Quit Picture Viewer", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Picture Viewer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowMainWindow();
        };
    }

    static System.Drawing.Icon LoadIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
        return new System.Drawing.Icon(resource.Stream, new System.Drawing.Size(32, 32));
    }

    void TellAboutTray()
    {
        if (_toldAboutTray) return;
        _toldAboutTray = true;
        _tray?.ShowBalloonTip(4000, "Picture Viewer is still running", $"Press {_settings.HotKey.Format()} to capture a screen area, or use the tray icon. Quit from its menu.", Forms.ToolTipIcon.Info);
    }

    void ShowAbout() => new AboutWindow(_hotKey != null ? _settings.HotKey.Format() : null).ShowDialog();
}
