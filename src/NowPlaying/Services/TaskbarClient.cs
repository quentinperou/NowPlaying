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
        Wire();
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

    private void Wire()
    {
        var view = _widget.View;
        view.PlayPauseRequested += (_, _) => _channel.Send("cmd", "playpause");
        view.NextRequested += (_, _) => _channel.Send("cmd", "next");
        view.PreviousRequested += (_, _) => _channel.Send("cmd", "previous");
        view.SeekRequested += (_, fraction) => _channel.Send("seek", fraction.ToString("0.####", CultureInfo.InvariantCulture));
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
            GetWindowRect(FindWindow("Shell_TrayWnd", null), out var bar);
            PopupMenu.ShowSources(state.Sources,
                id => _channel.Send("pin", id),
                () =>
                {
                    if (_appProcessId != 0)
                        AllowSetForegroundWindow(_appProcessId);
                    _channel.Send("open");
                },
                () => _channel.Send("quit"),
                aboveY: bar.Top);
        };

        _widget.BackdropChanged += (_, _) =>
        {
            Apply();
            _channel.Send("backdrop", _widget.BackdropIsDark switch { true => "dark", false => "light", null => "unknown" });
        };
        _widget.TaskbarButtonsChanged += (_, _) =>
            _channel.Send("weather", _widget.WindowsWeatherShown switch { true => "shown", false => "hidden", null => "unknown" });
        _widget.DisplayChanged += (_, _) => Apply();
        // Its taskbar went away with Explorer: this process ends, the app starts a new one for the new taskbar.
        _widget.Closed += (_, _) => Quit();
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
                        App.Settings.Theme = state.Theme;
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
        _widget.WidgetStyle = style;
        _widget.View.ApplyStyle(style, SurfaceColors.Resolve(style, media?.CoverColor, _widget.BackdropIsDark ?? WindowsSettings.TaskbarIsDark, overlay: false));
        _widget.View.ShowMedia(media, state.Idle);
        _widget.SetWanted(state.Wanted);
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
