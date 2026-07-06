using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke + registry reads used to size the painted (vector) fake cursor to match the real one.
/// The real cursor's on-screen size is the pointer-size "base size" (a logical size at 100%
/// scaling, from the Windows mouse-pointer-size setting) times the monitor's DPI scale (which
/// Windows applies to the cursor at draw time — the base size alone does not include it).
/// </summary>
internal static class NativePointer
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// The DPI scale (1.0 = 96 DPI, 1.25 = 125%) of the monitor the window is on — the factor by
    /// which Windows enlarges the cursor at draw time. Falls back to 1.0.
    /// </summary>
    public static double GetWindowDpiScale(IntPtr hwnd)
    {
        uint dpi = hwnd == IntPtr.Zero ? 0 : GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>
    /// The cursor base size from the Windows pointer-size setting
    /// (HKCU\Control Panel\Cursors\CursorBaseSize): 32 (default/position 1) up to 256, in logical
    /// pixels at 100% scaling. The real drawn size is this times the monitor DPI scale. Falls back
    /// to 32 when the value is missing.
    /// </summary>
    public static int GetCursorBaseSize()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            if (key?.GetValue("CursorBaseSize") is int size && size >= 32)
                return size;
        }
        catch { /* registry unavailable — use the default */ }
        return 32;
    }
}
