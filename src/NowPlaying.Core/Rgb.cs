using System.Globalization;

namespace NowPlaying.Core;

/// <summary>An opaque sRGB color, with the conversions the styles need (hex text, HSL, luminance).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>Reads "#RRGGBB" or "RRGGBB" (any case).</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;
        color = new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static Rgb ParseOr(string? text, Rgb fallback) => TryParse(text, out var color) ? color : fallback;

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Relative luminance (WCAG), 0 for black to 1 for white.</summary>
    public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    /// <summary>Whether black text reads better than white text on this color.</summary>
    public bool IsLight => (Luminance + 0.05) / 0.05 > 1.05 / (Luminance + 0.05);

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9)
            return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-9)
        {
            byte v = ToByte(l);
            return new Rgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        return new Rgb(ToByte(Hue(p, q, h + 1.0 / 3)), ToByte(Hue(p, q, h)), ToByte(Hue(p, q, h - 1.0 / 3)));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static double Linear(byte c)
    {
        double v = c / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
