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

    /// <summary>Makes a window click-through: mouse hit-tests pass to whatever is underneath.</summary>
    private const long WS_EX_TRANSPARENT = 0x00000020;

    // 64-bit only (the app ships as win-x64); GetWindowLongPtr/SetWindowLongPtr do not
    // exist as exports in 32-bit user32.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Re-asserts <paramref name="hWnd"/> at the top of the topmost band, above topmost popups
    /// that opened after it (WPF dropdowns and tooltips of a topmost window are themselves
    /// topmost). Position and size are untouched.
    /// </summary>
    public static void BumpTopmost(IntPtr hWnd)
        => SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Adds WS_EX_TOOLWINDOW and strips WS_EX_APPWINDOW on <paramref name="hWnd"/>.</summary>
    public static void MakeToolWindow(IntPtr hWnd)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>
    /// Toggles click-through (WS_EX_TRANSPARENT) on <paramref name="hWnd"/>. When set, mouse
    /// input hit-tests <i>past</i> the window to whatever is underneath; keyboard is unaffected
    /// (it always follows focus). The window is already layered via WPF's AllowsTransparency,
    /// so only the transparent bit is flipped here. The overlay keeps this set at all times —
    /// interactive mode drives its own fake cursor rather than relying on OS hit-testing.
    /// </summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        ex = clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(ex));
    }
}
