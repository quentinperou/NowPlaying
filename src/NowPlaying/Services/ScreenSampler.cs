using System.Runtime.InteropServices;
using NowPlaying.Core;
using static NowPlaying.Services.NativeMethods;

namespace NowPlaying.Services;

/// <summary>
/// Reads the mean color of a few screen pixels: the line of taskbar just above the widget, or a thin band around the
/// floating widget, to choose a readable text on whatever shows there (dark, light, the accent color, a wallpaper
/// through a see-through taskbar, the page under the floating widget). Nothing is kept nor sent: strips of a few
/// hundred pixels, read when a widget moves or Windows' look changes, and now and then while the floating widget plays.
/// </summary>
internal static class ScreenSampler
{
    /// <summary>The mean color of the rectangle (screen pixels), null when it cannot be read.</summary>
    public static Rgb? Mean(int x, int y, int width, int height) => Mean((x, y, width, height));

    /// <summary>The mean color of several rectangles together (empty ones are skipped), null when none can be read.</summary>
    public static Rgb? Mean(params (int X, int Y, int Width, int Height)[] areas)
    {
        long r = 0, g = 0, b = 0, count = 0;
        foreach (var (x, y, width, height) in areas)
        {
            if (Sum(x, y, width, height) is not { } sum)
                continue;
            (r, g, b, count) = (r + sum.R, g + sum.G, b + sum.B, count + sum.Count);
        }
        return count == 0 ? null : new Rgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static (long R, long G, long B, long Count)? Sum(int x, int y, int width, int height)
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
            return (r, g, b, pixels.Length);
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
