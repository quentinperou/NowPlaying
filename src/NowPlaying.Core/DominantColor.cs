namespace NowPlaying.Core;

/// <summary>
/// The color of a cover, for the "Cover" color mode: the most present vivid hue (not the plain average, which turns
/// every colorful cover brown), toned down so that white text stays readable on it, as in the Spectral widgets.
/// </summary>
public static class DominantColor
{
    private const int HueBuckets = 24;

    /// <summary>
    /// Picks the color of a picture given as 32-bit BGRA pixels (<paramref name="stride"/> bytes per row). A grey
    /// picture gives a grey. Null for an empty or fully transparent picture.
    /// </summary>
    public static Rgb? FromBgra(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
            return null;

        var weight = new double[HueBuckets];
        var sumR = new double[HueBuckets];
        var sumG = new double[HueBuckets];
        var sumB = new double[HueBuckets];
        double allR = 0, allG = 0, allB = 0;
        int all = 0;

        // About 64 x 64 samples whatever the size of the picture.
        int step = Math.Max(1, Math.Max(width, height) / 64);
        for (int y = 0; y < height; y += step)
        {
            for (int x = 0; x < width; x += step)
            {
                int i = y * stride + x * 4;
                if (i + 3 >= pixels.Length || pixels[i + 3] < 128)
                    continue;
                var c = new Rgb(pixels[i + 2], pixels[i + 1], pixels[i]);
                allR += c.R; allG += c.G; allB += c.B; all++;

                var (h, s, l) = c.ToHsl();
                if (s < 0.18 || l < 0.1 || l > 0.9)
                    continue;
                // Vivid mid-tones count most.
                double w = s * (1 - Math.Abs(l - 0.5) * 1.4);
                int bucket = (int)(h / 360 * HueBuckets) % HueBuckets;
                weight[bucket] += w;
                sumR[bucket] += c.R * w; sumG[bucket] += c.G * w; sumB[bucket] += c.B * w;
            }
        }
        if (all == 0)
            return null;

        // A hue that covers at least a few percent of the picture; neighbours count with it (hues across a boundary).
        int best = -1;
        double bestWeight = 0;
        for (int b = 0; b < HueBuckets; b++)
        {
            double w = weight[b] + 0.5 * (weight[(b + 1) % HueBuckets] + weight[(b + HueBuckets - 1) % HueBuckets]);
            if (w > bestWeight)
            {
                bestWeight = w;
                best = b;
            }
        }
        Rgb found = best >= 0 && weight[best] > all * 0.02
            ? new Rgb((byte)(sumR[best] / weight[best]), (byte)(sumG[best] / weight[best]), (byte)(sumB[best] / weight[best]))
            : new Rgb((byte)(allR / all), (byte)(allG / all), (byte)(allB / all));
        return ForBackground(found);
    }

    /// <summary>Keeps the hue, with a lightness and saturation under which white text reads well.</summary>
    public static Rgb ForBackground(Rgb color)
    {
        var (h, s, l) = color.ToHsl();
        s = Math.Min(s, 0.62);
        l = Math.Clamp(l, 0.2, 0.36);
        // Yellows and greens stay bright at the same lightness: darker until white text wins.
        var result = Rgb.FromHsl(h, s, l);
        while (result.IsLight && l > 0.12)
        {
            l -= 0.02;
            result = Rgb.FromHsl(h, s, l);
        }
        return result;
    }
}
