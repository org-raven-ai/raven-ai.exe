using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using RavenAI.Services;
using RavenAI.Services.Voice;
using RavenAI.ViewModels;
using Forms = System.Windows.Forms;

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

    private Forms.NotifyIcon? _tray;
    private DispatcherTimer? _protectionWatchdog;
    private bool _reallyExit;

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

        ApplyCaptureProtection(hwnd);
        SetupHotkeys(hwnd);
        SetupTray();
        StartProtectionWatchdog(hwnd);

        // Apply any saved transparency and keep it live as the settings slider moves.
        _vm.Settings.WindowOpacityChanged += OnWindowOpacityChanged;
        ApplyWindowOpacity(_vm.Settings.WindowOpacity);
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
        // Also log to the debugger for diagnostics — but never anything sensitive.
        System.Diagnostics.Debug.WriteLine($"[raven_ai] Capture protection: {result.Message} " +
                                           $"(success={result.Success}, fullyHidden={result.FullyHidden}, " +
                                           $"win32={result.Win32Error})");
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
            // Re-apply protection on reshow — affinity can need re-setting after hide.
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _vm.UpdateProtectionStatus(_protection.Protect(hwnd));
        }
    }

    // ---- Tray icon ------------------------------------------------------------------------

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "raven_ai",
        };
        _tray.DoubleClick += (_, _) => ToggleVisibility();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / Hide", null, (_, _) => ToggleVisibility());
        menu.Items.Add("Quit", null, (_, _) => { _reallyExit = true; Close(); });
        _tray.ContextMenuStrip = menu;
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
        // PasswordBox intentionally does not expose Password via binding; pull it here.
        _vm.Settings.APIKeyInput = APIKeyBox.Password;
        _vm.Settings.SaveCommand.Execute(null);
        APIKeyBox.Clear();
    }

    // ---- Shutdown -------------------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        // Close button / hotkey hides to tray instead of quitting, unless "Quit" was chosen.
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _protectionWatchdog?.Stop();
        _hotkeys.Dispose();
        _capture.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
