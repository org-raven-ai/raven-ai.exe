using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RavenAI.Services;
using RavenAI.Services.Voice;
using RavenAI.ViewModels;

namespace RavenAI.Views;

/// <summary>
/// The one and only window. This is where the app's whole reason to exist is wired up:
/// once the HWND exists we apply screen-capture exclusion, verify it, and keep it applied.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ScreenCaptureProtectionService _protection;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly NAudioCapture _capture;

    private DispatcherTimer? _protectionWatchdog;

    // Virtual-key codes for the hotkeys.
    private const uint VK_SPACE = 0x20;
    private const uint VK_V = 0x56;

    public MainWindow(
        MainViewModel vm,
        ScreenCaptureProtectionService protection,
        GlobalHotkeyService hotkeys,
        NAudioCapture capture)
    {
        _vm = vm;
        _protection = protection;
        _hotkeys = hotkeys;
        _capture = capture;

        DataContext = _vm;
        InitializeComponent();

        // Keep the log panel pinned to the newest entry as errors/events stream in.
        _vm.Log.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                LogScroll.ScrollToEnd();
        };

        // Place near the top-right of the working area by default.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Top + 24;
    }

    /// <summary>
    /// The HWND exists here (not in the constructor). This is the correct place to apply
    /// SetWindowDisplayAffinity, register global hotkeys, and hook WM_HOTKEY.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        HideFromTaskManagerApps(hwnd);
        SetupHotkeys(hwnd);
#if DEBUG
        // In Debug builds the window stays visible to screen capture so it can be seen while
        // testing/recording. It's still translucent, just not excluded from capture surfaces.
        Services.Logging.Log.Warning(
            "Debug build: screen-capture protection is DISABLED — the window is visible to captures.",
            null, "Protection");
        _vm.UpdateProtectionStatus(new CaptureProtectionResult(
            Success: false, AppliedAffinity: 0, FullyHidden: false, Win32Error: 0,
            Message: "Debug build: capture protection disabled for testing."));
#else
        ApplyCaptureProtection(hwnd);
        StartProtectionWatchdog(hwnd);
