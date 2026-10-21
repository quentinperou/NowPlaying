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
/// The sound of the PC for the "real spectrum" visualizer: a WASAPI loopback capture of the default output, read by
/// <see cref="SpectrumAnalyzer"/>. It runs only while someone looks (the widget at rest with its bars, an overlay
/// page with a visualizer) and something plays: <see cref="Acquire"/> / <see cref="Release"/> count them.
/// </summary>
internal sealed class AudioSpectrum : IDisposable
{
    public const int Bars = 24;

    private readonly SpectrumAnalyzer _analyzer = new(Bars);
    private readonly float[] _levels = new float[Bars];
    private readonly Dispatcher _ui;
    private WasapiLoopbackCapture? _capture;
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

    // On the capture thread: the frames, mixed down to mono.
    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded == 0)
            return;
        var format = capture.WaveFormat;
        int channels = Math.Max(1, format.Channels);
        // The shared-mode mix format of Windows is 32-bit float (plain or "extensible").
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample == 0)
            return;
        int frames = e.BytesRecorded / (bytesPerSample * channels);
        float[] mono = ArrayPool<float>.Shared.Rent(frames);
        try
        {
            var data = e.Buffer.AsSpan(0, e.BytesRecorded);
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
