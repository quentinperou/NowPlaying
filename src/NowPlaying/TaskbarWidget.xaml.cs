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
/// over the taskbar of the main screen (left, or near the clock), kept above it when the taskbar comes to the front,
/// hidden over full-screen apps and while an auto-hidden taskbar is away. It is never made a child of the taskbar:
/// a problem in NowPlaying can never freeze Explorer.
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

    public TaskbarWidget()
    {
        InitializeComponent();
        _autoHideTimer.Tick += (_, _) => Place();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Not in Alt+Tab, never activated by a click (the app in front keeps the keyboard).
        long style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        KeepTransparent();

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

    /// <summary>
    /// See-through where nothing is drawn, composed by DWM (a layered window, AllowsTransparency, would be drawn by the
    /// processor at each frame of the bars): no backdrop, no rounded corners nor border from Windows 11, the widget
    /// draws its own. Applied again when the theme of the app changes, which may touch every window.
    /// </summary>
    public void KeepTransparent()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        Background = System.Windows.Media.Brushes.Transparent;
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        int backdrop = DWMSBT_NONE, corners = DWMWCP_DONOTROUND, border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>
    /// Whether what is behind the widget is dark, read on the screen (the line of taskbar just above it): a dark or
    /// light taskbar, one in the accent color, or one made see-through by another app over a wallpaper. Null until read.
    /// </summary>
    public bool? BackdropIsDark { get; private set; }

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
        if (!IsVisible)
            Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        if (autoHide)
            _autoHideTimer.Start();
        else
            _autoHideTimer.Stop();
        ReadBackdrop(x, y);
    }

    /// <summary>Raised when what is behind the widget turned from light to dark or back: its text color must follow.</summary>
    public event EventHandler? BackdropChanged;

    private (int X, int Y, int Width) _sampledAt;
    private bool _resample = true;

    // The line of taskbar just above the widget (the widget is 40 px high in a 48 px taskbar), read once per place.
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

    private bool TryGetPlace(out int x, out int y, out bool autoHide)
    {
        x = y = 0;
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        autoHide = (SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt64() & ABS_AUTOHIDE) != 0;

        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var bar) || bar.Width <= bar.Height)
            return false; // no taskbar, or a vertical one (not on Windows 11)

        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(tray, MONITOR_DEFAULTTOPRIMARY), ref monitor);
        // An auto-hidden taskbar keeps a line or two of itself on screen.
        int visible = Math.Min(bar.Bottom, monitor.rcMonitor.Bottom) - Math.Max(bar.Top, monitor.rcMonitor.Top);
        if (visible < bar.Height - 4)
            return false;

        double scale = GetDpiForWindow(tray) / 96.0;
        var style = App.Settings.Widget;
        int width = (int)Math.Round(style.Width * scale), height = (int)Math.Round(WidgetStyle.Height * scale);
        int margin = (int)Math.Round((8 + style.Offset) * scale);
        y = bar.Top + (bar.Height - height) / 2;
        _barTop = bar.Top;
        _placeWidth = width;

        bool nearClock = style.Position == WidgetPosition.NearClock;
        if (!nearClock)
        {
            // After the weather of Windows (its Widgets button) when it is there, on by default on Windows 11.
            int left = _widgetsRight is int widgets && widgets > bar.Left && widgets < bar.Left + bar.Width / 2 ? widgets : bar.Left;
            x = left + margin;
            // No room before the icons (or they are aligned left, Start first): next to the clock rather than over them.
            if (_startLeft is int start && start > bar.Left && x + width > start - (int)Math.Round(4 * scale))
                nearClock = true;
        }
        if (nearClock)
        {
            // Left of the notification area; Windows 11 still gives its rectangle. Else about where it is.
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            int right = notify != IntPtr.Zero && GetWindowRect(notify, out var area) && area.Width > 0 && area.Left > bar.Left
                ? area.Left
                : bar.Right - (int)Math.Round(220 * scale);
            x = right - width - (int)Math.Round((8 + (style.Position == WidgetPosition.NearClock ? style.Offset : 0)) * scale);
        }
        x = Math.Max(bar.Left, x);
        return true;
    }

    // The Widgets button (weather) and the Start button of the taskbar, in pixels: drawn by Explorer without windows of
    // their own, found through UI Automation, away from the UI thread (Explorer may be slow to answer).
    private int? _widgetsRight;
    private int? _startLeft;
    private bool _lookingForButtons;
    private bool _buttonsKnown;

    /// <summary>
    /// Whether the weather of Windows (its Widgets button) is on the taskbar; null until looked for. Only the user can
    /// turn it off or on (Settings › Personalization › Taskbar): Windows protects that setting from the apps.
    /// </summary>
    public bool? WindowsWeatherShown => _buttonsKnown ? _widgetsRight is not null : null;

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
            int? widgets = null, start = null;
            try
            {
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero)
                {
                    var root = System.Windows.Automation.AutomationElement.FromHandle(tray);
                    widgets = Edge(root, "WidgetsButton", right: true);
                    start = Edge(root, "StartButton", right: false);
                }
            }
            catch (Exception ex) when (ex is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"Taskbar buttons: {ex.Message}");
            }
            Dispatcher.BeginInvoke(() =>
            {
                bool changed = !_buttonsKnown || (_widgetsRight is null) != (widgets is null);
                _widgetsRight = widgets;
                _startLeft = start;
                _buttonsKnown = true;
                _lookingForButtons = false;
                Place();
                if (changed)
                    TaskbarButtonsChanged?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private static int? Edge(System.Windows.Automation.AutomationElement root, string automationId, bool right)
    {
        var element = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, automationId));
        if (element is null || element.Current.IsOffscreen)
            return null;
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width <= 0)
            return null;
        return (int)Math.Round(right ? bounds.Right : bounds.Left);
    }

    private void KeepOnTop()
    {
        if (!IsVisible)
            return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        // The taskbar may raise itself once more right after: again when the UI is idle.
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }, DispatcherPriority.ApplicationIdle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

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
                    App.RefreshWidget();
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