#endif

        // Apply any saved transparency and keep it live as the settings slider moves.
        _vm.Settings.WindowOpacityChanged += OnWindowOpacityChanged;
        ApplyWindowOpacity(_vm.Settings.WindowOpacity);
    }

    // ---- Shell invisibility ---------------------------------------------------------------

    private Window? _hiddenOwner;

    /// <summary>
    /// Keeps the overlay out of Task Manager's "Apps" group (it stays listed under
    /// Background processes, like any process — that's OS-level and unavoidable).
    /// Task Manager promotes a process to "Apps" when it has a visible, unowned,
    /// non-tool top-level window, so we give this window a hidden owner and the
    /// WS_EX_TOOLWINDOW style. Must run before the window is first rendered.
    /// </summary>
    private void HideFromTaskManagerApps(IntPtr hwnd)
    {
        _hiddenOwner = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden,
        };
        // EnsureHandle creates the HWND without ever showing the owner. Set the native
        // owner directly (WPF's Window.Owner requires the owner to have been shown).
        IntPtr ownerHwnd = new WindowInteropHelper(_hiddenOwner).EnsureHandle();
        new WindowInteropHelper(this).Owner = ownerHwnd;

        Native.NativeWindowStyle.MakeToolWindow(hwnd);
    }

    // ---- Window transparency --------------------------------------------------------------

    private void OnWindowOpacityChanged(int opacityPercent) => ApplyWindowOpacity(opacityPercent);

    /// <summary>
    /// Fades the whole window via WPF's native <see cref="System.Windows.Window.Opacity"/>.
    /// This works because the window sets AllowsTransparency="True" (a per-pixel layered window).
    /// On this app's target (Windows 10 2004+ / Windows 11) that layered mode still honours
    /// WDA_EXCLUDEFROMCAPTURE, so the window stays hidden from screen captures while translucent
    /// on the physical display. The protection watchdog keeps verifying that guarantee, and the
    /// warning banner fires loudly if a given OS build ever fails to exclude a layered window.
    /// </summary>
    private void ApplyWindowOpacity(int opacityPercent)
    {
        Opacity = Math.Clamp(opacityPercent, 30, 100) / 100.0;
    }

    // ---- Screen-capture protection --------------------------------------------------------

    private void ApplyCaptureProtection(IntPtr hwnd)
    {
        CaptureProtectionResult result = _protection.Protect(hwnd);
        _vm.UpdateProtectionStatus(result);

        // Fail loudly: the banner (bound to ShowProtectionWarning) covers the visible warning.
        // Also record to the unified logger for diagnostics — never anything sensitive.
        string detail = $"success={result.Success}, fullyHidden={result.FullyHidden}, win32={result.Win32Error}";
        if (result.Success && result.FullyHidden)
            Services.Logging.Log.Info($"Capture protection active: {result.Message} ({detail})", "Protection");
        else
            Services.Logging.Log.Warning($"Capture protection degraded: {result.Message} ({detail})", null, "Protection");
    }

    /// <summary>
    /// Periodically re-verify protection and re-apply if the flag was lost (e.g. after a
    /// hide/reshow or a DWM state change). This is cheap and keeps the guarantee honest.
    /// </summary>
    private void StartProtectionWatchdog(IntPtr hwnd)
    {
        _protectionWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _protectionWatchdog.Tick += (_, _) =>
        {
            if (!_protection.IsStillProtected(hwnd))
            {
                CaptureProtectionResult result = _protection.Protect(hwnd);
                _vm.UpdateProtectionStatus(result);
            }
        };
        _protectionWatchdog.Start();
    }

    // ---- Global hotkeys -------------------------------------------------------------------

    private void SetupHotkeys(IntPtr hwnd)
    {
        _hotkeys.Attach(hwnd);
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        const uint ctrlShift = Native.NativeHotkey.MOD_CONTROL | Native.NativeHotkey.MOD_SHIFT;
        // Ctrl+Shift+Space -> toggle visibility. Ctrl+Shift+V -> push-to-talk.
        _hotkeys.Register("toggle", ctrlShift, VK_SPACE);
        _hotkeys.Register("voice", ctrlShift, VK_V);
    }

    private void OnHotkeyPressed(string name)
    {
        switch (name)
        {
            case "toggle":
                ToggleVisibility();
                break;
            case "voice":
                if (_vm.Chat.ToggleVoiceCommand.CanExecute(null))
                    _vm.Chat.ToggleVoiceCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Brings the overlay back to the user in response to a second launch attempt (the
    /// single-instance guard forwards the request here). Un-hides, un-minimizes, re-centers on
    /// the working area, and forces the window to the foreground.
    /// </summary>
    public void SurfaceFromOtherInstance()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        CenterOnWorkArea();

#if !DEBUG
        // Show()/reshow can drop the affinity — re-apply as we do on the hotkey toggle path.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _vm.UpdateProtectionStatus(_protection.Protect(hwnd));
#endif

        Activate();
        // Topmost flip forces the window to the top of the Z-order even when another app owns
        // the foreground (Windows won't hand focus over on Activate() alone).
        Topmost = false;
        Topmost = true;
    }

    /// <summary>Centers the window within the current monitor's working area (excludes taskbar).</summary>
    private void CenterOnWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    private void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
#if !DEBUG
            // Re-apply protection on reshow — affinity can need re-setting after hide.
            // Skipped in Debug builds where capture protection is disabled for testing.
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _vm.UpdateProtectionStatus(_protection.Protect(hwnd));
#endif
        }
    }

    // ---- Window chrome interactions -------------------------------------------------------

    // Manual title-bar drag. DragMove() enters the OS modal move loop, which triggers
    // Snap Layouts / Snap Assist hints — wrong for an overlay. Moving Left/Top ourselves
    // keeps the drag invisible to the shell, so no snap UI ever appears.
    private bool _dragging;
    private System.Windows.Point _dragCursorStart;   // cursor at drag start, in device pixels
    private double _dragWindowLeft;   // window position at drag start, in DIUs
    private double _dragWindowTop;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        _dragCursorStart = PointToScreen(e.GetPosition(this));
        _dragWindowLeft = Left;
        _dragWindowTop = Top;
        _dragging = ((UIElement)sender).CaptureMouse();
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging)
            return;

        // Work in device pixels, then convert the delta to DIUs so per-monitor DPI is honoured.
        System.Windows.Point cursor = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;

        System.Windows.Point delta = source.CompositionTarget.TransformFromDevice.Transform(
            new System.Windows.Point(cursor.X - _dragCursorStart.X, cursor.Y - _dragCursorStart.Y));
        Left = _dragWindowLeft + delta.X;
        Top = _dragWindowTop + delta.Y;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
            ((UIElement)sender).ReleaseMouseCapture();
    }

    private void TitleBar_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _dragging = false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.Chat.SendCommand.CanExecute(null))
        {
            _vm.Chat.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        // PasswordBox intentionally does not expose Password via binding; pull them here.
        _vm.Settings.APIKeyInput = APIKeyBox.Password;
        _vm.Settings.AzureSpeechKeyInput = AzureSpeechKeyBox.Password;
        _vm.Settings.SaveCommand.Execute(null);
        APIKeyBox.Clear();
        AzureSpeechKeyBox.Clear();
    }

    // ---- Shutdown -------------------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        // No tray icon (the overlay stays invisible everywhere in the shell), so ✕ really
        // quits. Hiding is the minimize button or the Ctrl+Shift+Space hotkey.
        _protectionWatchdog?.Stop();
        _hotkeys.Dispose();
        _capture.Dispose();
        _vm.Speech.Dispose();
        _hiddenOwner?.Close();
        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
