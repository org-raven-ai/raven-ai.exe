using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RavenAI.Native;
using RavenAI.Services;
using RavenAI.Services.Overlay;
using RavenAI.Services.Voice;
using RavenAI.ViewModels;

// WinForms is imported (for the tray API), so these type names are ambiguous — pin them to WPF.
using Point = System.Windows.Point;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using Control = System.Windows.Controls.Control;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace RavenAI.Views;

/// <summary>
/// The one and only window — a full-virtual-screen transparent, always-click-through overlay.
/// The existing assistant UI lives in a floating card on <see cref="RootCanvas"/>. In
/// pass-through mode every click/key falls to the app underneath; interactive mode
/// (Ctrl+Shift+I) captures the mouse via a low-level hook and drives the painted fake cursor,
/// which does its own hit-testing/dispatch against the card so it can be clicked and typed into.
///
/// Once the HWND exists we also apply screen-capture exclusion, verify it, and keep it applied.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ScreenCaptureProtectionService _protection;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly NAudioCapture _capture;
    private readonly InteractiveModeController _interactive;

    private DispatcherTimer? _protectionWatchdog;
    private bool _panelPlaced;

    // Fake-cursor position, in RootCanvas (device-independent) coordinates.
    private Point _fakeCursor;
    // The card whose title bar the fake cursor is currently dragging (null when not dragging).
    private Border? _draggingCard;
    // The button pressed by the fake cursor, invoked on release if still under the cursor.
    private ButtonBase? _pressedButton;

    // Fake cursor is drawn as a vector arrow (crisp at any size, unlike the raster OS cursor). Its
    // geometry is authored for a 32px cursor with the tip at (0,0); a ScaleTransform sizes it.

    // Fine-tune the fake cursor's size relative to the real one. The vector arrow glyph (plus its
    // scaled outline stroke) is larger, relative to the cursor box, than the real Windows arrow, so
    // trim it (0.71) to match. This is a constant proportion; the DPI/pointer-size scaling is
    // handled separately.
    private const double CursorSizeScale = 0.71;

    // Movement multiplier for the fake cursor: 1.0 maps the raw device delta 1:1; lower is slower,
    // higher is faster. Tuned to 0.8 so it tracks a bit slower than raw, closer to the real pointer.
    private const double CursorSpeedMultiplier = 0.8;

    // Drag-to-select state while the fake cursor holds the button down over a text box.
    private TextBox? _selectingTextBox;
    private int _selectionAnchor;

    // Virtual-key codes for the hotkeys.
    private const uint VK_SPACE = 0x20;
    private const uint VK_V = 0x56;
    private const uint VK_I = 0x49;

    public MainWindow(
        MainViewModel vm,
        ScreenCaptureProtectionService protection,
        GlobalHotkeyService hotkeys,
        NAudioCapture capture,
        InteractiveModeController interactive)
    {
        _vm = vm;
        _protection = protection;
        _hotkeys = hotkeys;
        _capture = capture;
        _interactive = interactive;

        DataContext = _vm;
        InitializeComponent();

        // Keep the log panel pinned to the newest entry as errors/events stream in.
        _vm.Log.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                LogScroll.ScrollToEnd();
        };

        // Keep the staging window pinned to the newest committed transcription.
        _vm.Speech.Staged.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                StagingScroll.ScrollToEnd();
        };

        // Cover the whole virtual desktop (all monitors). The card is positioned within it.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += (_, _) => PlaceCardsTopRight();
    }

    /// <summary>
    /// Places the two floating cards near the top-right of the primary working area (once): the chat
    /// card on the right, the transcription-staging card immediately to its left.
    /// </summary>
    private void PlaceCardsTopRight()
    {
        if (_panelPlaced)
            return;
        _panelPlaced = true;

        const double gap = 12;
        var wa = SystemParameters.WorkArea;
        // WorkArea is in screen coordinates; the canvas origin is the window's top-left.
        double top = wa.Top + 24 - Top;
        double chatLeft = wa.Right - ChatCard.Width - 24 - Left;

        Canvas.SetLeft(ChatCard, chatLeft);
        Canvas.SetTop(ChatCard, top);
        Canvas.SetLeft(StagingCard, chatLeft - StagingCard.Width - gap);
        Canvas.SetTop(StagingCard, top);

        // The first-run gate floats centered in the primary work area (upper third).
        Canvas.SetLeft(GateCard, (wa.Left + wa.Right - GateCard.Width) / 2 - Left);
        Canvas.SetTop(GateCard, wa.Top + Math.Max(24, wa.Height * 0.16) - Top);
    }

    /// <summary>
    /// The HWND exists here (not in the constructor). This is the correct place to apply
    /// SetWindowDisplayAffinity, make the overlay click-through, register global hotkeys, and
    /// wire up interactive mode.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        HideFromTaskManagerApps(hwnd);
        // Keep the overlay click-through at all times: pass-through mode relies on it, and
        // interactive mode drives its own fake cursor rather than OS hit-testing.
        Native.NativeWindowStyle.SetClickThrough(hwnd, true);
        SetupHotkeys(hwnd);
        SetupInteractiveMode(hwnd);
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
        // Ctrl+Shift+I -> toggle interactive mode.
        _hotkeys.Register("toggle", ctrlShift, VK_SPACE);
        _hotkeys.Register("voice", ctrlShift, VK_V);
        _hotkeys.Register("interactive", ctrlShift, VK_I);
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
            case "interactive":
                ToggleInteractive();
                break;
        }
    }

    // ---- Interactive mode -----------------------------------------------------------------

    private void SetupInteractiveMode(IntPtr hwnd)
    {
        HwndSource source = HwndSource.FromHwnd(hwnd)!;
        _interactive.Initialize(hwnd, source);
        _interactive.MouseMoved += OnInteractiveMouseMoved;
        _interactive.ButtonPressed += OnInteractiveButtonPressed;
        _interactive.ButtonReleased += OnInteractiveButtonReleased;
        _interactive.WheelScrolled += OnInteractiveWheel;
    }

    private void ToggleInteractive()
    {
        if (_interactive.IsActive)
            ExitInteractive();
        else
            EnterInteractive();
    }

    private void EnterInteractive()
    {
        // The overlay must be visible to interact with it.
        if (!IsVisible)
            Show();

        // Size the painted arrow to match the real cursor at the current DPI + pointer-size setting.
        UpdateCursorScale();

        // Start the fake cursor where the real pointer currently is.
        Native.NativeCursor.POINT p = Native.NativeCursor.GetPosition();
        _fakeCursor = ClampToCanvas(RootCanvas.PointFromScreen(new Point(p.X, p.Y)));
        MoveFakeCursor();

        _draggingCard = null;
        _pressedButton = null;
        _selectingTextBox = null;
        _vm.IsInteractive = true;

        // Capture input + freeze the real cursor BEFORE we take the foreground, so the
        // controller records the app the user came from (to restore focus on exit).
        _interactive.Enter();

        // Take keyboard focus so typing lands in the chat box while interactive.
        Activate();
    }

    private void ExitInteractive()
    {
        if (!_interactive.IsActive)
            return;

        _interactive.Exit();   // releases the cursor clip and restores the previous foreground app
        _draggingCard = null;
        _pressedButton = null;
        _selectingTextBox = null;
        _vm.IsInteractive = false;
    }

    /// <summary>
    /// Fake cursor moved: map the raw device delta 1:1 (times the speed multiplier), convert to
    /// DIU, move the cursor, then drag a card / extend a text selection if one is active.
    /// </summary>
    private void OnInteractiveMouseMoved(int dxPixels, int dyPixels)
    {
        PresentationSource? src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is null)
            return;

        Matrix toDiu = src.CompositionTarget.TransformFromDevice;
        double dx = dxPixels * CursorSpeedMultiplier * toDiu.M11;
        double dy = dyPixels * CursorSpeedMultiplier * toDiu.M22;

        _fakeCursor = ClampToCanvas(new Point(_fakeCursor.X + dx, _fakeCursor.Y + dy));
        MoveFakeCursor();

        if (_draggingCard is not null)
        {
            MoveCardBy(_draggingCard, dx, dy);
            return;
        }

        if (_selectingTextBox is not null)
            ExtendTextSelection(_selectingTextBox);
    }

    private void OnInteractiveButtonPressed(OverlayMouseButton button)
    {
        if (button != OverlayMouseButton.Left)
            return;

        DependencyObject? hit = HitTest(_fakeCursor);
        if (hit is null)
            return;

        // A button under the cursor wins over a title-bar drag (the title bar hosts buttons too).
        _pressedButton = FindAncestor<ButtonBase>(hit);
        if (_pressedButton is not null)
            return;

        // A settings-tab header: TabItem is not a ButtonBase, so the button dispatch above never
        // fires for it — select it explicitly (real tab controls also switch on mouse-down).
        if (FindAncestor<TabItem>(hit) is TabItem tab)
        {
            tab.IsSelected = true;
            return;
        }

        // Give focus to a clicked input control so the keyboard can type into it, and — for a text
        // box — drop the caret at the clicked character and arm drag-to-select.
        Control? input = FindInputControl(hit);
        if (input is not null)
        {
            input.Focus();
            Keyboard.Focus(input);
            if (input is TextBox textBox)
            {
                Point local = RootCanvas.TranslatePoint(_fakeCursor, textBox);
                int index = textBox.GetCharacterIndexFromPoint(local, snapToText: true);
                if (index >= 0)
                {
                    textBox.CaretIndex = index;
                    _selectingTextBox = textBox;
                    _selectionAnchor = index;
                }
            }
            return;
        }

        // Otherwise, pressing a card's title bar starts dragging that card.
        if (IsWithin(hit, ChatTitleBar))
            _draggingCard = ChatCard;
        else if (IsWithin(hit, StagingTitleBar))
            _draggingCard = StagingCard;
        else if (IsWithin(hit, GateTitleBar))
            _draggingCard = GateCard;
    }

    private void OnInteractiveButtonReleased(OverlayMouseButton button)
    {
        if (button != OverlayMouseButton.Left)
            return;

        _selectingTextBox = null;

        if (_draggingCard is not null)
        {
            _draggingCard = null;
            return;
        }

        // Invoke the button only if the cursor is still over the same one it pressed.
        if (_pressedButton is not null)
        {
            DependencyObject? hit = HitTest(_fakeCursor);
            if (hit is not null && FindAncestor<ButtonBase>(hit) == _pressedButton)
                InvokeButton(_pressedButton);
        }
        _pressedButton = null;
    }

    /// <summary>
    /// Routes a wheel notch to whatever is under the fake cursor by raising a real
    /// <see cref="UIElement.MouseWheelEvent"/>, so nested scroll viewers, list boxes and combo
    /// popups all scroll exactly as they would under the real pointer. If nothing in the route
    /// handled it, fall back to nudging the nearest <see cref="ScrollViewer"/> directly.
    /// </summary>
    private void OnInteractiveWheel(int delta)
    {
        DependencyObject? hit = HitTest(_fakeCursor);
        if (hit is null || FindAncestor<UIElement>(hit) is not UIElement target)
            return;

        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = target,
        };
        target.RaiseEvent(args);

        // One wheel notch is 120 units; scroll ~48 DIU per notch, wheel-up scrolls up.
        if (!args.Handled && FindAncestor<ScrollViewer>(hit) is ScrollViewer scroller)
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - delta / 120.0 * 48.0);
    }

    // ---- Fake-cursor helpers --------------------------------------------------------------

    private void MoveFakeCursor()
    {
        // The arrow's tip is authored at (0,0), so the translate is simply the cursor position.
        FakeCursorTransform.X = _fakeCursor.X;
        FakeCursorTransform.Y = _fakeCursor.Y;
    }

    // Scales the vector arrow to match the real cursor's on-screen size. The real cursor's physical
    // size is the pointer-size base size (logical, from the registry) times the monitor DPI scale
    // (Windows applies this at draw time; the base size alone omits it). The arrow is authored for a
    // 32px cursor and drawn in this window's DIU, which the window renders to physical pixels at its
    // composition scale, so:
    //   scale = realPhysical / 32 / windowScale = baseSize * monitorScale / (32 * windowScale)
    // With the size knob at 1.0 this equals the real cursor's size. (monitorScale and windowScale
    // are equal on a single monitor under per-monitor-v2 awareness, so DPI is applied exactly once.)
    private void UpdateCursorScale()
    {
        PresentationSource? src = PresentationSource.FromVisual(this);
        double windowScale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        if (windowScale <= 0) windowScale = 1.0;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        double monitorScale = NativePointer.GetWindowDpiScale(hwnd);
        int baseSize = NativePointer.GetCursorBaseSize();

        double scale = baseSize * monitorScale / (32.0 * windowScale) * CursorSizeScale;
        FakeCursorScale.ScaleX = scale;
        FakeCursorScale.ScaleY = scale;

        // Diagnostic: surfaces the real numbers in the Logs panel so cursor sizing can be verified.
        Services.Logging.Log.Info(
            $"Cursor scale: windowScale={windowScale:0.##} monitorDpiScale={monitorScale:0.##} " +
            $"baseSize={baseSize} sizeKnob={CursorSizeScale:0.##} -> scale={scale:0.###}", "Cursor");
    }

    // Extends the active text-box selection from the press anchor to the character under the cursor.
    private void ExtendTextSelection(TextBox textBox)
    {
        Point local = RootCanvas.TranslatePoint(_fakeCursor, textBox);
        int index = textBox.GetCharacterIndexFromPoint(local, snapToText: true);
        if (index < 0)
            return;
        textBox.Select(Math.Min(_selectionAnchor, index), Math.Abs(index - _selectionAnchor));
    }

    private Point ClampToCanvas(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(0, RootCanvas.ActualWidth)),
        Math.Clamp(p.Y, 0, Math.Max(0, RootCanvas.ActualHeight)));

    private void MoveCardBy(Border card, double dx, double dy)
    {
        // Keep a sliver of the card on-screen so it can never be lost off an edge.
        const double margin = 60;
        double left = Math.Clamp(Canvas.GetLeft(card) + dx,
            -(card.ActualWidth - margin), Math.Max(0, RootCanvas.ActualWidth - margin));
        double top = Math.Clamp(Canvas.GetTop(card) + dy,
            0, Math.Max(0, RootCanvas.ActualHeight - margin));
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
    }

    /// <summary>Hit-tests the visual tree at a RootCanvas point; returns the top-most hit visual.</summary>
    private DependencyObject? HitTest(Point point)
    {
        HitTestResult? result = VisualTreeHelper.HitTest(RootCanvas, point);
        return result?.VisualHit;
    }

    private static void InvokeButton(ButtonBase button)
    {
        AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(button);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            invoke.Invoke();                       // Button / plain command buttons
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
            toggle.Toggle();                       // CheckBox / ToggleButton
    }

    /// <summary>Finds the nearest input control (text box, password box, combo box) at or above a node.</summary>
    private static Control? FindInputControl(DependencyObject node)
    {
        for (DependencyObject? cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is TextBoxBase or PasswordBox or ComboBox)
                return (Control)cur;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (cur is T match)
                return match;
        }
        return null;
    }

    private static bool IsWithin(DependencyObject node, DependencyObject container)
    {
        for (DependencyObject? cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, container))
                return true;
        }
        return false;
    }

    // ---- Window management ----------------------------------------------------------------

    /// <summary>
    /// Brings the overlay back to the user in response to a second launch attempt (the
    /// single-instance guard forwards the request here). Un-hides, un-minimizes, re-applies the
    /// click-through style, and forces the window to the foreground.
    /// </summary>
    public void SurfaceFromOtherInstance()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Native.NativeWindowStyle.SetClickThrough(hwnd, true);

