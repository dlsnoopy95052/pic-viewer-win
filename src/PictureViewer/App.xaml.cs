using System.Windows;

namespace PictureViewer;

public partial class App : Application
{
    void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
        if (e.Args.Length > 0)
            window.OpenPath(e.Args[0]);
    }
}
