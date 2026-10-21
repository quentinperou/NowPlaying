using System.Windows;
using System.Windows.Interop;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// A widget window see-through where nothing is drawn, composed by DWM (a layered window, AllowsTransparency, would be
/// drawn by the processor at each frame of the bars): no backdrop, no rounded corners nor border from Windows 11, the
/// widget draws its own. Applied again when the theme of the app changes, which may touch every window.
/// </summary>
internal static class SeeThrough
{
    public static void Apply(Window window, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        window.Background = System.Windows.Media.Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int backdrop = DWMSBT_NONE, corners = DWMWCP_DONOTROUND, border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
