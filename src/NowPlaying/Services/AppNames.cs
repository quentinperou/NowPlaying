using System.Runtime.InteropServices;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// The name of a player as Windows shows it in its own media flyout: the display name of its app id in the
/// Applications folder (shell:AppsFolder, the Start menu's), "Firefox" for the hash Firefox names its sessions with.
/// <see cref="AppIdentity.FriendlyName"/> when Windows has none (a bare exe name). Asked once per app.
/// </summary>
internal static class AppNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase);

    public static string Of(string appId)
    {
        if (Known.TryGetValue(appId, out var name))
            return name;
        name = FromWindows(appId) ?? AppIdentity.FriendlyName(appId);
        Known[appId] = name;
        return name;
    }

    private static string? FromWindows(string appId)
    {
        // A path is not an app id of the Applications folder.
        if (string.IsNullOrWhiteSpace(appId) || appId.Contains('\\') || appId.Contains('/'))
            return null;
        try
        {
            SHCreateItemFromParsingName(@"shell:AppsFolder\" + appId, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
            item.GetDisplayName(SIGDN_NORMALDISPLAY, out IntPtr text);
            try
            {
                string? name = Marshal.PtrToStringUni(text);
                return string.IsNullOrWhiteSpace(name) || string.Equals(name, appId, StringComparison.OrdinalIgnoreCase) ? null : name;
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const uint SIGDN_NORMALDISPLAY = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
