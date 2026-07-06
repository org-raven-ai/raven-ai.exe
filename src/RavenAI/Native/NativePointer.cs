using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>Standard system cursors the overlay paints in place of the frozen real pointer.</summary>
public enum SystemCursorKind
{
    Arrow,
    IBeam,
    Hand,
}

/// <summary>
/// P/Invoke used to make the painted fake cursor look like the real one: it loads the actual
/// system cursor bitmaps (arrow / I-beam / hand, with their true hot-spots) at the current
/// on-screen cursor size — which reflects both the display DPI and the Windows "mouse pointer
/// size" accessibility setting — so the overlay can render the genuine OS cursor at the right size.
/// </summary>
internal static class NativePointer
{
    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;
    private const int IDC_HAND = 32649;

    private const uint IMAGE_CURSOR = 2;
    private const uint LR_SHARED = 0x00008000;

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
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static int CursorId(SystemCursorKind kind) => kind switch
    {
        SystemCursorKind.IBeam => IDC_IBEAM,
        SystemCursorKind.Hand => IDC_HAND,
        _ => IDC_ARROW,
    };

    /// <summary>
    /// Returns a handle to a standard system cursor, scaled to <paramref name="sizePixels"/> square
    /// when positive (matching the real on-screen cursor size), else the default base size. The
    /// handle is system-owned (shared) and must NOT be destroyed.
    /// </summary>
    public static IntPtr LoadSystemCursor(SystemCursorKind kind, int sizePixels)
    {
        int id = CursorId(kind);
        if (sizePixels > 0)
        {
            IntPtr scaled = LoadImageW(IntPtr.Zero, (IntPtr)id, IMAGE_CURSOR, sizePixels, sizePixels, LR_SHARED);
            if (scaled != IntPtr.Zero)
                return scaled;
        }
        return LoadCursor(IntPtr.Zero, (IntPtr)id);
    }

    /// <summary>The cursor's hot-spot (the pixel that is "the point") in cursor-bitmap pixels.</summary>
    public static (int X, int Y) GetHotspot(IntPtr hCursor)
    {
        if (hCursor == IntPtr.Zero || !GetIconInfo(hCursor, out ICONINFO info))
            return (0, 0);

        // GetIconInfo creates bitmaps we own; free them so we don't leak GDI objects.
        if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
        return (info.xHotspot, info.yHotspot);
    }

    /// <summary>
    /// The current cursor's height in physical pixels — the real size Windows is drawing, already
    /// reflecting the display DPI and the pointer-size accessibility setting. Returns 0 if unknown.
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
