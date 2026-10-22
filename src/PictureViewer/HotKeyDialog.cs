using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PictureViewer;

/// <summary>Asks for a new capture hot key: you just press the combination you want.</summary>
public sealed class HotKeyDialog : Window
{
    readonly TextBlock _combo = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _note = new() { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    readonly Button _ok = new() { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    HotKeySpec _chosen;

    /// <summary>The combination to use when the dialog returns true.</summary>
    public HotKeySpec Chosen => _chosen;

    public HotKeyDialog(HotKeySpec current)
    {
        _chosen = current;

        Title = "Capture Hot Key";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var reset = new Button { Content = "Reset to default", Padding = new Thickness(10, 4, 10, 4), Focusable = false };
        reset.Click += (_, _) => Show(HotKeySpec.Default);
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        _ok.Click += (_, _) => DialogResult = true;

        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        DockPanel.SetDock(reset, Dock.Left);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(_ok, Dock.Right);
        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "Press the key combination you want to use to capture a screen area.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) },
                new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Child = _combo },
                _note,
                buttons,
            },
        };

        PreviewKeyDown += OnKeyDown;
        Show(current);
        Loaded += (_, _) => Focus();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) return; // let Esc cancel the dialog

        e.Handled = true; // this window is only listening: don't let Alt open menus, Tab move focus, and so on
        if (HotKeySpec.FromKeyPress(key, Keyboard.Modifiers) is { } spec) Show(spec);
    }

    void Show(HotKeySpec spec)
    {
        _chosen = spec;
        _combo.Text = spec.Format();
        bool allowed = spec.IsAllowed;
        _ok.IsEnabled = allowed;
        _note.Text = allowed
            ? "Press another combination to change it."
            : "Include Ctrl, Alt, Shift or Win with the key (Print Screen can be used on its own).";
    }
}
