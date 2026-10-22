using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PictureViewer;

/// <summary>Print preview: pick scaling and centering, change printer / paper / orientation, then print.</summary>
public sealed class PrintWindow : Window
{
    // US Letter in 1/96 inch, with 0.25 inch margins: used when the printer can't say what it supports.
    static readonly Size FallbackPage = new(816, 1056);
    const double FallbackMargin = 24;

    readonly BitmapSource _picture;
    readonly string _jobTitle;
    readonly System.Windows.Controls.PrintDialog _dialog = new();

    readonly Border _previewHost = new() { Background = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0x58)), Padding = new Thickness(24) };
    readonly ComboBox _modeBox = new() { Margin = new Thickness(0, 4, 0, 12), Focusable = false };
    readonly CheckBox _centerBox = new() { Content = "Center on page", IsChecked = true, Margin = new Thickness(0, 0, 0, 16) };
    readonly TextBlock _printerText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    readonly TextBlock _paperText = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 10) };
    readonly Button _printButton = new() { Content = "Print", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };

    public PrintWindow(BitmapSource picture, string jobTitle)
    {
        _picture = picture;
        _jobTitle = jobTitle;

        Title = "Print";
        Width = 940;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var mode in Enum.GetValues<PrintScaleMode>())
            _modeBox.Items.Add(new ComboBoxItem { Content = PrintLayout.Title(mode), Tag = mode });
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectionChanged += (_, _) => UpdatePreview();
        _centerBox.Click += (_, _) => UpdatePreview();

        var setup = new Button { Content = "Printer and paper…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Left };
        setup.Click += (_, _) => ChoosePrinter();
        _printButton.Click += (_, _) => PrintNow();
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

        var options = new StackPanel { Margin = new Thickness(18) };
        options.Children.Add(new TextBlock { Text = "Printer", Foreground = Brushes.Gray });
        options.Children.Add(_printerText);
        options.Children.Add(_paperText);
        options.Children.Add(setup);
        options.Children.Add(new TextBlock { Text = "Scaling" });
        options.Children.Add(_modeBox);
        options.Children.Add(_centerBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18) };
        buttons.Children.Add(_printButton);
        buttons.Children.Add(cancel);

        var side = new DockPanel { Width = 280 };
        DockPanel.SetDock(buttons, Dock.Bottom);
        side.Children.Add(buttons);
        side.Children.Add(options);

        var root = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(side);
        root.Children.Add(_previewHost);
        Content = root;

        ChooseOrientation();
        SizeChanged += (_, _) => UpdatePreview();
        Loaded += (_, _) => UpdatePreview();
    }

    PrintScaleMode Mode => _modeBox.SelectedItem is ComboBoxItem { Tag: PrintScaleMode mode } ? mode : PrintScaleMode.ShrinkToFit;
    bool Centered => _centerBox.IsChecked == true;

    /// <summary>Wide pictures start on a landscape page, tall ones on a portrait page; the user can still change it.</summary>
    void ChooseOrientation()
    {
        try
        {
            var ticket = _dialog.PrintTicket;
            if (ticket != null)
            {
                ticket.PageOrientation = _picture.PixelWidth > _picture.PixelHeight ? PageOrientation.Landscape : PageOrientation.Portrait;
                _dialog.PrintTicket = ticket;
            }
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            // No printer, or one that rejects the ticket: the preview falls back to Letter.
        }
    }

    void ChoosePrinter()
    {
        try
        {
            if (_dialog.ShowDialog() == true) UpdatePreview();
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Page size, and the printable area inside it, in 1/96 inch (landscape/portrait already applied).</summary>
    (Size Page, Size Area) Geometry()
    {
        double areaW = 0, areaH = 0;
        Size? media = null;
        try
        {
            areaW = _dialog.PrintableAreaWidth;
            areaH = _dialog.PrintableAreaHeight;
            if (_dialog.PrintTicket?.PageMediaSize is { Width: { } w, Height: { } h }) media = new Size(w, h);
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            // Fall through to the defaults.
        }

        if (areaW <= 0 || areaH <= 0)
            return (FallbackPage, new Size(FallbackPage.Width - 2 * FallbackMargin, FallbackPage.Height - 2 * FallbackMargin));

        Size page;
        if (media is { } m)
        {
            bool landscape = areaW > areaH;
            page = landscape ? new Size(Math.Max(m.Width, m.Height), Math.Min(m.Width, m.Height))
                             : new Size(Math.Min(m.Width, m.Height), Math.Max(m.Width, m.Height));
        }
        else
        {
            page = new Size(areaW + 2 * FallbackMargin, areaH + 2 * FallbackMargin);
        }
        return (page, new Size(Math.Min(areaW, page.Width), Math.Min(areaH, page.Height)));
    }

    /// <summary>One page: the Picture placed on the printable area, which sits centered on the paper.</summary>
    FixedPage BuildPage(bool forPreview)
    {
        var (pageSize, areaSize) = Geometry();
        var rect = PrintLayout.Place(PrintLayout.NaturalSize(_picture), areaSize, Mode, Centered);

        var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height, Background = Brushes.White };
        var area = new Canvas { Width = areaSize.Width, Height = areaSize.Height, ClipToBounds = true };
        if (!rect.IsEmpty)
        {
            var image = new Image { Source = _picture, Width = rect.Width, Height = rect.Height, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(image, rect.X);
            Canvas.SetTop(image, rect.Y);
            area.Children.Add(image);
        }
        FixedPage.SetLeft(area, (pageSize.Width - areaSize.Width) / 2);
        FixedPage.SetTop(area, (pageSize.Height - areaSize.Height) / 2);
        page.Children.Add(area);

        if (forPreview)
        {
            // Show where the printer can actually print.
            var outline = new Rectangle
            {
                Width = areaSize.Width, Height = areaSize.Height, Stroke = Brushes.LightGray, StrokeThickness = 1,
                StrokeDashArray = [4, 3], IsHitTestVisible = false,
            };
            FixedPage.SetLeft(outline, (pageSize.Width - areaSize.Width) / 2);
            FixedPage.SetTop(outline, (pageSize.Height - areaSize.Height) / 2);
            page.Children.Add(outline);
        }
        return page;
    }

    void UpdatePreview()
    {
        if (!IsLoaded) return;

        var (pageSize, _) = Geometry();
        var page = BuildPage(forPreview: true);
        page.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5 };
        _previewHost.Child = new Viewbox { Stretch = Stretch.Uniform, Child = page };

        string name = "No printer found";
        try
        {
            name = _dialog.PrintQueue?.FullName ?? name;
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            // Keep the placeholder.
        }
        _printerText.Text = name;
        _printButton.IsEnabled = name != "No printer found";

        bool landscape = pageSize.Width > pageSize.Height;
        _paperText.Text = $"{pageSize.Width / 96:0.##} × {pageSize.Height / 96:0.##} in · {(landscape ? "Landscape" : "Portrait")}";
    }

    /// <summary>The document that is sent to the printer: one page, laid out exactly as in the preview.</summary>
    FixedDocument BuildDocument()
    {
        var page = BuildPage(forPreview: false);
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(page.Width, page.Height);
        document.Pages.Add(content);
        return document;
    }

    void PrintNow()
    {
        try
        {
            _dialog.PrintDocument(BuildDocument().DocumentPaginator, _jobTitle);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
