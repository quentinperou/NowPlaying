namespace NowPlaying.Core;

/// <summary>A color with its opacity (0 to 255), for backgrounds that let the taskbar or the scene show through.</summary>
public readonly record struct Argb(byte A, Rgb Color)
{
    /// <summary>"rgba(r,g,b,a)" for the overlay page.</summary>
    public string ToCss() => FormattableString.Invariant($"rgba({Color.R},{Color.G},{Color.B},{A / 255.0:0.###})");
}

/// <summary>The colors of the widget or of an overlay, worked out from its style and what plays.</summary>
public readonly record struct SurfaceColors(Argb Background, Argb Text, Argb SecondaryText, Argb Track, Argb Hover, bool Frosted)
{
    /// <summary>
    /// <paramref name="onDarkSurface"/>: the widget on a dark taskbar (the overlays always count as dark: a stream
    /// scene). <paramref name="coverColor"/>: the color of the current cover, if known. <paramref name="glassOpacity"/>:
    /// the dark glass of the overlays as opaque as the style's opacity (the floating widget, whose veil the user sets
    /// over the blur behind it), rather than its fixed veil. <paramref name="backdrop"/>: the mean color of what is
    /// behind, when it is read (the floating widget, around itself): the text is then dark or light after the
    /// background as seen, the style's color or veil over it (asked by the author, October 22, 2026).
    /// </summary>
    public static SurfaceColors Resolve(SurfaceStyle style, Rgb? coverColor, bool onDarkSurface, bool overlay, bool glassOpacity = false,
        Rgb? backdrop = null)
    {
        if (style.ColorMode == ColorMode.Glass)
        {
            if (overlay)
            {
                // A clear glass: white over a light background, dark over a dark one (the floating widget, which reads
                // what is behind it); dark when that is not known (OBS: a stream scene).
                var tint = backdrop is { IsLight: true } ? Rgb.White : new Rgb(16, 16, 26);
                var veil = new Argb(glassOpacity ? Alpha(style.Opacity) : (byte)140, tint);
                return backdrop is { } behind && Seen(veil, behind).IsLight ? OnLight(veil, frosted: true) : OnDark(veil, frosted: true);
            }
            return onDarkSurface
                ? OnDark(new Argb(19, Rgb.White), frosted: false)
                : OnLight(new Argb(13, Rgb.Black));
        }

        var color = style.ColorMode == ColorMode.Cover
            ? coverColor ?? new Rgb(58, 62, 84)
            : Rgb.ParseOr(style.CustomColor, new Rgb(91, 108, 240));
        var background = new Argb(Alpha(style.Opacity), color);
        // Seen through, the background mixes with what is behind it: count a light veil on a light taskbar as light.
        bool light = backdrop is { } seenOver
            ? Seen(background, seenOver).IsLight
            : color.IsLight || (!onDarkSurface && !overlay && style.Opacity < 45);
        return light ? OnLight(background) : OnDark(background, frosted: false);
    }

    private static byte Alpha(int opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 100) * 2.55);

    /// <summary>A see-through color over another, as the screen shows them.</summary>
    public static Rgb Seen(Argb top, Rgb behind)
    {
        double a = top.A / 255.0;
        byte Mix(byte over, byte under) => (byte)Math.Round(over * a + under * (1 - a));
        return new Rgb(Mix(top.Color.R, behind.R), Mix(top.Color.G, behind.G), Mix(top.Color.B, behind.B));
    }

    private static SurfaceColors OnDark(Argb background, bool frosted) => new(
        background, new Argb(255, Rgb.White), new Argb(178, Rgb.White), new Argb(64, Rgb.White), new Argb(28, Rgb.White), frosted);

    private static SurfaceColors OnLight(Argb background, bool frosted = false) => new(
        background, new Argb(255, new Rgb(21, 21, 24)), new Argb(150, Rgb.Black), new Argb(44, Rgb.Black), new Argb(16, Rgb.Black), frosted);
}
