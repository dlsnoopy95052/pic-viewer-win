using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PictureViewer;

/// <summary>A system-wide hot key that works while another app is in front (no extra permission needed).</summary>
public sealed class GlobalHotKey : IDisposable
{
    public const uint Alt = 0x0001, Control = 0x0002, Shift = 0x0004, Win = 0x0008;
    const int WmHotKey = 0x0312;
    const uint NoRepeat = 0x4000;
    static readonly IntPtr MessageOnlyParent = new(-3);
    const int HotKeyId = 0x5056;

    readonly HwndSource _source;

    /// <summary>False when another program already owns this key combination.</summary>
    public bool Registered { get; }

    public event EventHandler? Pressed;

    public GlobalHotKey(uint modifiers, uint virtualKey)
    {
        // A message-only window: it receives WM_HOTKEY without ever being visible.
        _source = new HwndSource(new HwndSourceParameters("PictureViewerHotKey") { ParentWindow = MessageOnlyParent });
        _source.AddHook(WndProc);
        Registered = RegisterHotKey(_source.Handle, HotKeyId, modifiers | NoRepeat, virtualKey);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Registered) UnregisterHotKey(_source.Handle, HotKeyId);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
