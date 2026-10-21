using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using NowPlaying.Services;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying;

/// <summary>
/// The floating widget: the same widget as in the taskbar, anywhere on the screen, above the other windows. Dragged
/// with the mouse (from anywhere but its progress and volume bars: a button pressed then moved is a drag, not a
/// click), its place is kept in the settings. Hidden over full-screen apps, like the taskbar widget. It runs in the app
/// itself: it is tied to no other window.
/// </summary>
public partial class FloatingWidget : Window
{
    private const int AppBarMessage = WM_APP + 3;
    private const double DragThreshold = 4;

    private IntPtr _hwnd;
    private bool _appBar;
    private bool _fullScreen;
    private bool _wanted;

    // A press that may become a drag: where the pointer and the window were (screen pixels).
    private POINT? _pressedAt;
    private RECT _windowAt;
    private bool _dragging;

    public FloatingWidget()
    {
        InitializeComponent();
        PreviewMouseLeftButtonDown += Pressed;
        PreviewMouseMove += Dragged;
        PreviewMouseLeftButtonUp += Released;
        LostMouseCapture += (_, _) => EndDrag();
        // The glass and the shadow go and come with it, and take its size.
        IsVisibleChanged += (_, _) => UpdateUnderlays();
        SizeChanged += (_, _) => UpdateUnderlays();
    }

