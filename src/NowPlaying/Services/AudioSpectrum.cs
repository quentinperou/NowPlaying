using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// The sound for the "real spectrum" visualizer, read by <see cref="SpectrumAnalyzer"/>: the sound of the player shown
/// only (<see cref="SetPlayer"/>, <see cref="ProcessLoopback"/>; asked by the author on October 24, 2026: not Discord, a
/// game or a notification), else the whole default output (WASAPI loopback: the player has made no sound yet, or a
/// Windows too old). It runs only while someone looks (the widget at rest with its bars, an overlay page with a
/// visualizer) and something plays: <see cref="Acquire"/> / <see cref="Release"/> count them.
/// </summary>
internal sealed class AudioSpectrum : IDisposable
{
    public const int Bars = 24;

    private readonly SpectrumAnalyzer _analyzer = new(Bars);
    private readonly float[] _levels = new float[Bars];
    private readonly Dispatcher _ui;
    private WasapiLoopbackCapture? _capture;
    private ProcessLoopback? _process;
    private string? _player;
    // This Windows has no process loopback (before Windows 10 2004, build 20348 for the include-tree mode): the whole
    // output only.
    private bool _processUnavailable = Environment.OSVersion.Version.Build < 20348;
    private long _lastLookForPlayer;
    private MMDeviceEnumerator? _devices;
    private DeviceWatcher? _watcher;
    private int _users;
    private long _lastData;

    public AudioSpectrum(Dispatcher ui) => _ui = ui;

    public void Acquire()
    {
        if (_users++ == 0)
            Start();
    }

    public void Release()
    {
        if (_users > 0 && --_users == 0)
            Stop();
    }

    /// <summary>
    /// The player shown (its media app id): its sound only from now on. Called at each change of what plays (cheap
    /// when nothing changes); while the whole output is heard for want of the player's sound, its sound is looked for
    /// again every few seconds.
    /// </summary>
    public void SetPlayer(string? appId)
    {
        if (string.IsNullOrEmpty(appId))
            return;
        if (appId != _player)
        {
            _player = appId;
            Restart();
        }
        else if (_users > 0 && _process is null && !_processUnavailable
            && Stopwatch.GetElapsedTime(_lastLookForPlayer) > TimeSpan.FromSeconds(3))
            Restart();
    }

    /// <summary>The bars now (0 to 1), smoothed with the previous call: call it at the pace of the display.</summary>
    public ReadOnlySpan<float> Levels()
    {
        // Several readers in the same frame (the widget, its preview, the overlay) share one computation.
        if (Stopwatch.GetElapsedTime(_lastCompute) < TimeSpan.FromMilliseconds(15))
            return _levels;
        _lastCompute = Stopwatch.GetTimestamp();
        // Windows sends nothing while the PC is silent: let the bars fall instead of freezing them.
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastData)) > TimeSpan.FromMilliseconds(120))
            _analyzer.AddSamples(new float[1024]);
        _analyzer.Compute(_levels);
        return _levels;
    }

    private long _lastCompute;

    private void Start()
    {
        // The player's sound when its process is known.
        _lastLookForPlayer = Stopwatch.GetTimestamp();
        if (!_processUnavailable && _player is not null && AppVolume.SoundProcess(_player) is int pid)
        {
            _analyzer.SampleRate = ProcessLoopback.SampleRate;
            _analyzer.Clear();
            _process = ProcessLoopback.Start(pid);
            _process.DataAvailable += (buffer, count) =>
                Feed(buffer.AsSpan(0, count), ProcessLoopback.Channels, ProcessLoopback.BitsPerSample / 8, isFloat: false);
            var started = _process;
            _process.Stopped += error => _ui.BeginInvoke(async () =>
            {
                if (_process != started)
                    return;
                // Not available on this Windows: the whole output from now on. The player gone: look again soon.
                if (error is EntryPointNotFoundException or DllNotFoundException)
                    _processUnavailable = true;
                await Task.Delay(1000);
                Restart();
            });
            return;
        }
        try
        {
            _capture = new WasapiLoopbackCapture();
            _analyzer.SampleRate = _capture.WaveFormat.SampleRate;
            _analyzer.Clear();
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _capture.StartRecording();
            if (_devices is null)
            {
                // Another output chosen (headphones, a TV): capture that one instead.
                _devices = new MMDeviceEnumerator();
                _watcher = new DeviceWatcher(() => _ui.BeginInvoke(Restart));
                _devices.RegisterEndpointNotificationCallback(_watcher);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"Loopback capture failed: {ex.Message}");
            DisposeCapture();
        }
    }

    private void Stop()
    {
        DisposeCapture();
        _analyzer.Clear();
    }

    private void Restart()
    {
        if (_users == 0)
            return;
        DisposeCapture();
        Start();
    }

    private void DisposeCapture()
    {
        if (_process is not null)
        {
            var process = _process;
            _process = null;
            process.Dispose();
        }
        if (_capture is null)
            return;
        var capture = _capture;
        _capture = null;
        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnStopped;
        try
        {
            capture.StopRecording();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
        capture.Dispose();
    }

    // The device went away under the capture: try again in a moment.
    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null)
            return;
        _ui.BeginInvoke(async () =>
        {
            await Task.Delay(1000);
            Restart();
        });
    }

    // On the capture thread: the frames of the whole output.
    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded == 0)
            return;
        var format = capture.WaveFormat;
        // The shared-mode mix format of Windows is 32-bit float (plain or "extensible").
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        Feed(e.Buffer.AsSpan(0, e.BytesRecorded), format.Channels, format.BitsPerSample / 8, isFloat);
    }

    // On a capture thread: the frames, mixed down to mono, to the analyzer.
    private void Feed(ReadOnlySpan<byte> data, int channelCount, int bytesPerSample, bool isFloat)
    {
        int channels = Math.Max(1, channelCount);
        if (bytesPerSample == 0 || data.Length == 0)
            return;
        int frames = data.Length / (bytesPerSample * channels);
        float[] mono = ArrayPool<float>.Shared.Rent(frames);
        try
        {
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int offset = (f * channels + c) * bytesPerSample;
                    sum += isFloat && bytesPerSample == 4
                        ? BitConverter.ToSingle(data.Slice(offset, 4))
                        : bytesPerSample == 2 ? BitConverter.ToInt16(data.Slice(offset, 2)) / 32768f : 0;
                }
                mono[f] = sum / channels;
            }
            _analyzer.AddSamples(mono.AsSpan(0, frames));
            Interlocked.Exchange(ref _lastData, Stopwatch.GetTimestamp());
        }
        finally
        {
            ArrayPool<float>.Shared.Return(mono);
        }
    }

    public void Dispose()
    {
        DisposeCapture();
        if (_devices is not null && _watcher is not null)
        {
            try
            {
                _devices.UnregisterEndpointNotificationCallback(_watcher);
            }
            catch (COMException)
            {
            }
        }
        _devices?.Dispose();
        _devices = null;
    }

    private sealed class DeviceWatcher(Action defaultChanged) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                defaultChanged();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
