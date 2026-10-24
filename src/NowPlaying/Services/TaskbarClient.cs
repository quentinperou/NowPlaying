using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NowPlaying.Core;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// The widget process (NowPlaying.exe --taskbar, started by <see cref="TaskbarHost"/>): only the widget of the taskbar
/// and the sound bars, nothing else of the app. It shows what the app sends, sends it its buttons back, answers its
/// pings from the widget's thread, and quits when the app goes away or when its taskbar does (Explorer restarted).
/// </summary>
internal sealed class TaskbarClient
{
    private readonly Application _app;
    private readonly LineChannel _channel;
    private readonly TaskbarWidget _widget;
    // "Every screen": the widgets of the taskbars of the other screens, by taskbar.
    private readonly Dictionary<IntPtr, TaskbarWidget> _others = [];
    private readonly DispatcherTimer _screensAgain = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly int _appProcessId;
    private TaskbarState? _state;
    private string? _language;
    private (string Key, BitmapSource? Cover) _cover = ("", null);

    private TaskbarClient(Application app, NamedPipeClientStream pipe, int appProcessId)
    {
        _app = app;
        _appProcessId = appProcessId;
        _widget = new TaskbarWidget();
        _widget.View.Spectrum = new AudioSpectrum(app.Dispatcher);
        Wire(_widget);
        WireMain();
        // The window exists (handle, owner, hooks) before it is shown for the first time.
        new System.Windows.Interop.WindowInteropHelper(_widget).EnsureHandle();
        _channel = new LineChannel(pipe,
            line => app.Dispatcher.BeginInvoke(() => Received(line)),
            () => app.Dispatcher.BeginInvoke(Quit));
        _channel.Send("ready");
        MemoryTrim.Soon();
    }

