using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RavenAI.Native;

// WinForms is imported process-wide (for the tray API), so these names are ambiguous — pin to
// WPF. (Color and HorizontalAlignment are already aliased globally in GlobalUsings.cs.)
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace RavenAI.Views;

/// <summary>
/// A tiny topmost, click-through, layered window that paints the interactive-mode fake cursor.
///
/// The arrow must live in its own top-level window rather than on a canvas inside the overlay:
/// combo dropdowns and tooltips are separate popup HWNDs that Windows stacks above the overlay,
/// so a cursor drawn inside the overlay disappears underneath an open dropdown.
///
/// Presentation is tuned for a 120Hz-class display:
///  - The window is small, so its layered (UpdateLayeredWindow) surface is trivial — unlike a
///    virtual-screen-sized layered window, whose 4K re-render throttled motion below the
///    monitor's refresh rate.
///  - Raw-input moves (up to ~1000 Hz) only record the target position; the window is actually
///    repositioned at most once per composed frame, from CompositionTarget.Rendering. Moving the
///    HWND synchronously per input event backlogged the message queue (start delay + drift).
///  - Each per-frame reposition inserts at the top of the topmost band, keeping the arrow above
///    any dropdown popup while it moves.
///
/// Like the overlay, the window is excluded from screen capture (Release builds only).
/// </summary>
public sealed class FakeCursorWindow : Window
{
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private IntPtr _hwnd;

    // Latest requested tip position in physical screen pixels, applied on the next frame.
    private int _targetX;
    private int _targetY;
    private bool _moveRequested;

    public FakeCursorWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Generously sized so the arrow never clips at large pointer sizes / DPI scales.
        Width = 100;
        Height = 100;
        Left = 0;
        Top = 0;

        // The same vector arrow the overlay used to paint: tip at (0,0) = the window origin,
        // authored for a 32px cursor; the scale transform sizes it to match the real pointer.
        Content = new Path
        {
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
            StrokeThickness = 1,
            Data = Geometry.Parse("M 0,0 L 0,17 L 4.5,13 L 7.5,19.5 L 10,18.3 L 7,12 L 12.5,12 Z"),
            RenderTransform = _scale,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // The per-frame pump only runs while the cursor is on screen.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                CompositionTarget.Rendering += OnFrameRendering;
            else
                CompositionTarget.Rendering -= OnFrameRendering;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Click-through + tool window, like the overlay: never hit-testable, never in Alt+Tab.
        NativeWindowStyle.SetClickThrough(_hwnd, true);
        NativeWindowStyle.MakeToolWindow(_hwnd);
#if !DEBUG
        // The cursor must vanish from captures together with the rest of the overlay.
        if (!NativeCaptureProtection.Apply(
                _hwnd, NativeCaptureProtection.WDA_EXCLUDEFROMCAPTURE, out int error))
        {
            Services.Logging.Log.Warning(
                $"Fake-cursor window capture exclusion failed (win32={error})", null, "Protection");
        }
#endif
    }

    /// <summary>Scales the arrow to match the real cursor's on-screen size.</summary>
    public void SetScale(double scale)
    {
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    /// <summary>
    /// Requests the arrow's tip at the given physical-pixel screen point. Cheap enough for
    /// raw-input rate — the window itself moves on the next composed frame.
    /// </summary>
    public void MoveTo(Point screenPixels)
    {
        _targetX = (int)Math.Round(screenPixels.X);
        _targetY = (int)Math.Round(screenPixels.Y);
        _moveRequested = true;
    }

    /// <summary>
    /// Re-asserts this window at the top of the topmost band, above popups that opened after it
    /// (WPF dropdowns/tooltips of a topmost window are themselves topmost). Movement does this
    /// implicitly each frame; this covers popups opening while the cursor is stationary.
    /// </summary>
    public void BumpAbovePopups()
    {
        if (_hwnd != IntPtr.Zero)
            NativeWindowStyle.BumpTopmost(_hwnd);
    }

    private void OnFrameRendering(object? sender, EventArgs e)
    {
        if (!_moveRequested || _hwnd == IntPtr.Zero)
            return;
        _moveRequested = false;
        NativeWindowStyle.MoveTopmost(_hwnd, _targetX, _targetY);

#if DEBUG
        // Rate diagnostic (Logs panel): applied cursor moves per second while in motion.
        // ≈ min(display refresh, mouse polling rate) when the mouse is moving continuously.
        _movesApplied++;
        long now = Environment.TickCount64;
        if (now - _rateWindowStart >= 1000)
        {
            if (_rateWindowStart != 0)
                Services.Logging.Log.Info($"Cursor moves applied: {_movesApplied}/s", "Cursor");
            _rateWindowStart = now;
            _movesApplied = 0;
        }
#endif
    }

#if DEBUG
    private int _movesApplied;
    private long _rateWindowStart;
#endif
}
