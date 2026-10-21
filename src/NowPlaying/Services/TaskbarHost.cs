using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;
using NowPlaying.Core;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// The widget in the taskbar runs in a process of its own (NowPlaying.exe --taskbar, see <see cref="TaskbarClient"/>):
/// its window is owned by the taskbar, which keeps it over the taskbar even while Start or search is open, but which
/// ties the two together, so that a widget that stops answering could hold the taskbar. This side, in the app, sends it
/// what to show, gets its buttons back, and watches it: a widget silent for 2 seconds is ended (the taskbar is free
/// again at once) and started again. Whatever happens to it, the app goes on.
/// </summary>
/// <remarks>Tested by suspending the widget process: ended about 3 seconds later, a new one shown a second after.</remarks>
internal sealed class TaskbarHost : IDisposable
{
    public const string Argument = "--taskbar";

    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(20);

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _restart = new();
    private Process? _process;
    private LineChannel? _channel;
    private DateTime _startedAt;
    private DateTime? _pingSentAt; // a ping not answered yet
    private bool _ready;
    private bool _enabled;
    private bool _disposed;
    private int _failures;

    // The last of each, sent again to a widget started anew.
    private TaskbarState? _state;
    private MediaSnapshot? _media;
    private string? _sentState;
    private string? _sentCover;

    public TaskbarHost(Dispatcher ui)
    {
        _ui = ui;
        _watchdog.Tick += (_, _) => Watch();
        _restart.Tick += (_, _) =>
        {
            _restart.Stop();
            if (_enabled && _process is null)
                Start();
        };
    }

    /// <summary>Whether the taskbar behind the widget is dark, as the widget reads it on the screen; null until known.</summary>
    public bool? BackdropIsDark { get; private set; }

    /// <summary>Whether the weather of Windows is on the taskbar; null until the widget has looked.</summary>
    public bool? WindowsWeatherShown { get; private set; }

    public event EventHandler? BackdropChanged;
    public event EventHandler? WeatherChanged;

    /// <summary>A button of the widget: playpause, next or previous.</summary>
    public event Action<string>? CommandReceived;
    public event Action<double>? SeekRequested;

    /// <summary>A player chosen in the menu of the widget (its id).</summary>
    public event Action<string>? PinRequested;

    /// <summary>"Customize" in the menu of the widget (the widget process allowed the app to come to the front).</summary>
    public event EventHandler? OpenRequested;
    public event EventHandler? QuitRequested;

    /// <summary>What the widget shows. <paramref name="enabled"/>: the widget process runs (the widget is on).</summary>
    public void Update(WidgetStyle style, MediaSnapshot? media, bool idle, bool wanted, bool enabled,
        IReadOnlyList<MediaSource> sources, string language)
    {
        _state = new TaskbarState(style, wanted, idle, media is null ? null : TaskbarMedia.From(media), sources, language);
        _media = media;
        _enabled = enabled;
        if (!enabled)
        {
            Stop();
            return;
        }
        if (_process is null && !_restart.IsEnabled)
            Start();
        Flush();
    }

    // ---------------------------------------------------------------- The process

    private void Start()
    {
        if (_disposed || Environment.ProcessPath is not { } exe)
            return;
        string name = $"NowPlaying.Taskbar.{Environment.ProcessId}.{Guid.NewGuid():N}";
        NamedPipeServerStream pipe;
        Process? process;
        try
        {
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            process = Process.Start(new ProcessStartInfo(exe, $"{Argument} {name}") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Taskbar widget: {ex.Message}");
            Lost(null);
            return;
        }
        if (process is null)
        {
            pipe.Dispose();
            Lost(null);
            return;
        }
        _process = process;
        _startedAt = DateTime.UtcNow;
        _ready = false;
        _pingSentAt = null;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ui.BeginInvoke(() => Lost(process));
        _watchdog.Start();
        _ = ConnectAsync(pipe, process);
    }

    private async Task ConnectAsync(NamedPipeServerStream pipe, Process process)
    {
        try
        {
            using var timeout = new CancellationTokenSource(StartLimit);
            await pipe.WaitForConnectionAsync(timeout.Token);
            // Only the process started here may talk to the app.
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != process.Id)
                throw new IOException("Another process connected.");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            pipe.Dispose();
            _ = _ui.BeginInvoke(() => End(process));
            return;
        }
        _ = _ui.BeginInvoke(() =>
        {
            if (process != _process)
            {
                pipe.Dispose();
                return;
            }
            _channel = new LineChannel(pipe,
                line => _ui.BeginInvoke(() => Received(process, line)),
                () => _ui.BeginInvoke(() => Lost(process)));
            _sentState = _sentCover = null;
            Flush();
        });
    }