#if !DEBUG
        // Show()/reshow can drop the affinity — re-apply as we do on the hotkey toggle path.
        _vm.UpdateProtectionStatus(_protection.Protect(hwnd));
#endif

        Activate();
        // Topmost flip forces the window to the top of the Z-order even when another app owns
        // the foreground (Windows won't hand focus over on Activate() alone).
        Topmost = false;
        Topmost = true;
    }

    private void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (_interactive.IsActive)
                ExitInteractive();
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Native.NativeWindowStyle.SetClickThrough(hwnd, true);
#if !DEBUG
            // Re-apply protection on reshow — affinity can need re-setting after hide.
            // Skipped in Debug builds where capture protection is disabled for testing.
            _vm.UpdateProtectionStatus(_protection.Protect(hwnd));
#endif
        }
    }

    /// <summary>Esc leaves interactive mode (works even when a child input has focus).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _interactive.IsActive)
        {
            ExitInteractive();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // ---- Window chrome interactions -------------------------------------------------------

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_interactive.IsActive)
            ExitInteractive();
        Hide();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- First-run provider gate ------------------------------------------------------------

    /// <summary>
    /// Validates the gate's Base URL + API key: pulls the secret out of whichever key box is
    /// visible (PasswordBox intentionally has no binding) and hands it to the view model, which
    /// persists the pair and probes the provider's /models endpoint.
    /// </summary>
    private void GateValidate_Click(object sender, RoutedEventArgs e)
    {
        _vm.Settings.APIKeyInput =
            GateKeyPeek.IsChecked == true ? GateKeyPlainBox.Text : GateKeyBox.Password;
        _vm.Settings.ValidateGateCommand.Execute(null);
    }

    /// <summary>
    /// Commits the gate: stores any optional channel keys typed in, saves the settings, and
    /// dismisses the gate so the overlay cards appear. Enabled only after a successful Validate.
    /// </summary>
    private void GateUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.Settings.GateKeyValidated)
            return;

        _vm.Settings.AzureSpeechKeyInput = GateAzureKeyBox.Password;
        _vm.Settings.WebSearchKeyInput = GateTavilyKeyBox.Password;
        _vm.Settings.SaveCommand.Execute(null);

        GateKeyBox.Clear();
        GateKeyPlainBox.Clear();
        GateAzureKeyBox.Clear();
        GateTavilyKeyBox.Clear();
        _vm.IsGateOpen = false;
    }

    /// <summary>Peek on: mirror the secret into the visible plain box.</summary>
    private void GatePeek_Checked(object sender, RoutedEventArgs e)
    {
        GateKeyPlainBox.Text = GateKeyBox.Password;
        GateKeyPlainBox.Visibility = Visibility.Visible;
        GateKeyBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>Peek off: mirror any edits back into the masked box.</summary>
    private void GatePeek_Unchecked(object sender, RoutedEventArgs e)
    {
        GateKeyBox.Password = GateKeyPlainBox.Text;
        GateKeyBox.Visibility = Visibility.Visible;
        GateKeyPlainBox.Visibility = Visibility.Collapsed;
    }

    private void GateGetKey_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://platform.openai.com/api-keys") { UseShellExecute = true });
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        // PasswordBox intentionally does not expose Password via binding; pull them here.
        _vm.Settings.APIKeyInput = APIKeyBox.Password;
        _vm.Settings.AzureSpeechKeyInput = AzureSpeechKeyBox.Password;
        _vm.Settings.WebSearchKeyInput = WebSearchKeyBox.Password;
        _vm.Settings.SaveCommand.Execute(null);
        APIKeyBox.Clear();
        AzureSpeechKeyBox.Clear();
        WebSearchKeyBox.Clear();
    }

    // ---- Shutdown -------------------------------------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        // No tray icon (the overlay stays invisible everywhere in the shell), so ✕ really
        // quits. Hiding is the minimize button or the Ctrl+Shift+Space hotkey.
        _protectionWatchdog?.Stop();
        _interactive.Dispose();
        _hotkeys.Dispose();
        _capture.Dispose();
        _vm.Speech.Dispose();
        _hiddenOwner?.Close();
        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
