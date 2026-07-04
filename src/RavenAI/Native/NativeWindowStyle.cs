using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke wrapper for tweaking a window's extended styles.
///
/// Task Manager groups a process under "Apps" when it owns a visible, unowned,
/// non-tool top-level window (roughly the Alt+Tab rule). The overlay avoids that
/// classification by carrying WS_EX_TOOLWINDOW (and never WS_EX_APPWINDOW), which —
/// combined with a hidden owner window — makes it a plain background process.
/// </summary>
internal static class NativeWindowStyle
{
    private const int GWL_EXSTYLE = -20;

    /// <summary>Tool windows are excluded from the taskbar, Alt+Tab, and Task Manager's Apps group.</summary>
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Forces a window ONTO the taskbar — must never be set on the overlay.</summary>
    private const long WS_EX_APPWINDOW = 0x00040000;

    // 64-bit only (the app ships as win-x64); GetWindowLongPtr/SetWindowLongPtr do not
    // exist as exports in 32-bit user32.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>Adds WS_EX_TOOLWINDOW and strips WS_EX_APPWINDOW on <paramref name="hWnd"/>.</summary>
    public static void MakeToolWindow(IntPtr hWnd)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(ex));
    }
}
