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
/// P/Invoke used to make the painted fake cursor look and move like the real one: it loads the
/// actual system cursor bitmaps (arrow / I-beam / hand, with their true hot-spots) so the overlay
/// can render the genuine OS cursor, and it reads the pointer-speed slider and "enhance pointer
/// precision" (acceleration) setting so raw-input deltas can be scaled to match how Windows would
/// have moved the real pointer.
/// </summary>
internal static class NativePointer
{
    // ---- System cursors ---------------------------------------------------------------------

    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;
    private const int IDC_HAND = 32649;

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Returns the shared handle of a standard system cursor. The handle is owned by the system
    /// (loaded from a null instance), so it must NOT be destroyed.
    /// </summary>
    public static IntPtr LoadSystemCursor(SystemCursorKind kind)
    {
        int id = kind switch
        {
            SystemCursorKind.IBeam => IDC_IBEAM,
            SystemCursorKind.Hand => IDC_HAND,
            _ => IDC_ARROW,
        };
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

    // ---- Pointer speed / acceleration -------------------------------------------------------

    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_GETMOUSESPEED = 0x0070;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam,
        [In, Out] int[] pvParam, uint fWinIni);

    /// <summary>The pointer-speed slider value, 1..20 (10 is the 1:1 default). Falls back to 10.</summary>
    public static int GetPointerSpeed()
    {
        int speed = 10;
        if (SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0) && speed >= 1 && speed <= 20)
            return speed;
        return 10;
    }

    /// <summary>True when "Enhance pointer precision" (mouse acceleration) is enabled.</summary>
    public static bool IsEnhancePointerPrecisionOn()
    {
        var mouse = new int[3]; // { threshold1, threshold2, accelerationOn }
        return SystemParametersInfo(SPI_GETMOUSE, 0, mouse, 0) && mouse[2] != 0;
    }
}
