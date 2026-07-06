using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke used to size the painted (vector) fake cursor to match the real one: it reads the
/// current cursor's pixel size (which reflects the Windows "mouse pointer size" accessibility
/// setting) and the monitor's DPI scale (which Windows applies to the cursor at draw time).
/// </summary>
internal static class NativePointer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public NativeCursor.POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// The DPI scale (1.0 = 96 DPI, 1.5 = 150%) of the monitor the window is on. This is the factor
    /// by which Windows enlarges the base cursor at draw time, which the cursor bitmap size alone
    /// does not reflect. Falls back to 1.0.
    /// </summary>
    public static double GetWindowDpiScale(IntPtr hwnd)
    {
        uint dpi = hwnd == IntPtr.Zero ? 0 : GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>
    /// The current cursor's height in pixels — reflecting the pointer-size accessibility setting
    /// (but not the DPI scale, which Windows applies at draw time). Returns 0 if unknown.
    /// </summary>
    public static int GetCurrentCursorSize()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero)
            return 0;
        if (!GetIconInfo(ci.hCursor, out ICONINFO info))
            return 0;

        int size = 0;
        try
        {
            var bmp = new BITMAP();
            if (info.hbmColor != IntPtr.Zero)
            {
                if (GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), ref bmp) != 0)
                    size = bmp.bmHeight;
            }
            else if (info.hbmMask != IntPtr.Zero)
            {
                // A monochrome cursor stores the AND and XOR masks stacked, so the bitmap is
                // double height; the true cursor size is half.
                if (GetObject(info.hbmMask, Marshal.SizeOf<BITMAP>(), ref bmp) != 0)
                    size = bmp.bmHeight / 2;
            }
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
        return size;
    }
}
