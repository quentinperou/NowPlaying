using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace NowPlaying.Services;

/// <summary>
/// Calls back when a value of a registry key of the current user changes. Windows Settings changes some taskbar
/// options (the Widgets switch, the alignment) without telling the apps: their key is watched instead. One thread
/// that sleeps on a Windows notification: nothing runs until the key changes.
/// </summary>
internal sealed class RegistryWatcher : IDisposable
{
    private const int RegNotifyChangeLastSet = 0x4;

    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread? _thread;

    /// <param name="changed">Called on the watching thread.</param>
    public RegistryWatcher(string path, Action changed)
    {
        var key = Registry.CurrentUser.OpenSubKey(path);
        if (key is null)
            return;
        _thread = new Thread(() => Watch(key, changed)) { IsBackground = true, Name = "Registry watcher" };
        _thread.Start();
    }

    private void Watch(RegistryKey key, Action changed)
    {
        using (key)
        using (var signal = new AutoResetEvent(false))
        {
            while (true)
            {
                if (RegNotifyChangeKeyValue(key.Handle, false, RegNotifyChangeLastSet, signal.SafeWaitHandle, true) != 0)
                    return;
                if (WaitHandle.WaitAny([signal, _stop]) == 1)
                    return;
                changed();
            }
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(1000);
        _stop.Dispose();
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int notifyFilter, SafeWaitHandle @event, bool asynchronous);
}