    /// <summary>Its new place after a drag, in screen pixels (top-left corner).</summary>
    public event EventHandler<(int X, int Y)>? PlaceChanged;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Not in Alt+Tab, never activated by a click (the app in front keeps the keyboard).
        long style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        KeepTransparent();
        // An app bar that takes no room: Windows tells it when an app goes full screen.
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = AppBarMessage };
        _appBar = SHAppBarMessage(ABM_NEW, ref data) != IntPtr.Zero;
    }

    /// <summary>See-through where nothing is drawn (see <see cref="SeeThrough"/>).</summary>
    public void KeepTransparent() => SeeThrough.Apply(this, _hwnd);

    private bool _blur;
    private double _radius;
    private double _shadowOpacity;
    private FloatingGlass? _glass;
    private FloatingShadow? _shadow;

    /// <summary>
    /// Its look. <paramref name="blur"/>: frosted glass, what is behind the widget shows blurred (Windows sets how much),
    /// under the widget's veil, within its corners (<paramref name="radius"/>, pixels at 100 %).
    /// <paramref name="shadow"/>: the darkness of its shadow, 0 for none. Both are windows of their own just under it.
    /// </summary>
    public void SetLook(bool blur, int radius, double shadow)
    {
        _blur = blur;
        _radius = radius;
        _shadowOpacity = shadow;
        UpdateUnderlays();
    }

    // The glass and the shadow: shown with the widget, of its size, just under it (the shadow under the glass).
    private void UpdateUnderlays()
    {
        if (_blur && IsVisible)
        {
            _glass ??= new FloatingGlass();
            _glass.Show();
        }
        else
            _glass?.Hide();

        if (_shadowOpacity > 0 && IsVisible)
        {
            _shadow ??= new FloatingShadow();
            _shadow.Shape(ActualWidth, ActualHeight, _radius, _shadowOpacity);
            if (!_shadow.IsVisible)
                _shadow.Show();
        }
        else
            _shadow?.Hide();
        FollowUnderlays();
    }

    private void FollowUnderlays()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible)
            return;
        GetWindowRect(_hwnd, out var bounds);
        if (_shadow is { IsVisible: true })
            _shadow.Follow(_hwnd, bounds, _onTop);
        if (_blur)
            _glass?.Follow(_hwnd, bounds, _radius * GetDpiForWindow(_hwnd) / 96.0, _onTop);
    }

    private bool _onTop = true;

    /// <summary>Above the other windows; off, under them (a widget on the desktop: never activated, a click does not raise it).</summary>
    public bool OnTop
    {
        get => _onTop;
        set
        {
            if (value == _onTop)
                return;
            _onTop = value;
            Topmost = value;
            if (_hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, Layer, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private IntPtr Layer => _onTop ? HWND_TOPMOST : HWND_NOTOPMOST;

    /// <summary>It cannot be dragged (no move by mistake).</summary>
    public bool Locked { get; set; }

    /// <summary>Whether it should be on screen (the floating widget on, a track to show): it still hides over full screen.</summary>
    public void SetWanted(bool wanted)
    {
        _wanted = wanted;
        UpdateVisibility();
    }

    /// <summary>
    /// Puts it at (<paramref name="x"/>, <paramref name="y"/>), screen pixels; at the bottom right of the main screen
    /// when null, or when that place is on no screen any more (a screen unplugged). Its size is only known once shown:
    /// until then the place is kept for the first showing.
    /// </summary>
    public void PlaceAt(int? x, int? y)
    {
        _place = (x, y);
        if (IsVisible)
            ApplyPlace();
        else
            _placed = false;
    }

    private (int? X, int? Y) _place;
    private bool _placed;

    private void ApplyPlace()
    {
        _placed = true;
        var (x, y) = _place;
        GetWindowRect(_hwnd, out var current);
        int width = Math.Max(1, current.Width), height = Math.Max(1, current.Height);
        if (x is { } left && y is { } top)
        {
            // At least its middle on a screen.
            var probe = new RECT { Left = left + width / 2 - 1, Top = top + height / 2 - 1, Right = left + width / 2 + 1, Bottom = top + height / 2 + 1 };
            if (MonitorFromRect(ref probe, MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, Layer, left, top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }
        }
        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(FindWindow("Shell_TrayWnd", null), MONITOR_DEFAULTTOPRIMARY), ref monitor);
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int margin = (int)Math.Round(24 * scale);
        SetWindowPos(_hwnd, Layer, monitor.rcWork.Right - width - margin, monitor.rcWork.Bottom - height - margin, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void UpdateVisibility()
    {
        bool show = _wanted && !_fullScreen;
        if (show && !IsVisible)
        {
            if (!_placed)
            {
                // Shown first off the screen, the time to learn its size, then put in its place.
                Left = Top = -32000;
                Show();
                UpdateLayout();
                ApplyPlace();
            }
            else
                Show();
            SetWindowPos(_hwnd, Layer, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else if (!show && IsVisible)
            Hide();
    }

    // ---------------------------------------------------------------- Drag

    private void Pressed(object sender, MouseButtonEventArgs e)
    {
        // The progress and volume bars act on the press itself: no drag from them.
        if (Locked || View.IsOnTrack(e.OriginalSource as DependencyObject))
            return;
        GetCursorPos(out var pointer);
        GetWindowRect(_hwnd, out _windowAt);
        _pressedAt = pointer;
    }

    private void Dragged(object sender, MouseEventArgs e)
    {
        if (_pressedAt is not { } start)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }
        GetCursorPos(out var pointer);
        int dx = pointer.X - start.X, dy = pointer.Y - start.Y;
        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
                return;
            // From now on a drag: the window takes the mouse, a button under the press is not clicked.
            _dragging = true;
            CaptureMouse();
        }
        SetWindowPos(_hwnd, IntPtr.Zero, _windowAt.Left + dx, _windowAt.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void Released(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true;
            GetWindowRect(_hwnd, out var r);
            EndDrag();
            _place = (r.Left, r.Top);
            PlaceChanged?.Invoke(this, (r.Left, r.Top));
        }
        else
            _pressedAt = null;
    }

    private void EndDrag()
    {
        _pressedAt = null;
        if (!_dragging)
            return;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    // ---------------------------------------------------------------- Windows messages

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            // Moved (dragged, placed) or put back on top: its glass and its shadow follow, just under it.
            case WM_WINDOWPOSCHANGED:
                FollowUnderlays();
                break;

            case AppBarMessage when wParam.ToInt32() == ABN_FULLSCREENAPP:
                _fullScreen = lParam != IntPtr.Zero;
                UpdateVisibility();
                break;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _shadow?.Close();
        _glass?.Dispose();
        if (_appBar)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            SHAppBarMessage(ABM_REMOVE, ref data);
        }
        base.OnClosed(e);
    }
}
