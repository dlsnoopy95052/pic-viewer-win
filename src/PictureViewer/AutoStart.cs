using Microsoft.Win32;

namespace PictureViewer;

/// <summary>"Open at Login": a per-user Run entry that starts the app straight into the tray.</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "PictureViewer";

    public const string BackgroundFlag = "--background";

    static string Command => $"\"{Environment.ProcessPath}\" {BackgroundFlag}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