    /// <summary>Sends what changed since the last line: the cover first (only when it changed), then the state.</summary>
    private void Flush()
    {
        if (_channel is null || _state is null)
            return;
        if (_state.Media is { } media && media.CoverKey != _sentCover)
        {
            _sentCover = media.CoverKey;
            _channel.Send("cover", JsonSerializer.Serialize(new TaskbarCover(media.CoverKey, _media?.CoverBytes)));
        }
        string state = JsonSerializer.Serialize(_state);
        if (state == _sentState)
            return;
        _sentState = state;
        _channel.Send("state", state);
    }

    private void Received(Process process, string line)
    {
        if (process != _process)
            return;
        var (verb, payload) = LineChannel.Parse(line);
        switch (verb)
        {
            case "ready":
                _ready = true;
                break;
            case "pong":
                _pingSentAt = null;
                break;
            case "cmd" when payload is "playpause" or "next" or "previous":
                CommandReceived?.Invoke(payload);
                break;
            case "seek" when double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out double fraction):
                SeekRequested?.Invoke(Math.Clamp(fraction, 0, 1));
                break;
            case "pin" when payload.Length > 0:
                PinRequested?.Invoke(payload);
                break;
            case "open":
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "quit":
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "backdrop":
                bool? dark = payload switch { "dark" => true, "light" => false, _ => null };
                if (dark != BackdropIsDark)
                {
                    BackdropIsDark = dark;
                    BackdropChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            case "weather":
                bool? shown = payload switch { "shown" => true, "hidden" => false, _ => null };
                if (shown != WindowsWeatherShown)
                {
                    WindowsWeatherShown = shown;
                    WeatherChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    // Once a second: a ping, which the widget answers from its window's thread. Measured from the ping itself, so that
    // a busy app never takes itself for a hung widget.
    private void Watch()
    {
        if (_process is not { } process)
        {
            _watchdog.Stop();
            return;
        }
        var now = DateTime.UtcNow;
        if (_channel is null || !_ready)
        {
            if (now - _startedAt > StartLimit)
            {
                Debug.WriteLine("Taskbar widget: did not start");
                End(process);
            }
            return;
        }
        if (_pingSentAt is { } sent)
        {
            if (now - sent > HangLimit)
            {
                Debug.WriteLine("Taskbar widget: not answering, ended");
                End(process);
            }
            return;
        }
        _pingSentAt = now;
        _channel.Send("ping");
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    // Ended on purpose (hung, or never connected): what follows is the same as a crash.
    private void End(Process process)
    {
        Kill(process);
        Lost(process);
    }

    /// <summary>The widget process went away (ended, crashed, the taskbar it belonged to restarted): started again.</summary>
    private void Lost(Process? process)
    {
        if (process is not null && process != _process)
            return;
        Disconnect();
        if (!_enabled || _disposed)
            return;
        // Again at once after a long life (Explorer restarted), slower and slower when it keeps failing.
        _failures = DateTime.UtcNow - _startedAt > TimeSpan.FromMinutes(1) ? 0 : _failures + 1;
        _restart.Interval = TimeSpan.FromSeconds(_failures == 0 ? 1 : Math.Min(60, Math.Pow(2, _failures)));
        _restart.Start();
    }

    private void Disconnect()
    {
        var process = _process;
        _process = null;
        _channel?.Dispose();
        _channel = null;
        _ready = false;
        _pingSentAt = null;
        if (process is not null && !process.HasExited)
            Kill(process);
        process?.Dispose();
    }

    /// <summary>The widget turned off: its process ends (on its own, when its pipe closes).</summary>
    private void Stop()
    {
        _restart.Stop();
        _watchdog.Stop();
        var process = _process;
        _process = null;
        _channel?.Dispose();
        _channel = null;
        _ready = false;
        process?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        // A widget that does not answer any more is ended; otherwise it closes itself when the pipe does.
        if (_pingSentAt is { } sent && DateTime.UtcNow - sent > TimeSpan.FromSeconds(1) && _process is { } hung)
            Kill(hung);
        Stop();
    }
}
