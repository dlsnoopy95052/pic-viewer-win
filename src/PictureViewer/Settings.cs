using System.Text.Json;

namespace PictureViewer;

/// <summary>The few options that survive restarts, kept as JSON under %APPDATA%.</summary>
public sealed class Settings
{
    /// <summary>Put every Capture on the clipboard (so it can be pasted onto an open Picture).</summary>
    public bool CopyCaptures { get; set; } = true;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PictureViewer", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupt settings: start from the defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember an option isn't worth interrupting the user.
        }
    }
}
