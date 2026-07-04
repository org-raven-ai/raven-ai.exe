using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke for the Raw Input API, used purely to read <b>relative</b> mouse movement while
/// interactive mode has the real cursor pinned.
///
/// Raw input is delivered independently of the low-level hook and independently of where the
/// cursor is (or that it is clipped to a 1&#215;1 rect), so we get clean, unaccelerated
/// <c>lLastX/lLastY</c> deltas to drive the fake cursor even though the hook is swallowing every
/// legacy mouse message. Registered with <c>RIDEV_INPUTSINK</c> so deltas arrive even when the
/// overlay is not the foreground window.
/// </summary>
internal static class NativeRawInput
{
    public const int WM_INPUT = 0x00FF;

    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;

    // dwFlags for RAWINPUTDEVICE.
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDEV_INPUTSINK = 0x00000100;

    // Generic-desktop mouse usage.
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    // Explicit layout mirrors the C RAWMOUSE (the button fields sit in a union with padding
    // after usFlags); we only read usFlags and the movement deltas.
    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public uint ulButtons;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData,
        ref uint pcbSize, uint cbSizeHeader);

    /// <summary>Registers the mouse for raw input targeted at <paramref name="hWnd"/> (input-sink).</summary>
    public static bool Register(IntPtr hWnd)
    {
        var dev = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_MOUSE,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = hWnd,
            }
        };
        return RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>Removes the mouse raw-input registration.</summary>
    public static void Unregister()
    {
        var dev = new RAWINPUTDEVICE[]
        {
            new()
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_MOUSE,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero,
            }
        };
        RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>
    /// Reads a relative mouse-movement delta from a <c>WM_INPUT</c> message. Returns false for
    /// non-mouse input, absolute-coordinate devices (touch / RDP), or zero-movement events.
    /// </summary>
    public static bool TryGetMouseDelta(IntPtr hRawInput, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
            return false;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size)
                return false;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RIM_TYPEMOUSE)
                return false;

            var mouse = Marshal.PtrToStructure<RAWMOUSE>(IntPtr.Add(buffer, (int)headerSize));
            if ((mouse.usFlags & MOUSE_MOVE_ABSOLUTE) != 0)
                return false;

            dx = mouse.lLastX;
            dy = mouse.lLastY;
            return dx != 0 || dy != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
