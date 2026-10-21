using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NowPlaying.Services;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying;

/// <summary>
/// The shadow of the floating widget, as dark as the user asks: a window of its own just under the widget, which the
/// mouse goes through. Drawn once (a layered window, redrawn only when the widget's size or the density changes), only
/// around the widget: the widget is see-through, the shape casting the shadow must not show through it. Windows'
/// own shadow cannot be set (and comes with its rounded corners, those of the frosted glass).
/// </summary>
internal sealed class FloatingShadow : Window
{
    /// <summary>Room around the widget for the blur of the shadow, in pixels at 100 %.</summary>
    public const double Spread = 24;

    private readonly Grid _root = new();
    private readonly Border _shape = new() { Background = Brushes.Black, Margin = new Thickness(Spread) };
    // Even all around (barely lower), unlike the shadow of Windows, which falls far below.
    private readonly DropShadowEffect _effect = new() { BlurRadius = 18, ShadowDepth = 1.5, Direction = 270, Color = Colors.Black };
    private IntPtr _hwnd;

    public FloatingShadow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "NowPlaying Shadow";
        _shape.Effect = _effect;
        _root.Children.Add(_shape);
        Content = _root;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Not in Alt+Tab, never activated, and the mouse goes through it to the windows below.
            long style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
        };
    }

    /// <summary>The widget's size and corners (pixels at 100 %), and how dark the shadow is (0 to 1).</summary>
    public void Shape(double width, double height, double radius, double opacity)
    {
        Width = width + 2 * Spread;
        Height = height + 2 * Spread;
        _shape.Width = width;
        _shape.Height = height;
        _shape.CornerRadius = new CornerRadius(radius);
        _effect.Opacity = opacity;
        // Only around the widget.
        _root.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, Width, Height)),
            new RectangleGeometry(new Rect(Spread, Spread, width, height), radius, radius));
    }

    /// <summary>Just under the widget (<paramref name="widget"/>), around its rectangle (screen pixels).</summary>
    public void Follow(IntPtr widget, RECT bounds, bool onTop)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        int spread = (int)Math.Round(Spread * GetDpiForWindow(widget) / 96.0);
        Topmost = onTop;
        SetWindowPos(_hwnd, widget, bounds.Left - spread, bounds.Top - spread, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
