using System.Globalization;
using System.Reflection;

namespace PictureViewer;

/// <summary>What About shows: the version and when this build was made.</summary>
public static class AboutInfo
{
    static readonly Assembly App = typeof(AboutInfo).Assembly;

    /// <summary>"1.1.0", plus the short commit when the build knows it ("1.1.0 (6238820)").</summary>
    public static string Version
    {
        get
        {
            string informational = App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            int plus = informational.IndexOf('+');
            string version = plus > 0 ? informational[..plus] : informational;
            if (version.Length == 0) version = App.GetName().Version?.ToString(3) ?? "?";
            return plus > 0 && informational.Length > plus + 7 ? $"{version} ({informational.Substring(plus + 1, 7)})" : version;
        }
    }

    /// <summary>When the build was made, or null for a build that didn't record it.</summary>
    public static DateTime? BuiltAt
    {
        get
        {
            string? text = App.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var built) ? built : null;
        }
    }

    /// <summary>"2026-10-22 11:40:05", or "unknown".</summary>
    public static string BuiltText => BuiltAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "unknown";
}
