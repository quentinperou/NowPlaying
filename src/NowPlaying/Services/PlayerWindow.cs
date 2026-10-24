using System.Diagnostics;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// Brings a player to the front, for a click on the cover of a widget (asked by the author, October 24, 2026; off by
/// default): its window last in front (Chrome: that window, not the tab that plays, which Windows does not tell), put
/// back from the taskbar if it was minimized. A Store app shows its windows through another process: it is opened
/// again from Windows' list of apps, which brings the running one. Called by the process that got the click, the only
/// one Windows lets bring a window to the front.
/// </summary>
internal static class PlayerWindow
{
    public static void BringToFront(string appId)
    {
        if (string.IsNullOrEmpty(appId))
            return;
        IntPtr found = IntPtr.Zero;
        // The windows from the front to the back: the first one of the player is the one it showed last.
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) && !IsIconic(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(hwnd) == 0)
                return true;
            GetWindowThreadProcessId(hwnd, out int pid);
            if (!AppVolume.BelongsTo(appId, pid))
                return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            if (IsIconic(found))
                ShowWindow(found, SW_RESTORE);
            SetForegroundWindow(found);
            return;
        }
        // A path is not an app of the list (a classic exe named by its path).
        if (appId.Contains('\\') || appId.Contains('/'))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Player window: {ex.Message}");
        }
    }
}
