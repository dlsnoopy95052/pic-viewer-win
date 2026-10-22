using System.Text.Json;

namespace PictureViewer;

/// <summary>The few options that survive restarts, kept as JSON under %APPDATA%.</summary>
public sealed class Settings
{
    /// <summary>Put every Capture on the clipboard (so it can be pasted onto an open Picture).</summary>
    public bool CopyCaptures { get; set; } = true;

    /// <summary>The global capture hot key (modifier flags and virtual key; see <see cref="HotKeySpec"/>).</summary>
    public uint HotKeyModifiers { get; set; } = HotKeySpec.Default.Modifiers;
    public uint HotKeyVirtualKey { get; set; } = HotKeySpec.Default.VirtualKey;

    [System.Text.Json.Serialization.JsonIgnore]
    public HotKeySpec HotKey
    {
        get => new(HotKeyModifiers, HotKeyVirtualKey);
        set
        {
            HotKeyModifiers = value.Modifiers;
            HotKeyVirtualKey = value.VirtualKey;
        }
    }

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
