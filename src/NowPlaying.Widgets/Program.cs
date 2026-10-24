using System.Runtime.InteropServices;

namespace NowPlaying.Widgets;

/// <summary>
/// Started by Windows (the Widgets board) with -RegisterProcessAsComServer, as the package declares: it registers the
/// widget provider, then lives as long as a NowPlaying widget is on the board. Started any other way, it does nothing.
/// </summary>
internal static class Program
{
    // The class of the provider, as the package declares it: the version from GitHub (package/AppxManifest.xml) and
    // the Store version (store/AppxManifest.xml) each have their own, so that both can be installed on one PC.
    private static readonly Guid ClassicClassId = new("6E5961A8-95F5-4413-ADCB-B9BD8D1B3EDA");
    private static readonly Guid StoreClassId = new("FD626078-0E6B-4C13-8494-3A132E92C261");

    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;

    private static readonly ManualResetEvent s_noMoreWidgets = new(false);

    [MTAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
            return;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var provider = new WidgetProvider(() => s_noMoreWidgets.Set());
        var classId = AppLink.StoreAppId() is null ? ClassicClassId : StoreClassId;
        int hr = CoRegisterClassObject(classId, new ClassFactory(provider), ClsctxLocalServer, RegclsMultipleUse, out uint cookie);
        if (hr != 0)
        {
            Log.Write($"CoRegisterClassObject failed: 0x{hr:X8}");
            return;
        }
        Log.Write("Registered");
        s_noMoreWidgets.WaitOne();
        CoRevokeClassObject(cookie);
        provider.Dispose();
        Log.Write("No widget left: exit");
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject([MarshalAs(UnmanagedType.LPStruct)] Guid classId,
        [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
}

/// <summary>A few lines in %TEMP%\NowPlaying-Widgets.log: this process has no window to tell anything.</summary>
internal static class Log
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NowPlaying-Widgets.log");
    private static readonly object Lock = new();

    public static void Write(string line)
    {
        try
        {
            lock (Lock)
            {
                if (File.Exists(Path) && new FileInfo(Path).Length > 200_000)
                    File.Delete(Path);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }
}
