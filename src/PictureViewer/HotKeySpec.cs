using System.Windows.Input;

namespace PictureViewer;

/// <summary>A key combination for the global capture hot key: modifier flags (see <see cref="GlobalHotKey"/>) plus a virtual key.</summary>
public readonly record struct HotKeySpec(uint Modifiers, uint VirtualKey)
{
    public const uint PrintScreenKey = 0x2C;

    public static HotKeySpec Default => new(GlobalHotKey.Control | GlobalHotKey.Alt, 0x34); // Ctrl+Alt+4

    /// <summary>
    /// A hot key needs a modifier, otherwise it would swallow ordinary typing everywhere. Print Screen is the one
    /// key that is fine on its own. Alt+F4 is the system's own.
    /// </summary>
    public bool IsAllowed
    {
        get
        {
            if (VirtualKey == 0) return false;
            if (Modifiers == GlobalHotKey.Alt && VirtualKey == 0x73) return false; // Alt+F4
            return Modifiers != 0 || VirtualKey == PrintScreenKey;
        }
    }

    /// <summary>"Ctrl+Alt+4", "Win+Shift+S", "Print Screen".</summary>
    public string Format()
    {
        var parts = new List<string>();
        if ((Modifiers & GlobalHotKey.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & GlobalHotKey.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & GlobalHotKey.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & GlobalHotKey.Win) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    static string KeyName(uint virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        string name = key.ToString();
        return key switch
        {
            Key.PrintScreen => "Print Screen",
            >= Key.D0 and <= Key.D9 => name[1..],            // "D4" -> "4"
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemTilde => "`",
            Key.Return => "Enter",
            Key.Next => "Page Down",
            Key.Prior => "Page Up",
            _ => name,
        };
    }

    /// <summary>
    /// The combination for a key press, or null while only modifiers are held. <paramref name="modifiers"/> are WPF's.
    /// </summary>
    public static HotKeySpec? FromKeyPress(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None or Key.DeadCharProcessed)
            return null;

        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= GlobalHotKey.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= GlobalHotKey.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= GlobalHotKey.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= GlobalHotKey.Win;
        return new HotKeySpec(flags, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}
