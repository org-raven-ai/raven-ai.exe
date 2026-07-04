using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using RavenAI.Native;

namespace RavenAI.Services.Overlay;

/// <summary>Which mouse button an interactive-mode event refers to.</summary>
public enum OverlayMouseButton
{
    Left,
    Right,
}

/// <summary>
/// Owns the native side of interactive mode: the global low-level mouse hook (input
/// suppression + button/wheel events), raw input (movement deltas), and the frozen real cursor.
///
/// While active it <b>swallows every mouse event system-wide</b> so nothing reaches the apps
/// underneath and the OS cursor stays put, and it re-emits movement/buttons/wheel as .NET
/// events for the overlay to drive its fake cursor and hit-test its own UI. It does not know
/// anything about WPF visuals — the window subscribes to these events and does the painting and
/// dispatch. Keyboard focus is handed to the overlay on enter and restored on exit so typing
/// lands in the chat box while interactive and in the previous app afterwards.
///
/// Everything runs on the UI thread: the hook is installed there (so its callback fires there),
/// and button/wheel events are marshalled through the dispatcher so no UI work runs inside the
/// time-critical hook callback.
/// </summary>
public sealed class InteractiveModeController : IDisposable
{
    private IntPtr _hwnd;
    private HwndSource? _source;
    private Dispatcher? _dispatcher;

    // The hook delegate MUST be kept alive for the hook's lifetime or the GC collects it and
    // the callback becomes a dangling pointer.
    private NativeMouseHook.LowLevelMouseProc? _hookProc;
    private IntPtr _hookHandle;

    private IntPtr _previousForeground;

    public bool IsActive { get; private set; }

    /// <summary>Relative mouse movement (device pixels) while interactive.</summary>
    public event Action<int, int>? MouseMoved;

    /// <summary>A mouse button was pressed while interactive (marshalled to the UI thread).</summary>
    public event Action<OverlayMouseButton>? ButtonPressed;

    /// <summary>A mouse button was released while interactive (marshalled to the UI thread).</summary>
    public event Action<OverlayMouseButton>? ButtonReleased;

    /// <summary>Mouse wheel turned while interactive; positive is up/away (marshalled to UI thread).</summary>
    public event Action<int>? WheelScrolled;

    /// <summary>
    /// Wires the controller to the overlay window's message loop. Call once the HWND exists.
    /// Raw <c>WM_INPUT</c> movement is read here; it is ignored unless interactive mode is active.
    /// </summary>
    public void Initialize(IntPtr hwnd, HwndSource source)
    {
        _hwnd = hwnd;
        _source = source;
        _dispatcher = source.Dispatcher;
        source.AddHook(WndProc);
    }

    /// <summary>Enters interactive mode: freezes the real cursor and starts capturing all mouse input.</summary>
    public void Enter()
    {
        if (IsActive || _source is null)
            return;

        _previousForeground = NativeCursor.GetForeground();

        // Pin the physical pointer where it is; raw input still delivers movement deltas.
        NativeCursor.Freeze(NativeCursor.GetPosition());
        NativeRawInput.Register(_hwnd);

        _hookProc = HookCallback;
        using Process proc = Process.GetCurrentProcess();
        using ProcessModule module = proc.MainModule!;
        _hookHandle = NativeMouseHook.SetWindowsHookEx(
            NativeMouseHook.WH_MOUSE_LL, _hookProc,
            NativeMouseHook.GetModuleHandle(module.ModuleName), 0);

        IsActive = true;
    }

    /// <summary>Exits interactive mode: releases the cursor, stops capturing, restores focus.</summary>
    public void Exit()
    {
        if (!IsActive)
            return;

        IsActive = false;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMouseHook.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProc = null;

        NativeRawInput.Unregister();
        NativeCursor.Release();
        NativeCursor.RestoreForeground(_previousForeground);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (IsActive && msg == NativeRawInput.WM_INPUT
            && NativeRawInput.TryGetMouseDelta(lParam, out int dx, out int dy))
        {
            MouseMoved?.Invoke(dx, dy);
        }
        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !IsActive)
            return NativeMouseHook.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        switch (msg)
        {
            case NativeMouseHook.WM_LBUTTONDOWN:
                Post(() => ButtonPressed?.Invoke(OverlayMouseButton.Left));
                break;
            case NativeMouseHook.WM_LBUTTONUP:
                Post(() => ButtonReleased?.Invoke(OverlayMouseButton.Left));
                break;
            case NativeMouseHook.WM_RBUTTONDOWN:
                Post(() => ButtonPressed?.Invoke(OverlayMouseButton.Right));
                break;
            case NativeMouseHook.WM_RBUTTONUP:
                Post(() => ButtonReleased?.Invoke(OverlayMouseButton.Right));
                break;
            case NativeMouseHook.WM_MOUSEWHEEL:
                var data = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<NativeMouseHook.MSLLHOOKSTRUCT>(lParam);
                int delta = NativeMouseHook.WheelDelta(data.mouseData);
                Post(() => WheelScrolled?.Invoke(delta));
                break;
        }

        // Swallow every mouse event so nothing passes through to the apps underneath and the
        // OS cursor never moves.
        return (IntPtr)1;
    }

    // Defers work out of the hook callback so no UI dispatch runs inside the time-critical hook.
    private void Post(Action action) => _dispatcher?.BeginInvoke(action);

    public void Dispose()
    {
        Exit();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
