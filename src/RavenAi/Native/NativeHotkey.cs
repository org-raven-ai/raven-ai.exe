using System.Runtime.InteropServices;

namespace RavenAi.Native;

/// <summary>
/// P/Invoke for the global hotkey API. RegisterHotKey associates a system-wide key
/// combination with a window; Windows then posts WM_HOTKEY (0x0312) to that window's
/// message loop whenever the combo is pressed, even when the app is not focused.
/// </summary>
internal static class NativeHotkey
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;

    // Modifier flags (fsModifiers) for RegisterHotKey.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // don't fire repeatedly while held
}
