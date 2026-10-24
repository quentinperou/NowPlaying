using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NowPlaying.Services;

/// <summary>
/// The sound of one program only (and of the processes it started: a browser plays through a process of its own),
/// as the real spectrum asked by the author on October 24, 2026: the player shown, not Discord, a game or a
/// notification. Windows' "process loopback" (Windows 10 2004 and later): a virtual capture device activated for a
/// process, read here as 16-bit stereo at 44.1 kHz on a thread of its own. NAudio has no such capture, hence these
/// few COM declarations.
/// </summary>
internal sealed class ProcessLoopback : IDisposable
{
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private readonly int _processId;

    /// <summary>A block of samples (16-bit stereo, interleaved), on the capture thread.</summary>
    public event Action<byte[], int>? DataAvailable;

    /// <summary>The capture ended by itself (the program closed, an error); on the capture thread.</summary>
    public event Action<Exception?>? Stopped;

    private ProcessLoopback(int processId)
    {
        _processId = processId;
        _thread = new Thread(Run) { IsBackground = true, Name = "NowPlaying process loopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Starts listening to <paramref name="processId"/> and its child processes.</summary>
    public static ProcessLoopback Start(int processId)
    {
        var capture = new ProcessLoopback(processId);
        capture._thread.Start();
        return capture;
    }

    private void Run()
    {
        Exception? failure = null;
        IAudioClient? client = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            client = Activate(_processId);
            format = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatEx>());
            Marshal.StructureToPtr(new WaveFormatEx
            {
                FormatTag = 1, // PCM
                Channels = Channels,
                SamplesPerSec = SampleRate,
                AvgBytesPerSec = SampleRate * Channels * BitsPerSample / 8,
                BlockAlign = Channels * BitsPerSample / 8,
                BitsPerSample = BitsPerSample,
            }, format, false);
            var session = Guid.Empty;
            Check(client.Initialize(0 /* shared */, StreamFlagsLoopback | StreamFlagsEventCallback | StreamFlagsAutoConvertPcm,
                2_000_000 /* 200 ms */, 0, format, ref session));
            using var ready = new AutoResetEvent(false);
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureId, out object service));
            var capture = (IAudioCaptureClient)service;
            Check(client.Start());
            int frameBytes = Channels * BitsPerSample / 8;
            byte[] buffer = new byte[SampleRate / 5 * frameBytes];
            var handles = new WaitHandle[] { ready, _stop.WaitHandle };
            while (WaitHandle.WaitAny(handles, 500) != 1)
            {
                while (capture.GetNextPacketSize(out int frames) >= 0 && frames > 0)
                {
                    Check(capture.GetBuffer(out IntPtr data, out frames, out int flags, out _, out _));
                    int bytes = frames * frameBytes;
                    if (bytes > buffer.Length)
                        buffer = new byte[bytes];
                    if ((flags & BufferFlagsSilent) != 0)
                        Array.Clear(buffer, 0, bytes);
                    else
                        Marshal.Copy(data, buffer, 0, bytes);
                    capture.ReleaseBuffer(frames);
                    DataAvailable?.Invoke(buffer, bytes);
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TimeoutException or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        {
            Debug.WriteLine($"Process loopback: {ex.Message}");
            failure = ex;
        }
        finally
        {
            if (format != IntPtr.Zero)
                Marshal.FreeCoTaskMem(format);
            if (client is not null)
                Marshal.ReleaseComObject(client);
        }
        if (!_stop.IsSet)
            Stopped?.Invoke(failure);
    }

    public void Dispose()
    {
        _stop.Set();
        if (Thread.CurrentThread != _thread)
            _thread.Join(1000);
    }

    // ActivateAudioInterfaceAsync on the virtual device of the process loopback, waited for here (an MTA thread).
    private static IAudioClient Activate(int processId)
    {
        var parameters = new ActivationParams { ActivationType = 1 /* process loopback */, TargetProcessId = processId, LoopbackMode = 0 /* include the tree */ };
        IntPtr blob = Marshal.AllocCoTaskMem(Marshal.SizeOf<ActivationParams>());
        IntPtr variant = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(parameters, blob, false);
            Marshal.StructureToPtr(new PropVariantBlob { Type = 65 /* VT_BLOB */, Size = Marshal.SizeOf<ActivationParams>(), Data = blob }, variant, false);
            var done = new CompletionHandler();
            ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, variant, done, out _);
            if (!done.Completed.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("No answer from the audio service.");
            return done.Client ?? throw done.Error ?? new InvalidOperationException("No audio client.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(variant);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static void Check(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
    }

    private const int StreamFlagsLoopback = 0x00020000;
    private const int StreamFlagsEventCallback = 0x00040000;
    private const int StreamFlagsAutoConvertPcm = unchecked((int)0x80000000);
    private const int BufferFlagsSilent = 0x2;

    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEventSlim Completed { get; } = new();
        public IAudioClient? Client { get; private set; }
        public Exception? Error { get; private set; }

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out int result, out object activated);
                if (result < 0)
                    Error = Marshal.GetExceptionForHR(result);
                else
                    Client = (IAudioClient)activated;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Error = ex;
            }
            Completed.Set();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams
    {
        public int ActivationType;
        public int TargetProcessId;
        public int LoopbackMode;
    }

    // A PROPVARIANT holding a BLOB (24 bytes on x64: the type, three reserved words, the size, the pointer).
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public int Size;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    // Lets Windows call the completion handler from any thread.
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, ref Guid sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        [PreserveSig] int ReleaseBuffer(int frames);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }
}
