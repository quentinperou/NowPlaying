using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NowPlaying.Widgets;

/// <summary>
/// The pictures of the widget, made here as data URIs (an Adaptive Card has no blur, no rounded image, no icon of its
/// own): the background, the cover blurred; the cover with rounded corners; the icons of the controls. Each in two
/// looks, as the board's theme: darkened under white text and icons on a dark board, lightened under dark ones on a
/// light board (Windows writes the widget's name over the background in the color of the theme). Twice the size they
/// are shown at, for high-DPI screens.
/// </summary>
internal static class Artwork
{
    /// <summary>The cover, shown at 176 px in the large widget: twice, at most (never more than the player gives).</summary>
    private const int CoverSize = 352;
    // 6 px at the size of the medium widget (108 px).
    private const double CoverRadius = 6.0 / 108;
    private const int BackgroundSize = 320;
    // The background is drawn from a cover this small: blurred, then smoothly enlarged.
    private const int BackgroundSource = 24;

    // On a dark background: white glyphs, play in a white circle. On a light one: the same in dark.
    public static readonly string Previous = Icon(48, (Rect(6, 5, 8, 19), White), (Triangle(20, 5, 20, 19, 9, 12), White));
    public static readonly string Next = Icon(48, (Rect(16, 5, 18, 19), White), (Triangle(4, 5, 4, 19, 15, 12), White));
    public static readonly string Play = Icon(96, (Circle(12, 12, 12), White), (Triangle(9.5, 7, 9.5, 17, 17.5, 12), Dark));
    public static readonly string Pause = Icon(96, (Circle(12, 12, 12), White), (Rect(8.5, 7, 10.5, 17), Dark), (Rect(13.5, 7, 15.5, 17), Dark));
    public static readonly string PreviousOnLight = Icon(48, (Rect(6, 5, 8, 19), Dark), (Triangle(20, 5, 20, 19, 9, 12), Dark));
    public static readonly string NextOnLight = Icon(48, (Rect(16, 5, 18, 19), Dark), (Triangle(4, 5, 4, 19, 15, 12), Dark));
    public static readonly string PlayOnLight = Icon(96, (Circle(12, 12, 12), Dark), (Triangle(9.5, 7, 9.5, 17, 17.5, 12), White));
    public static readonly string PauseOnLight = Icon(96, (Circle(12, 12, 12), Dark), (Rect(8.5, 7, 10.5, 17), White), (Rect(13.5, 7, 15.5, 17), White));

    /// <summary>Nothing playing, or a track without cover: the colors of the app's icon, darkened or lightened.</summary>
    public static readonly string DefaultBackground = Gradient((60, 68, 150), (20, 22, 70));
    public static readonly string DefaultBackgroundOnLight = Gradient((226, 229, 255), (196, 200, 240));

