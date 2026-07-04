using RavenAi.Native;

namespace RavenAi.Services;

/// <summary>
/// Result of an attempt to protect a window from screen capture.
/// </summary>
public sealed record CaptureProtectionResult(
    bool Success,
    uint AppliedAffinity,
    bool FullyHidden,      // true => EXCLUDEFROMCAPTURE (invisible); false => MONITOR fallback (black box)
    int Win32Error,
    string Message);

/// <summary>
/// Owns the screen-capture-exclusion behaviour — the whole reason RavenAi exists.
///
/// Strategy:
///   * Windows 10 build 19041+  -> apply WDA_EXCLUDEFROMCAPTURE (window fully invisible in captures).
///   * Older Windows            -> fall back to WDA_MONITOR (window shows as a black box in captures).
/// After applying, we always read the affinity back to VERIFY it stuck, and we surface
/// any failure so the UI can warn the user loudly. We never let the user believe they are
/// protected when they are not.
/// </summary>
public sealed class ScreenCaptureProtectionService
{
    // Windows 10, version 2004 == build 19041. EXCLUDEFROMCAPTURE requires this or newer.
    private const int MinBuildForExcludeFromCapture = 19041;

    /// <summary>True when the running OS supports full invisibility (EXCLUDEFROMCAPTURE).</summary>
    public bool SupportsExcludeFromCapture =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= MinBuildForExcludeFromCapture;

    /// <summary>
    /// Applies the strongest available capture protection to the given window handle,
    /// then verifies the result. Safe to call again after hide/reshow.
    /// </summary>
    public CaptureProtectionResult Protect(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return new(false, NativeCaptureProtection.WDA_NONE, false, 0,
                "Window handle not available yet.");

        // Choose the strongest flag the OS supports.
        uint desired = SupportsExcludeFromCapture
            ? NativeCaptureProtection.WDA_EXCLUDEFROMCAPTURE
            : NativeCaptureProtection.WDA_MONITOR;

        bool ok = NativeCaptureProtection.Apply(hWnd, desired, out int err);
        if (!ok)
        {
            // Most infamous failure is error 8 (ERROR_NOT_ENOUGH_MEMORY) which DWM returns
            // for layered windows — guard against that by keeping the window opaque.
            string hint = err == 8
                ? " (error 8 / ERROR_NOT_ENOUGH_MEMORY — the window is likely layered; " +
                  "ensure AllowsTransparency is not set)"
                : string.Empty;
            return new(false, NativeCaptureProtection.WDA_NONE, false, err,
                $"SetWindowDisplayAffinity failed with Win32 error {err}{hint}.");
        }

        // VERIFY: read the affinity back — do not trust the set call alone.
        if (!NativeCaptureProtection.Verify(hWnd, desired))
        {
            NativeCaptureProtection.TryGet(hWnd, out uint actual);
            return new(false, actual, false, 0,
                $"Verification failed: expected affinity 0x{desired:X8} but read 0x{actual:X8}.");
        }

        bool fullyHidden = desired == NativeCaptureProtection.WDA_EXCLUDEFROMCAPTURE;
        string msg = fullyHidden
            ? "Screen-capture protection active: window is hidden from all captures."
            : "Screen-capture protection active in fallback mode: the window appears as a " +
              "black box in captures (Windows build < 19041 does not support full exclusion).";

        return new(true, desired, fullyHidden, 0, msg);
    }

    /// <summary>Removes capture protection (window becomes normally capturable again).</summary>
    public bool Unprotect(IntPtr hWnd)
        => hWnd != IntPtr.Zero &&
           NativeCaptureProtection.Apply(hWnd, NativeCaptureProtection.WDA_NONE, out _);

    /// <summary>Re-verifies that protection is still in effect (for periodic self-checks).</summary>
    public bool IsStillProtected(IntPtr hWnd)
    {
        uint expected = SupportsExcludeFromCapture
            ? NativeCaptureProtection.WDA_EXCLUDEFROMCAPTURE
            : NativeCaptureProtection.WDA_MONITOR;
        return NativeCaptureProtection.Verify(hWnd, expected);
    }
}
