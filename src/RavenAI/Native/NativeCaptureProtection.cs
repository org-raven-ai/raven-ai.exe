using System.Runtime.InteropServices;

namespace RavenAI.Native;

/// <summary>
/// P/Invoke wrapper around the Desktop Window Manager display-affinity API.
///
/// This is the single most important piece of native interop in the app:
/// it is what makes the window omitted from screen captures. Handle with care.
///
/// <see cref="SetWindowDisplayAffinity"/> instructs DWM how the given window may
/// appear on capture surfaces (screenshots, screen recorders, screen-share).
/// With <see cref="WDA_EXCLUDEFROMCAPTURE"/> the window is composited normally onto
/// the physical display but is entirely omitted from any capture buffer, so whatever
/// is behind it is what gets shared/recorded instead.
/// </summary>
internal static class NativeCaptureProtection
{
    // BOOL SetWindowDisplayAffinity(HWND hWnd, DWORD dwAffinity);
    // Sets how the window is displayed on capture surfaces. Returns false on failure;
    // call GetLastWin32Error() for the reason.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // BOOL GetWindowDisplayAffinity(HWND hWnd, DWORD* pdwAffinity);
    // Reads back the current affinity so we can VERIFY the flag actually stuck.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    /// <summary>Default. Window is captured normally (visible in shares/recordings).</summary>
    public const uint WDA_NONE = 0x00000000;

    /// <summary>
    /// Fallback for Windows &lt; 10 build 19041. The window still shows on the physical
    /// display but renders as a solid black box in captures (content is hidden, but the
    /// box's presence is not). Better than nothing when EXCLUDEFROMCAPTURE is unavailable.
    /// </summary>
    public const uint WDA_MONITOR = 0x00000001;

    /// <summary>
    /// Preferred. Window is fully omitted from all capture surfaces — invisible in
    /// screenshots/recordings/screen-share, while still visible on the real monitor.
    /// Requires Windows 10 version 2004 (build 19041) or later.
    /// </summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    /// <summary>
    /// Applies the requested display affinity to <paramref name="hWnd"/>.
    /// </summary>
    /// <param name="lastError">
    /// 0 on success, otherwise the Win32 error from GetLastWin32Error().
    /// A common failure is error 8 (ERROR_NOT_ENOUGH_MEMORY) on layered
    /// (WS_EX_LAYERED / AllowsTransparency) windows — which is exactly why the
    /// protected window must stay opaque.
    /// </param>
    public static bool Apply(IntPtr hWnd, uint affinity, out int lastError)
    {
        bool ok = SetWindowDisplayAffinity(hWnd, affinity);
        lastError = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    /// <summary>Reads the current affinity back and confirms it equals <paramref name="expected"/>.</summary>
    public static bool Verify(IntPtr hWnd, uint expected)
        => GetWindowDisplayAffinity(hWnd, out uint current) && current == expected;

    /// <summary>Reads the raw current affinity value (for diagnostics / logging).</summary>
    public static bool TryGet(IntPtr hWnd, out uint current)
        => GetWindowDisplayAffinity(hWnd, out current);
}