    /// <summary>The cover with rounded corners, and the backgrounds made from it (dark, light).</summary>
    public static async Task<(string Cover, string Background, string BackgroundOnLight)> FromCoverAsync(byte[] bytes)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);

        // No bigger than the player's own picture: enlarged here, it would only weigh more.
        int coverSize = (int)Math.Min(CoverSize, Math.Min(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
        byte[] cover = await SquareAsync(decoder, coverSize);
        RoundCorners(cover, coverSize, (int)Math.Round(coverSize * CoverRadius));
        byte[] small = await SquareAsync(decoder, BackgroundSource);
        return (DataUri("image/png", await EncodeAsync(BitmapEncoder.PngEncoderId, cover, coverSize)),
            DataUri("image/jpeg", await EncodeAsync(BitmapEncoder.JpegEncoderId, Backdrop(small, light: false), BackgroundSize)),
            DataUri("image/jpeg", await EncodeAsync(BitmapEncoder.JpegEncoderId, Backdrop(small, light: true), BackgroundSize)));
    }

    // The middle square of the picture (a video thumbnail is wide), at size × size, BGRA.
    private static async Task<byte[]> SquareAsync(BitmapDecoder decoder, int size)
    {
        double scale = (double)size / Math.Min(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        uint width = (uint)Math.Max(size, Math.Round(decoder.OrientedPixelWidth * scale));
        uint height = (uint)Math.Max(size, Math.Round(decoder.OrientedPixelHeight * scale));
        var transform = new BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
            InterpolationMode = BitmapInterpolationMode.Fant,
            Bounds = new BitmapBounds { X = (width - (uint)size) / 2, Y = (height - (uint)size) / 2, Width = (uint)size, Height = (uint)size },
        };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var pixels = data.DetachPixelData();
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        return pixels;
    }

    // Transparent corners, smooth (4 × 4 samples a pixel).
    private static void RoundCorners(byte[] pixels, int size, int radius)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool left = x < radius, right = x >= size - radius, top = y < radius, bottom = y >= size - radius;
                if (!((left || right) && (top || bottom)))
                    continue;
                double cx = left ? radius : size - radius, cy = top ? radius : size - radius;
                int inside = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double dx = x + (sx + 0.5) / 4 - cx, dy = y + (sy + 0.5) / 4 - cy;
                        if (dx * dx + dy * dy <= radius * radius)
                            inside++;
                    }
                pixels[(y * size + x) * 4 + 3] = (byte)(inside * 255 / 16);
            }
        }
    }

    // The background: the tiny cover blurred, enlarged smoothly, more vivid; darker towards the bottom, or a pastel
    // (half white) under dark text.
    private static byte[] Backdrop(byte[] small, bool light)
    {
        const int n = BackgroundSource;
        var channels = new double[3][];
        for (int c = 0; c < 3; c++)
        {
            channels[c] = new double[n * n];
            for (int i = 0; i < n * n; i++)
                channels[c][i] = small[i * 4 + c];
            for (int pass = 0; pass < 2; pass++)
                BoxBlur(channels[c], n, 2);
        }

        const int size = BackgroundSize;
        var pixels = new byte[size * size * 4];
        Span<double> bgr = stackalloc double[3];
        for (int y = 0; y < size; y++)
        {
            double sy = Math.Clamp((y + 0.5) * n / size - 0.5, 0, n - 1);
            int y0 = (int)sy, y1 = Math.Min(n - 1, y0 + 1);
            double fy = sy - y0;
            // 70 % of the light at the top, 45 % at the bottom.
            double shade = 0.70 - 0.25 * y / (size - 1);
            for (int x = 0; x < size; x++)
            {
                double sx = Math.Clamp((x + 0.5) * n / size - 0.5, 0, n - 1);
                int x0 = (int)sx, x1 = Math.Min(n - 1, x0 + 1);
                double fx = sx - x0;
                for (int c = 0; c < 3; c++)
                {
                    var ch = channels[c];
                    bgr[c] = (ch[y0 * n + x0] * (1 - fx) + ch[y0 * n + x1] * fx) * (1 - fy)
                        + (ch[y1 * n + x0] * (1 - fx) + ch[y1 * n + x1] * fx) * fy;
                }
                double luma = 0.114 * bgr[0] + 0.587 * bgr[1] + 0.299 * bgr[2];
                int i = (y * size + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    double vivid = Math.Clamp(luma + (bgr[c] - luma) * 1.3, 0, 255);
                    pixels[i + c] = (byte)(light ? vivid * 0.42 + 255 * 0.58 : vivid * shade);
                }
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static void BoxBlur(double[] values, int n, int radius)
    {
        var copy = new double[values.Length];
        for (int horizontal = 0; horizontal < 2; horizontal++)
        {
            Array.Copy(values, copy, values.Length);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = horizontal == 0 ? Math.Clamp(x + k, 0, n - 1) : x;
                        int yy = horizontal == 0 ? y : Math.Clamp(y + k, 0, n - 1);
                        sum += copy[yy * n + xx];
                    }
                    values[y * n + x] = sum / (2 * radius + 1);
                }
        }
    }

    // From the top left to the bottom right (the icon goes from #6C7BFF to #2A2F9B).
    private static string Gradient((double R, double G, double B) from, (double R, double G, double B) to)
    {
        const int size = BackgroundSize;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double t = (x + y) / (2.0 * (size - 1));
                int i = (y * size + x) * 4;
                pixels[i] = (byte)(from.B + (to.B - from.B) * t);
                pixels[i + 1] = (byte)(from.G + (to.G - from.G) * t);
                pixels[i + 2] = (byte)(from.R + (to.R - from.R) * t);
                pixels[i + 3] = 255;
            }
        return DataUri("image/jpeg", EncodeAsync(BitmapEncoder.JpegEncoderId, pixels, size).GetAwaiter().GetResult());
    }

    // ---------------------------------------------------------------- Icons

    private const uint White = 0xFFFFFF;
    private const uint Dark = 0x1B1B1F;

    // On the 24 × 24 grid of the icons.
    private delegate bool Shape(double x, double y);

    private static Shape Rect(double x0, double y0, double x1, double y1) => (x, y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;

    private static Shape Circle(double cx, double cy, double r) => (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    private static Shape Triangle(double ax, double ay, double bx, double by, double cx, double cy) => (x, y) =>
    {
        double d1 = (x - bx) * (ay - by) - (ax - bx) * (y - by);
        double d2 = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
        double d3 = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    };

    // Shapes painted in order (the last one on top), 4 × 4 samples a pixel for smooth edges.
    private static string Icon(int size, params (Shape Shape, uint Color)[] layers)
    {
        var pixels = new byte[size * size * 4];
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0;
                int covered = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double x = (px + (sx + 0.5) / 4) * 24 / size, y = (py + (sy + 0.5) / 4) * 24 / size;
                        uint? color = null;
                        foreach (var (shape, c) in layers)
                            if (shape(x, y))
                                color = c;
                        if (color is not { } col)
                            continue;
                        covered++;
                        r += (col >> 16) & 0xFF;
                        g += (col >> 8) & 0xFF;
                        b += col & 0xFF;
                    }
                int i = (py * size + px) * 4;
                if (covered > 0)
                {
                    pixels[i] = (byte)(b / covered);
                    pixels[i + 1] = (byte)(g / covered);
                    pixels[i + 2] = (byte)(r / covered);
                    pixels[i + 3] = (byte)(covered * 255 / 16);
                }
            }
        return DataUri("image/png", EncodeAsync(BitmapEncoder.PngEncoderId, pixels, size).GetAwaiter().GetResult());
    }

    // ---------------------------------------------------------------- Encoding

    private static async Task<byte[]> EncodeAsync(Guid encoderId, byte[] pixels, int size)
    {
        using var output = new InMemoryRandomAccessStream();
        var options = encoderId == BitmapEncoder.JpegEncoderId
            ? new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.85f, PropertyType.Single) }
            : new BitmapPropertySet();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, output, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,
            encoderId == BitmapEncoder.JpegEncoderId ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight,
            (uint)size, (uint)size, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    private static string DataUri(string type, byte[] bytes) => $"data:{type};base64,{Convert.ToBase64String(bytes)}";
}
