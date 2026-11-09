using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NowPlaying.Core;
using NowPlaying.Services;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying;

/// <summary>
/// The widget in the taskbar. Windows 11 lets no app add anything to its taskbar: this is a window of its own, laid
/// over the taskbar of the main screen, or of another screen (<see cref="Tray"/>; "every screen", asked by the author on
/// October 24, 2026), left or near the clock, hidden over full-screen apps and while an auto-hidden taskbar is away. Its owner is the taskbar: an owned window always stays above its owner, even when
/// Windows lifts the taskbar over every window while Start or search is open (a topmost window then went under it).
/// That tie lets the two threads share their input: the window runs in the widget process only (see
/// <see cref="Services.TaskbarHost"/>), which the app ends if it ever stops answering.
/// </summary>
public partial class TaskbarWidget : Window
{
    private const int AppBarMessage = WM_APP + 2;

    private IntPtr _hwnd;
    private IntPtr _foregroundHook;
    private WinEventProc? _foregroundProc; // kept alive while hooked
    private bool _appBar;
    private bool _fullScreen;
    private bool _wanted;
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private readonly IntPtr _secondaryTray;

    /// <param name="secondaryTray">The taskbar of another screen (Shell_SecondaryTrayWnd); none: the main one.</param>
    public TaskbarWidget(IntPtr secondaryTray = default)
    {
        _secondaryTray = secondaryTray;
        InitializeComponent();
        _autoHideTimer.Tick += (_, _) => Place();
        // Beside a vertical taskbar the widget unfolds over the screen on hover: its window grows or shrinks.
        View.UnfoldedChanged += (_, _) => Place();
    }

    /// <summary>The taskbar it sits on: the main one (found again after Explorer restarts), or that of another screen.</summary>
    public IntPtr Tray => _secondaryTray != IntPtr.Zero ? _secondaryTray : FindWindow("Shell_TrayWnd", null);

