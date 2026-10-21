using System.Runtime.InteropServices;
using NowPlaying.Core;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// Reads the mean color of a few screen pixels: the line of taskbar just above the widget, to choose a readable text
/// on whatever the taskbar shows (dark, light, the accent color, a wallpaper through a see-through taskbar). Nothing is
/// kept nor sent: a strip of a few hundred pixels, read when the widget moves or Windows' look changes.
/// </summary>
internal static class ScreenSampler
{
    /// <summary>The mean color of the rectangle (screen pixels), null when it cannot be read.</summary>
    public static Rgb? Mean(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                return null;
            IntPtr old = SelectObject(memory, bitmap);
            bool copied = BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY);
            SelectObject(memory, old);
            if (!copied)
                return null;
            var pixels = new int[width * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            long r = 0, g = 0, b = 0;
            foreach (int p in pixels)
            {
                r += (p >> 16) & 0xFF;
                g += (p >> 8) & 0xFF;
                b += p & 0xFF;
            }
            return new Rgb((byte)(r / pixels.Length), (byte)(g / pixels.Length), (byte)(b / pixels.Length));
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
