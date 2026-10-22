namespace PictureViewer;

/// <summary>File extensions the viewer will list and try to open.</summary>
public static class ImageFormats
{
    /// <summary>Formats Windows Imaging Component can usually decode (some need a Store codec).</summary>
    static readonly string[] Native =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff",
        ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".pdf",
    ];

    /// <summary>Formats only Magick.NET decodes.</summary>
    static readonly string[] MagickOnly =
    [
        ".psd", ".svg", ".tga", ".jxl", ".exr", ".hdr", ".pcx", ".xcf",
        ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".orf", ".rw2", ".raf", ".pef", ".srw",
    ];

    static readonly HashSet<string> All =
        new(Native.Concat(MagickOnly), StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> MagickOnlySet = new(MagickOnly, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string path) => All.Contains(Path.GetExtension(path));

    /// <summary>PDF pages are drawn by Windows itself (first page only).</summary>
    public static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool NeedsMagick(string path) => MagickOnlySet.Contains(Path.GetExtension(path));

    /// <summary>Filter string for the Open dialog.</summary>
    public static string DialogFilter
    {
        get
        {
            var patterns = string.Join(';', All.Order().Select(e => "*" + e));
            return $"Pictures|{patterns}|All files|*.*";
        }
    }
}