    /// <summary>Runs the widget process: connects to the app, waits for a taskbar to belong to, shows the widget.</summary>
    public static void Run(Application app, string pipeName)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // No message box from this process: it ends, and the app starts it again.
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            app.Shutdown(1);
        };

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(5000);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            app.Shutdown(1);
            return;
        }
        int appId = GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) ? (int)server : 0;

        // Explorer restarting: its taskbar comes back after a moment.
        var waitForTaskbar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        int tries = 0;
        void TryStart()
        {
            if (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
            {
                waitForTaskbar.Stop();
                _ = new TaskbarClient(app, pipe, appId);
            }
            else if (++tries > 120)
            {
                waitForTaskbar.Stop();
                pipe.Dispose();
                app.Shutdown(1);
            }
        }
        waitForTaskbar.Tick += (_, _) => TryStart();
        waitForTaskbar.Start();
        TryStart();
    }

    private IEnumerable<TaskbarWidget> Widgets => _others.Values.Prepend(_widget);

    // A widget of any screen: its buttons, its menu (above its taskbar), its colors after what is behind it.
    private void Wire(TaskbarWidget widget)
    {
        var view = widget.View;
        view.PlayPauseRequested += (_, _) => _channel.Send("cmd", "playpause");
        view.NextRequested += (_, _) => _channel.Send("cmd", "next");
        view.PreviousRequested += (_, _) => _channel.Send("cmd", "previous");
        view.SeekRequested += (_, fraction) => _channel.Send("seek", fraction.ToString("0.####", CultureInfo.InvariantCulture));
        // This process got the click: it may bring the player to the front itself.
        view.CoverClicked += (_, _) =>
        {
            if (_state?.Media is { } media)
                PlayerWindow.BringToFront(media.AppId);
        };
        // The volume of the app that plays (Core Audio) is set from here: no round trip.
        view.VolumeWheel += (_, notches) =>
        {
            if (_state?.Media is not { } media)
                return;
            // 2 % a notch, as the volume flyout of Windows.
            float? volume = AppVolume.Get(media.AppId);
            if (volume is { } v && notches != 0)
                volume = AppVolume.Set(media.AppId, (float)Math.Round(v + notches * 0.02, 2));
            view.ShowVolume(volume);
        };
        view.VolumeRequested += (_, fraction) =>
        {
            if (_state?.Media is { } media)
                view.ShowVolume(AppVolume.Set(media.AppId, (float)fraction));
        };
        // The menu is shown here, where the click was (see PopupMenu.ShowSources), above the taskbar; the choices go
        // to the app, allowed to come to the front for its window.
        view.MenuRequested += (_, _) =>
        {
            if (_state is not { } state)
                return;
            GetWindowRect(widget.Tray, out var bar);
            void Open(string page)
            {
                if (_appProcessId != 0)
                    AllowSetForegroundWindow(_appProcessId);
                _channel.Send("open", page);
            }
            PopupMenu.ShowSources(state.Sources,
                id => _channel.Send("pin", id),
                () => Open(""),
                () => _channel.Send("quit"),
                aboveY: bar.Top,
                copy: TrackText.Format(state.Media?.Title, state.Media?.Artist),
                history: () => Open("history"));
        };

        widget.BackdropChanged += (_, _) => Apply();
        widget.DisplayChanged += (_, _) => Apply();
    }

    // The widget of the main taskbar: what the app is told, and the end of this process with Explorer.
    private void WireMain()
    {
        _widget.BackdropChanged += (_, _) =>
            _channel.Send("backdrop", _widget.BackdropIsDark switch { true => "dark", false => "light", null => "unknown" });
        _widget.TaskbarButtonsChanged += (_, _) =>
            _channel.Send("weather", _widget.WindowsWeatherShown switch { true => "shown", false => "hidden", null => "unknown" });
        // A screen plugged in: Explorer gives it its taskbar a moment later.
        _widget.DisplayChanged += (_, _) =>
        {
            _screensAgain.Stop();
            _screensAgain.Start();
        };
        _screensAgain.Tick += (_, _) =>
        {
            _screensAgain.Stop();
            Apply();
        };
        // Its taskbar went away with Explorer: this process ends, the app starts a new one for the new taskbar.
        _widget.Closed += (_, _) => Quit();
    }

    // "Every screen": a widget on the taskbar of each other screen, made or closed as the screens come and go.
    private void SyncScreens(bool every)
    {
        var trays = new HashSet<IntPtr>();
        if (every)
            for (IntPtr tray = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); tray != IntPtr.Zero;
                 tray = FindWindowEx(IntPtr.Zero, tray, "Shell_SecondaryTrayWnd", null))
                trays.Add(tray);
        foreach (var (tray, widget) in _others.ToList())
            if (!trays.Contains(tray))
            {
                _others.Remove(tray);
                widget.Close();
            }
        foreach (var tray in trays)
        {
            if (_others.ContainsKey(tray))
                continue;
            var widget = new TaskbarWidget(tray);
            widget.View.Spectrum = _widget.View.Spectrum; // one capture of the sound for all of them
            Wire(widget);
            // Its screen unplugged: its taskbar, its owner, takes it away.
            widget.Closed += (_, _) => _others.Remove(tray);
            new System.Windows.Interop.WindowInteropHelper(widget).EnsureHandle();
            _others[tray] = widget;
        }
    }

    private void Received(string line)
    {
        var (verb, payload) = LineChannel.Parse(line);
        try
        {
            switch (verb)
            {
                case "ping":
                    _channel.Send("pong");
                    break;
                case "cover":
                    if (JsonSerializer.Deserialize<TaskbarCover>(payload) is { } cover)
                    {
                        _cover = (cover.Key, MediaSessions.Decode(cover.Data).Item1);
                        Apply();
                    }
                    break;
                case "state":
                    _state = JsonSerializer.Deserialize<TaskbarState>(payload);
                    if (_state is { } state)
                    {
                        // For the menu, as in the app.
                        if (state.Language != _language)
                            App.ApplyLanguage(_language = state.Language);
                    }
                    Apply();
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private void Apply()
    {
        if (_state is not { } state)
            return;
        var style = state.Style.Normalized();
        var media = state.Media?.ToSnapshot(state.Media.CoverKey == _cover.Key ? _cover.Cover : null);
        SyncScreens(style.AllScreens);
        // The bars listen to the player shown (one capture for every screen).
        _widget.View.Spectrum?.SetPlayer(state.Media?.AppId);
        foreach (var widget in Widgets.ToList())
        {
            widget.WidgetStyle = style;
            // Each one dark or light after its own taskbar.
            widget.View.ApplyStyle(style, SurfaceColors.Resolve(style, media?.CoverColor, widget.BackdropIsDark ?? WindowsSettings.TaskbarIsDark, overlay: false));
            widget.View.ShowMedia(media, state.Idle);
            widget.SetWanted(state.Wanted);
        }
    }

    private void Quit()
    {
        if (_quitting)
            return;
        _quitting = true;
        _channel.Dispose();
        _app.Shutdown();
    }

    private bool _quitting;
}
