using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>The About box: icon, name, version and build date / time.</summary>
public sealed class AboutWindow : Window
{
    /// <param name="hotKey">The current capture hot key, shown as a hint; null leaves it out.</param>
    public AboutWindow(string? hotKey = null)
    {
        Title = "About Picture Viewer";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        details.Children.Add(new TextBlock { Text = "Picture Viewer", FontSize = 22, FontWeight = FontWeights.SemiBold });
        details.Children.Add(Line("Version", AboutInfo.Version));
        details.Children.Add(Line("Built", AboutInfo.BuiltText));

        var header = new DockPanel { LastChildFill = true };
        var icon = new Image { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            icon.Source = BitmapFrame.Create(new Uri("pack://application:,,,/PictureViewer;component/Resources/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            icon.Visibility = Visibility.Collapsed; // the About box still works without its picture
        }
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(details);

        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        ok.Click += (_, _) => Close();

        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(header);
        root.Children.Add(new TextBlock
        {
            Text = "A small picture viewer with light annotation and editing.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0),
        });
        if (hotKey != null)
            root.Children.Add(new TextBlock { Text = $"Capture hot key: {hotKey}", Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(ok);
        Content = root;
    }

    static TextBlock Line(string label, string value) => new()
    {
        Margin = new Thickness(0, 4, 0, 0),
        Inlines =
        {
            new System.Windows.Documents.Run(label + ":  ") { Foreground = Brushes.Gray },
            new System.Windows.Documents.Run(value),
        },
    };
}
