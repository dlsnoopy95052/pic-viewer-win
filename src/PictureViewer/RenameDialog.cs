using System.Windows;
using System.Windows.Controls;

namespace PictureViewer;

/// <summary>Asks for a new file name; the extension is left out of the initial selection.</summary>
public sealed class RenameDialog : Window
{
    readonly TextBox _box;

    public string NewName => _box.Text.Trim();

    public RenameDialog(string currentName)
    {
        Title = "Rename";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox { Text = currentName, Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(4, 3, 4, 3) };
        var ok = new Button { Content = "Rename", IsDefault = true, Width = 84, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 84 };
        ok.Click += (_, _) => DialogResult = true;

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                _box,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.Select(0, Path.GetFileNameWithoutExtension(currentName).Length);
        };
    }
}