    public bool IsSecondary => _secondaryTray != IntPtr.Zero;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Not in Alt+Tab, never activated by a click (the app in front keeps the keyboard).
        long style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        // Windows keeps a window at least 39 px high, more than the widget in a small taskbar (issue #1), and WPF keeps
        // that minimum from the first WM_GETMINMAXINFO: a size change now asks again, answered by WndProc.
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 1, 1, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        KeepTransparent();
        SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, Tray);

        // An app bar that takes no room: Windows tells it when an app goes full screen on its screen.
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = AppBarMessage };
        _appBar = SHAppBarMessage(ABM_NEW, ref data) != IntPtr.Zero;

        // The taskbar comes over the widget when it is clicked (or an app is chosen in it): back on top then.
        _foregroundProc = (_, _, _, _, _, _, _) => KeepOnTop();
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        FindTaskbarButtons();

        // The Widgets switch and the alignment of the taskbar, changed in Settings, reach no app: their key is watched.
        // Explorer lays its taskbar out again a moment after: look twice.
        _taskbarSettings = new RegistryWatcher(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            () => Dispatcher.BeginInvoke(() =>
            {
                _relookTimer.Stop();
                _relooks = 0;
                _relookTimer.Start();
            }));
        _relookTimer.Tick += (_, _) =>
        {
            FindTaskbarButtons();
            if (++_relooks >= 2)
                _relookTimer.Stop();
        };
    }

    private RegistryWatcher? _taskbarSettings;
    private readonly DispatcherTimer _relookTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private int _relooks;

    /// <summary>See-through where nothing is drawn (see <see cref="SeeThrough"/>).</summary>
    public void KeepTransparent() => SeeThrough.Apply(this, _hwnd);

    /// <summary>
    /// Whether what is behind the widget is dark, read on the screen (the line of taskbar just above it): a dark or
    /// light taskbar, one in the accent color, or one made see-through by another app over a wallpaper. Null until read.
    /// </summary>
    public bool? BackdropIsDark { get; private set; }

    /// <summary>Its place and width.</summary>
    public WidgetStyle WidgetStyle { get; set; } = new();

    /// <summary>Resolution, scale, light / dark or taskbar settings changed: the colors may have to follow.</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Whether the widget should be on screen (a track to show, the widget on): it still hides over full screen.</summary>
    public void SetWanted(bool wanted)
    {
        _wanted = wanted;
        Place();
    }

    /// <summary>Puts the widget at its place on the taskbar, or hides it when it has none now.</summary>
    public void Place()
    {
        bool placed = TryGetPlace(out int x, out int y, out bool autoHide);
        if (!_wanted || _fullScreen || !placed)
        {
            _autoHideTimer.Stop();
            if (IsVisible)
                Hide();
            // An auto-hidden taskbar away: look again in a moment.
            if (_wanted && !_fullScreen && autoHide)
                _autoHideTimer.Start();
            return;
        }
        View.FitHeight(_fitHeight);
        View.SetVertical(_onTheRight);
        if (!IsVisible)
            Show();
        // Its size too: unfolding beside a taskbar on the right moves its left edge and widens it at once.
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _size.Width, _size.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        if (autoHide)
            _autoHideTimer.Start();
        else
            _autoHideTimer.Stop();
        // Unfolded, it is over the screen: what is behind it is not the taskbar.
        if (!View.IsUnfolded)
            ReadBackdrop(x, y);
    }

    /// <summary>Raised when what is behind the widget turned from light to dark or back: its text color must follow.</summary>
    public event EventHandler? BackdropChanged;

    private (int X, int Y, int Width) _sampledAt;
    private bool _resample = true;

    // The line of taskbar just above the widget (40 px high in a 48 px taskbar, 2 px from the edges of a small one),
    // read once per place.
    private void ReadBackdrop(int x, int y)
    {
        if (!_resample && _sampledAt == (x, y, _placeWidth))
            return;
        _resample = false;
        _sampledAt = (x, y, _placeWidth);
        int top = _barTop + 1, height = Math.Min(3, y - top);
        if (height <= 0 || ScreenSampler.Mean(x, top, _placeWidth, height) is not { } color)
            return;
        bool dark = color.Luminance < 0.36;
        if (dark == BackdropIsDark)
            return;
        BackdropIsDark = dark;
        BackdropChanged?.Invoke(this, EventArgs.Empty);
    }

    private int _barTop;
    private int _placeWidth;
    private double _fitHeight = WidgetStyle.Height;
    private (int Width, int Height) _size;
    private bool? _onTheRight;

    private bool TryGetPlace(out int x, out int y, out bool autoHide)
    {
        x = y = 0;
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        autoHide = (SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt64() & ABS_AUTOHIDE) != 0;

        IntPtr tray = Tray;
        if (tray == IntPtr.Zero || !IsWindow(tray) || !GetWindowRect(tray, out var rect))
            return false;

        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(tray, MONITOR_DEFAULTTOPRIMARY), ref monitor);
        var screen = monitor.rcMonitor;
        double scale = GetDpiForWindow(tray) / 96.0;
        int Px(double value) => (int)Math.Round(value * scale);
        var bar = new TaskbarRect(rect.Left, rect.Top, rect.Width, rect.Height);
        bool faked = false;
        // Test hooks (pictures, tests: the taskbar's size and side are settings of the user): NOWPLAYING_TASKBAR_HEIGHT=31
        // lays the widget out as in a taskbar that high, NOWPLAYING_TASKBAR_SIDE=left|right as on a 48 px vertical
        // taskbar on that side of the screen, above the real one.
        if (int.TryParse(Environment.GetEnvironmentVariable("NOWPLAYING_TASKBAR_HEIGHT"), out int forced) && forced > 0)
            bar = bar with { Height = Px(forced) };
        if (Environment.GetEnvironmentVariable("NOWPLAYING_TASKBAR_SIDE") is "left" or "right" && !bar.Vertical)
        {
            bool right = Environment.GetEnvironmentVariable("NOWPLAYING_TASKBAR_SIDE") == "right";
            bar = new TaskbarRect(right ? screen.Right - Px(48) : screen.Left, screen.Top, Px(48), rect.Top - screen.Top);
            faked = true;
        }

        // An auto-hidden taskbar keeps a line or two of itself on screen.
        bool vertical = bar.Vertical;
        int visible = vertical
            ? Math.Min(bar.Right, screen.Right) - Math.Max(bar.Left, screen.Left)
            : Math.Min(bar.Bottom, screen.Bottom) - Math.Max(bar.Top, screen.Top);
        if (!faked && visible < (vertical ? bar.Width : bar.Height) - 4)
            return false;

        // What the taskbar holds, along it (x, or y on a vertical taskbar). The notification area: Windows 11 still
        // gives its rectangle (the taskbar of another screen has none).
        int Along(Edges edges) => vertical ? edges.Top : edges.Left;
        IntPtr notify = faked ? IntPtr.Zero : FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        int? notifyAt = notify != IntPtr.Zero && GetWindowRect(notify, out var area) && area.Width > 0 && area.Height > 0
            ? (vertical ? area.Top : area.Left)
            : null;
        var buttons = faked
            ? new TaskbarButtons()
            : new TaskbarButtons(
                _weather is { } weather ? (vertical ? (weather.Top, weather.Bottom) : (weather.Left, weather.Right)) : null,
                _start is { } start ? Along(start) : null,
                notifyAt,
                _clock is { } clock ? Along(clock) : null,
                _alignedLeft);
        var place = TaskbarPlacement.Place(bar, scale, WidgetStyle, buttons);
        (x, y, _fitHeight) = place;
        int height = Px(place.Height);
        _onTheRight = vertical ? TaskbarPlacement.OnTheRight(bar, (screen.Left + screen.Right) / 2) : null;
        // Beside a vertical taskbar: the cover alone (a square), or unfolded over the screen.
        int width = !vertical || View.IsUnfolded ? Px(WidgetStyle.Width) : height;
        if (_onTheRight is { } onTheRight && View.IsUnfolded)
            x = TaskbarPlacement.UnfoldedLeft(onTheRight, x, height, width);
        _size = (width, height);
        _barTop = bar.Top;
        _placeWidth = vertical ? height : width;
        return true;
    }

    // The Widgets button (weather) and the Start button of the taskbar, in pixels: drawn by Explorer without windows of
    // their own, found through UI Automation, away from the UI thread (Explorer may be slow to answer).
    private Edges? _weather;
    private Edges? _start;
    private Edges? _clock;
    private bool _alignedLeft;
    private bool _lookingForButtons;
    private bool _buttonsKnown;

    /// <summary>
    /// Whether the weather of Windows (its Widgets button) is on the taskbar; null until looked for. Only the user can
    /// turn it off or on (Settings › Personalization › Taskbar): Windows protects that setting from the apps.
    /// </summary>
    public bool? WindowsWeatherShown => _buttonsKnown ? _weather is not null : null;

    /// <summary>The weather of Windows appeared or went away.</summary>
    public event EventHandler? TaskbarButtonsChanged;

    /// <summary>Looks again where the buttons of the taskbar are (taskbar settings, screen, Explorer restarted).</summary>
    public void FindTaskbarButtons()
    {
        if (_lookingForButtons)
            return;
        _lookingForButtons = true;
        Task.Run(() =>
        {
            Edges? weather = null, start = null, clock = null;
            bool alignedLeft = WindowsSettings.TaskbarIconsAlignedLeft;
            try
            {
                IntPtr tray = Tray;
                if (tray != IntPtr.Zero)
                {
                    var root = System.Windows.Automation.AutomationElement.FromHandle(tray);
                    weather = Bounds(root, "WidgetsButton");
                    start = Bounds(root, "StartButton");
                    if (IsSecondary)
                        clock = Bounds(root, "SystemTrayIcon");
                }
            }
            catch (Exception ex) when (ex is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"Taskbar buttons: {ex.Message}");
            }
            Dispatcher.BeginInvoke(() =>
            {
                bool changed = !_buttonsKnown || (_weather is null) != (weather is null);
                _weather = weather;
                _start = start;
                _clock = clock;
                _alignedLeft = alignedLeft;
                _buttonsKnown = true;
                _lookingForButtons = false;
                Place();
                if (changed)
                    TaskbarButtonsChanged?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    /// <summary>The edges of a button of the taskbar, in pixels of the screen.</summary>
    private readonly record struct Edges(int Left, int Top, int Right, int Bottom);

    private static Edges? Bounds(System.Windows.Automation.AutomationElement root, string automationId)
    {
        var element = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, automationId));
        if (element is null || element.Current.IsOffscreen)
            return null;
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width <= 0)
            return null;
        return new Edges((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
    }

    // Raised alone (SWP_NOOWNERZORDER): raised with it, the taskbar it belongs to came over the menus of the other apps,
    // a tray icon's right-click menu among them (November 9, 2026; LocalBell had the same, fixed the same way).
    private void KeepOnTop()
    {
        if (!IsVisible)
            return;
        const int Flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, Flags);
        // The taskbar may raise itself once more right after: again when the UI is idle.
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, Flags);
        }, DispatcherPriority.ApplicationIdle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            case WM_GETMINMAXINFO:
                // No minimum size (ptMinTrackSize of MINMAXINFO, after three POINTs); not handled: WPF reads it after.
                Marshal.WriteInt32(lParam, 24, 1);
                Marshal.WriteInt32(lParam, 28, 1);
                break;

            case AppBarMessage:
                if (wParam.ToInt32() == ABN_FULLSCREENAPP)
                {
                    _fullScreen = lParam != IntPtr.Zero;
                    Place();
                }
                else if (wParam.ToInt32() == ABN_POSCHANGED)
                    Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
                break;

            case WM_DISPLAYCHANGE:
            case WM_SETTINGCHANGE:
                // Resolution, scale, taskbar settings (alignment, auto-hide), light / dark.
                Dispatcher.BeginInvoke(() =>
                {
                    _resample = true;
                    DisplayChanged?.Invoke(this, EventArgs.Empty);
                    Place();
                    FindTaskbarButtons();
                }, DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(Place, DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoHideTimer.Stop();
        _relookTimer.Stop();
        _taskbarSettings?.Dispose();
        if (_foregroundHook != IntPtr.Zero)
            UnhookWinEvent(_foregroundHook);
        if (_appBar)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            SHAppBarMessage(ABM_REMOVE, ref data);
        }
        base.OnClosed(e);
    }
}
