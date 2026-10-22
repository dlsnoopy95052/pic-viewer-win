using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PictureViewer;

/// <summary>Multi-line text entry for text annotations. Enter adds a new line; Ctrl+Enter confirms.</summary>
public sealed class TextInputDialog : Window
{
    readonly TextBox _box;

    public string Value => _box.Text;

    public TextInputDialog(string title, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox
        {
            Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 110, Padding = new Thickness(4, 3, 4, 3),
        };
        var ok = new Button { Content = "OK", Width = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 84, IsCancel = true };
        ok.Click += (_, _) => DialogResult = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DialogResult = true;
                e.Handled = true;
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Press Enter for a new line, Ctrl+Enter to finish.", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) },
                _box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel },
                },
            },
        };

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }
}
