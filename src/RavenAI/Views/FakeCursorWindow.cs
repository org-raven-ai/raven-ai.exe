using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RavenAI.Native;

// WinForms is imported process-wide (for the tray API), so these names are ambiguous — pin to
// WPF. (Color and HorizontalAlignment are already aliased globally in GlobalUsings.cs.)
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;

namespace RavenAI.Views;

/// <summary>
/// A tiny topmost, click-through, layered window that paints the interactive-mode fake cursor.
///
/// The arrow must live in its own top-level window rather than on a canvas inside the overlay:
/// combo dropdowns and tooltips are separate popup HWNDs that Windows stacks above the overlay,
/// so a cursor drawn inside the overlay disappears underneath an open dropdown. Every move
/// re-asserts this window at the top of the topmost band so it stays above popups that opened
/// after it. Like the overlay, it is excluded from screen capture (Release builds only).
/// </summary>
public sealed class FakeCursorWindow : Window
{
    private readonly ScaleTransform _scale = new(1.0, 1.0);
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
        // Generously sized so the arrow never clips at large pointer sizes / DPI scales.
        Width = 80;
        Height = 80;
        Left = 0;
        Top = 0;

        // The same vector arrow the overlay used to paint: tip at (0,0), authored for a
        // 32px cursor; the scale transform sizes it to match the real pointer.
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

    /// <summary>Puts the arrow's tip at the given physical-pixel screen point, above all popups.</summary>
    public void MoveTo(int xPixels, int yPixels)
    {
        if (_hwnd != IntPtr.Zero)
            NativeWindowStyle.MoveTopmost(_hwnd, xPixels, yPixels);
    }
}
