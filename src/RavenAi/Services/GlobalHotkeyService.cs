using System.Windows.Interop;
using RavenAi.Native;

namespace RavenAi.Services;

/// <summary>
/// Registers system-wide hotkeys and raises <see cref="HotkeyPressed"/> when they fire.
/// Works even when the app is not focused because RegisterHotKey is a global registration
/// and Windows delivers WM_HOTKEY straight to our HwndSource message hook.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private HwndSource? _source;
    private IntPtr _hWnd;
    private int _nextId = 1;

    // id -> friendly name, so HotkeyPressed can tell callers which combo fired.
    private readonly Dictionary<int, string> _registered = new();

    /// <summary>Raised (on the UI thread) with the registered name of the hotkey that fired.</summary>
    public event Action<string>? HotkeyPressed;

    /// <summary>
    /// Attaches to a window's message loop. Call once the HWND exists
    /// (e.g. in OnSourceInitialized).
    /// </summary>
    public void Attach(IntPtr hWnd)
    {
        _hWnd = hWnd;
        _source = HwndSource.FromHwnd(hWnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a hotkey under a friendly <paramref name="name"/>.
    /// Returns false if Windows refuses (e.g. the combo is already taken system-wide).
    /// </summary>
    public bool Register(string name, uint modifiers, uint virtualKey)
    {
        int id = _nextId++;
        // MOD_NOREPEAT stops the hotkey from auto-firing while the keys are held down.
        bool ok = NativeHotkey.RegisterHotKey(_hWnd, id, modifiers | NativeHotkey.MOD_NOREPEAT, virtualKey);
        if (ok) _registered[id] = name;
        return ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeHotkey.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registered.TryGetValue(id, out string? name))
            {
                HotkeyPressed?.Invoke(name);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            foreach (int id in _registered.Keys)
                NativeHotkey.UnregisterHotKey(_hWnd, id);
        }
        _registered.Clear();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
