using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace NowPlaying.Widgets;

[ComImport]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outer, ref Guid interfaceId, out IntPtr instance);

    [PreserveSig]
    int LockServer(bool doLock);
}

/// <summary>Hands the one provider of this process to Windows (COM activation of the class of the package).</summary>
[ComVisible(true)]
internal sealed class ClassFactory(WidgetProvider provider) : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);
    private static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");

    public int CreateInstance(IntPtr outer, ref Guid interfaceId, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero)
            return ClassENoAggregation;
        if (interfaceId != typeof(IWidgetProvider).GUID && interfaceId != IUnknown)
            return ENoInterface;
        instance = MarshalInspectable<IWidgetProvider>.FromManaged(provider);
        return 0;
    }

    public int LockServer(bool doLock) => 0;
}
