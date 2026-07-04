using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke for a global low-level mouse hook (<c>WH_MOUSE_LL</c>).
///
/// The callback sees every mouse event system-wide <i>before</i> it is applied. Returning a
/// non-zero value instead of calling <see cref="CallNextHookEx"/> <b>swallows</b> the event:
/// it reaches no other window and the OS cursor never moves. That single fact gives interactive
/// mode its "frozen real pointer + nothing passes through" behaviour. Movement itself is read
/// from raw input (see <see cref="NativeRawInput"/>), because the pinned cursor can no longer
/// report position deltas through the hook.
///
/// The callback runs on the thread that installed the hook (the UI thread) and must be fast —
/// if it blocks past <c>LowLevelHooksTimeout</c>, Windows silently drops the hook. So the
/// callback only classifies the event and raises a lightweight delegate; any real work is
/// posted to the dispatcher by the caller.
/// </summary>
internal static class NativeMouseHook
{
    public const int WH_MOUSE_LL = 14;

    // Mouse messages delivered as wParam to the low-level hook.
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;

    /// <summary>Set in <see cref="MSLLHOOKSTRUCT.flags"/> for events synthesized by SendInput/etc.</summary>
    public const uint LLMHF_INJECTED = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public NativeCursor.POINT pt;
        public uint mouseData; // high word = wheel delta for WM_MOUSEWHEEL
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>Extracts the signed wheel notch delta from an <see cref="MSLLHOOKSTRUCT.mouseData"/>.</summary>
    public static int WheelDelta(uint mouseData) => (short)(mouseData >> 16);
}
