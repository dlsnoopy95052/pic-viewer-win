using System.Globalization;
using System.Windows.Media.Imaging;

namespace PictureViewer;

/// <summary>File and EXIF details shown in the info panel.</summary>
public static class ImageInfo
{
    public static List<string> Lines(string path, int pixelWidth, int pixelHeight)
    {
        var lines = new List<string> { Path.GetFileName(path) };

        try
        {
            var file = new FileInfo(path);
            lines.Add("Size: " + FormatBytes(file.Length));
            if (pixelWidth > 0) lines.Add($"Dimensions: {pixelWidth} × {pixelHeight} px");
            lines.Add("Modified: " + file.LastWriteTime.ToString("g", CultureInfo.CurrentCulture));
        }
        catch (IOException)
        {
            // File vanished while reading: show what we have.
        }

        if (!ImageFormats.NeedsMagick(path))
            AddExif(path, lines);
        return lines;
    }

    static void AddExif(string path, List<string> lines)
    {
        try
        {
            // Metadata is read lazily, so the stream must stay open until the queries are done.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames[0].Metadata is BitmapMetadata meta)
                AddMetadataLines(meta, lines);
        }
        catch (Exception)
        {
            // No readable metadata for this format.
        }
    }

    static void AddMetadataLines(BitmapMetadata meta, List<string> lines)
    {
        var camera = string.Join(' ', new[] { Query(meta, "System.Photo.CameraManufacturer"), Query(meta, "System.Photo.CameraModel") }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (camera.Length > 0) lines.Add("Camera: " + camera);

        if (Query(meta, "System.Photo.LensModel") is { Length: > 0 } lens) lines.Add("Lens: " + lens);

        var shot = new List<string>();
        if (Number(meta, "System.Photo.FNumber") is { } f) shot.Add(string.Create(CultureInfo.InvariantCulture, $"ƒ/{f:0.0}"));
        if (Number(meta, "System.Photo.ExposureTime") is { } t and > 0)
            shot.Add(t < 1 ? $"1/{Math.Round(1 / t)} s" : string.Create(CultureInfo.InvariantCulture, $"{t:0.0} s"));
        if (Number(meta, "System.Photo.ISOSpeed") is { } iso) shot.Add($"ISO {iso:0}");
        if (Number(meta, "System.Photo.FocalLength") is { } fl) shot.Add($"{fl:0} mm");
        if (shot.Count > 0) lines.Add(string.Join("  ", shot));

        string? taken = null;
        try { taken = meta.DateTaken; }
        catch (Exception) { /* not supported by this format */ }
        if (!string.IsNullOrWhiteSpace(taken)) lines.Add("Taken: " + taken);
    }

    static object? Raw(BitmapMetadata meta, string query)
    {
        try { return meta.GetQuery(query); }
        catch (Exception) { return null; } // format doesn't support this query
    }

    static string? Query(BitmapMetadata meta, string query) => Raw(meta, query)?.ToString()?.Trim();

    static double? Number(BitmapMetadata meta, string query)
    {
        try { return Raw(meta, query) is { } v ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null; }
        catch (Exception) { return null; }
    }

    public static string FormatBytes(long length)
    {
        double bytes = length;
        string[] units = ["B", "KB", "MB", "GB"];
        int u = 0;
        while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
        return u == 0 ? $"{bytes:0} {units[u]}" : $"{bytes:0.#} {units[u]}";
    }
}
