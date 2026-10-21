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
    /// scene). <paramref name="coverColor"/>: the color of the current cover, if known.
    /// </summary>
    public static SurfaceColors Resolve(SurfaceStyle style, Rgb? coverColor, bool onDarkSurface, bool overlay)
    {
        if (style.ColorMode == ColorMode.Glass)
        {
            if (overlay)
                return OnDark(new Argb(140, new Rgb(16, 16, 26)), frosted: true);
            return onDarkSurface
                ? OnDark(new Argb(19, Rgb.White), frosted: false)
                : OnLight(new Argb(13, Rgb.Black));
        }

        var color = style.ColorMode == ColorMode.Cover
            ? coverColor ?? new Rgb(58, 62, 84)
            : Rgb.ParseOr(style.CustomColor, new Rgb(91, 108, 240));
        var background = new Argb((byte)Math.Round(Math.Clamp(style.Opacity, 0, 100) * 2.55), color);
        // Seen through, the background mixes with what is behind it: count a light veil on a light taskbar as light.
        bool light = color.IsLight || (!onDarkSurface && !overlay && style.Opacity < 45);
        return light ? OnLight(background) : OnDark(background, frosted: false);
    }

    private static SurfaceColors OnDark(Argb background, bool frosted) => new(
        background, new Argb(255, Rgb.White), new Argb(178, Rgb.White), new Argb(64, Rgb.White), new Argb(28, Rgb.White), frosted);

    private static SurfaceColors OnLight(Argb background) => new(
        background, new Argb(255, new Rgb(21, 21, 24)), new Argb(150, Rgb.Black), new Argb(44, Rgb.Black), new Argb(16, Rgb.Black), false);
}
