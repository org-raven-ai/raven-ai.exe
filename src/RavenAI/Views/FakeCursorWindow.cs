using System.Windows;
using System.Windows.Controls;
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
/// A topmost, click-through, layered window that paints the interactive-mode fake cursor.
///
/// The arrow must live in its own top-level window rather than on a canvas inside the overlay:
/// combo dropdowns and tooltips are separate popup HWNDs that Windows stacks above the overlay,
/// so a cursor drawn inside the overlay disappears underneath an open dropdown.
///
/// The window spans the whole virtual screen (mirroring the overlay's bounds) and the arrow is
/// positioned with a render transform. Raw-input mouse moves arrive at up to ~1000 Hz, so the
/// per-move work must stay this cheap — moving the HWND itself with SetWindowPos per event
/// backlogs the message queue and the cursor visibly lags and drifts. The window's z-order is
/// re-asserted only on the rare occasions something could stack above it (interactive entry, a
/// popup opening). Like the overlay, it is excluded from screen capture (Release builds only).
/// </summary>
public sealed class FakeCursorWindow : Window
{
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly TranslateTransform _translate = new(0, 0);
    private IntPtr _hwnd;

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

        // Mirror the overlay's bounds exactly, so fake-cursor canvas coordinates map 1:1.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // The same vector arrow the overlay used to paint: tip at (0,0), authored for a
        // 32px cursor. Scale sizes it to the real pointer; translate follows the fake cursor.
        var arrow = new Path
        {
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
            StrokeThickness = 1,
            Data = Geometry.Parse("M 0,0 L 0,17 L 4.5,13 L 7.5,19.5 L 10,18.3 L 7,12 L 12.5,12 Z"),
        };
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);
        arrow.RenderTransform = group;

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(arrow);
        Content = canvas;
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
    /// Puts the arrow's tip at the given point (in the overlay canvas's DIU coordinates, which
    /// map 1:1 onto this window). Pure render-transform update — cheap enough for raw-input rate.
    /// </summary>
    public void MoveTo(Point position)
    {
        _translate.X = position.X;
        _translate.Y = position.Y;
    }

    /// <summary>
    /// Re-asserts this window at the top of the topmost band, above popups that opened after it
    /// (WPF dropdowns/tooltips of a topmost window are themselves topmost).
    /// </summary>
    public void BumpAbovePopups()
    {
        if (_hwnd != IntPtr.Zero)
            NativeWindowStyle.BumpTopmost(_hwnd);
    }
}
