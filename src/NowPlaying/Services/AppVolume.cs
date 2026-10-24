using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using NowPlaying.Core;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// The volume of the app that plays, as in the Windows volume mixer (not the volume of the whole PC): the audio
/// sessions of the default output whose process is the player's. Read and written only when the wheel turns.
/// </summary>
internal static class AppVolume
{
    /// <summary>The player's volume, 0 to 1; null when it has no sound session now (nothing heard yet, another device).</summary>
    public static float? Get(string appId) => Apply(appId, null);

    /// <summary>Sets the player's volume (0 to 1) on all its sessions; returns the volume set, or null if none.</summary>
    public static float? Set(string appId, float volume) => Apply(appId, Math.Clamp(volume, 0, 1));

    private static float? Apply(string appId, float? newVolume)
    {
        if (string.IsNullOrEmpty(appId))
            return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            float? found = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                int pid = (int)session.GetProcessID;
                if (pid == 0 || !BelongsTo(appId, pid))
                    continue;
                if (newVolume is { } v)
                    session.SimpleAudioVolume.Volume = v;
                found ??= session.SimpleAudioVolume.Volume;
            }
            return found;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"App volume failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The process that plays the player's sound (its audio session on the default output, the one sounding first):
    /// for a browser, the process of its own that it plays through. Null when it has none now.
    /// </summary>
    public static int? SoundProcess(string appId)
    {
        if (string.IsNullOrEmpty(appId))
            return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            int? found = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                int pid = (int)session.GetProcessID;
                if (pid == 0 || !BelongsTo(appId, pid))
                    continue;
                if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                    return pid;
                found ??= pid;
            }
            return found;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"Sound process: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether the process <paramref name="pid"/> is the player <paramref name="appId"/> (also used to find its window).</summary>
    internal static bool BelongsTo(string appId, int pid)
    {
        string name;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        return AppIdentity.Matches(appId, name, PackageFamily(pid));
    }

    // The Store package of a process ("SpotifyAB.SpotifyMusic_zpdnekdrzrea0"), null for a classic app.
    private static string? PackageFamily(int pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            int length = 0;
            if (GetPackageFamilyName(handle, ref length, null) != 122 /* ERROR_INSUFFICIENT_BUFFER */ || length == 0)
                return null;
            var name = new StringBuilder(length);
            return GetPackageFamilyName(handle, ref length, name) == 0 ? name.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
