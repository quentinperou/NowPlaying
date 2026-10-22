using System.Numerics;
using System.Windows.Interop;
using Windows.UI.Composition;
using WinRT;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying;

/// <summary>
/// The frosted glass under the floating widget: a window of its own just under it, which shows what is behind it
/// blurred (the backdrop of Windows' acrylic, drawn by the compositor, also while the window is not active), within
/// the widget's own rounded corners. Not a blur of the widget window itself: Windows rounds such a window with its own
/// corners only, which come with a shadow that cannot be set nor removed. Nothing of WPF draws in it.
/// </summary>
internal sealed class FloatingGlass : IDisposable
{
    private static IntPtr s_queue; // the DispatcherQueue of the UI thread, made once

    private readonly HwndSource _window;
    private readonly Compositor _compositor;
    private readonly object _target; // kept alive with the window
    private readonly CompositionRoundedRectangleGeometry _shape;

    public FloatingGlass()
    {
        if (s_queue == IntPtr.Zero)
            CreateDispatcherQueueController(new DispatcherQueueOptions { dwSize = 12, threadType = 2, apartmentType = 2 }, out s_queue);

        // No redirection bitmap: only the compositor draws in it, see-through outside the rounded shape.
        _window = new HwndSource(new HwndSourceParameters("NowPlaying Glass")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
        });
        _window.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg != WM_MOUSEACTIVATE)
                return IntPtr.Zero;
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        });
        IntPtr hwnd = _window.Handle;
        int corners = DWMWCP_DONOTROUND, backdrop = 1, border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref backdrop, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

        _compositor = new Compositor();
        _compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(hwnd, false, out IntPtr pointer);
        var target = Windows.UI.Composition.Desktop.DesktopWindowTarget.FromAbi(pointer);
        _visual = _compositor.CreateSpriteVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _visual.Brush = _compositor.CreateHostBackdropBrush();
        _shape = _compositor.CreateRoundedRectangleGeometry();
        _visual.Clip = _compositor.CreateGeometricClip(_shape);
        target.Root = _visual;
        _target = target;
    }

    private readonly SpriteVisual _visual;

    /// <summary>
    /// How much of the blur shows, 0 to 1: Windows sets its strength, less lets the sharp background show through it
    /// (the "Blur" slider of the floating widget).
    /// </summary>
    public double Strength
    {
        set => _visual.Opacity = (float)Math.Clamp(value, 0, 1);
    }

    /// <summary>Just under the widget (<paramref name="widget"/>), on its rectangle and with its corners (screen pixels).</summary>
    public void Follow(IntPtr widget, RECT bounds, double radius, bool onTop)
    {
        IntPtr hwnd = _window.Handle;
        _shape.Size = new Vector2(bounds.Width, bounds.Height);
        _shape.CornerRadius = new Vector2((float)radius);
        SetWindowPos(hwnd, onTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(hwnd, widget, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
    }

    public void Show() => ShowWindow(_window.Handle, SW_SHOWNOACTIVATE);

    public void Hide() => ShowWindow(_window.Handle, SW_HIDE);

    public void Dispose()
    {
        (_target as IDisposable)?.Dispose();
        _compositor.Dispose();
        _window.Dispose();
    }
}
