using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke for reading, moving, and confining the system mouse cursor, plus reading/setting
/// the foreground window.
///
/// Interactive mode uses these to <b>freeze</b> the real pointer: it clips the cursor to a
/// 1&#215;1 rectangle so Windows can't move it, while the app paints and drives its own fake
/// cursor from raw-input deltas. Foreground save/restore lets us hand keyboard focus to the
/// overlay while interactive (so typing lands in the chat box) and give it back on exit.
/// </summary>
internal static class NativeCursor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursorNull(IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Current cursor position in physical screen (virtual-desktop) pixels.</summary>
    public static POINT GetPosition()
    {
        GetCursorPos(out POINT p);
        return p;
    }

    /// <summary>
    /// Pins the cursor to a 1&#215;1 rectangle at <paramref name="p"/>, which stops the OS from
    /// moving it at all — the physical pointer is frozen exactly where the user left it. Raw
    /// input still reports relative movement, so the fake cursor keeps moving.
    /// </summary>
    public static void Freeze(POINT p)
    {
        var rect = new RECT { Left = p.X, Top = p.Y, Right = p.X + 1, Bottom = p.Y + 1 };
        ClipCursor(ref rect);
    }

    /// <summary>Releases any cursor clip, letting the real pointer move freely again.</summary>
    public static void Release() => ClipCursorNull(IntPtr.Zero);

    /// <summary>The window that currently owns the foreground (keyboard focus).</summary>
    public static IntPtr GetForeground() => GetForegroundWindow();

    /// <summary>Restores the foreground to a previously captured window.</summary>
    public static void RestoreForeground(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
            SetForegroundWindow(hWnd);
    }
}
