using System.Text.Json.Serialization;

namespace NowPlaying.Core;

/// <summary>Background of the widget or of an overlay.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ColorMode>))]
public enum ColorMode
{
    /// <summary>The widget: a light veil on the taskbar. The overlays: frosted dark glass.</summary>
    Glass,
    /// <summary>The color of the cover of each track.</summary>
    Cover,
    /// <summary>A color chosen by the user.</summary>
    Custom,
}

[JsonConverter(typeof(JsonStringEnumConverter<VisualizerMode>))]
public enum VisualizerMode
{
    /// <summary>The real spectrum of the sound of the PC.</summary>
    Spectrum,
    /// <summary>Calm waves while playing: costs nothing, follows nothing.</summary>
    Animation,
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetPosition>))]
public enum WidgetPosition
{
    /// <summary>The left of the taskbar: free when its icons are centered (Windows puts its weather widget there).</summary>
    Left,
    /// <summary>Just left of the notification area and the clock.</summary>
    NearClock,
}

[JsonConverter(typeof(JsonStringEnumConverter<IdleMode>))]
public enum IdleMode
{
    /// <summary>Nothing playing nor paused: the widget goes away.</summary>
    Hide,
    /// <summary>The last track stays, greyed out.</summary>
    KeepLast,
}

[JsonConverter(typeof(JsonStringEnumConverter<OverlayAppearance>))]
public enum OverlayAppearance
{
    /// <summary>On the scene as long as a track is known.</summary>
    Always,
    /// <summary>A few seconds at each new track, then faded out (asked by the author, October 24, 2026).</summary>
    OnNewTrack,
}

[JsonConverter(typeof(JsonStringEnumConverter<OverlayLayout>))]
public enum OverlayLayout
{
    /// <summary>Like the widget: cover, title, artist, visualizer.</summary>
    Bar,
    /// <summary>Cover, title, artist, progress.</summary>
    Card,
}

/// <summary>What the widget and the overlays have in common: colors, size, text, the parts shown.</summary>
public abstract class SurfaceStyle
{
    public const string DefaultFont = "Segoe UI Variable Text";

    /// <summary>Fonts offered in the app (installed with Windows 11).</summary>
    public static readonly string[] Fonts = [DefaultFont, "Segoe UI", "Bahnschrift", "Cascadia Code", "Consolas", "Georgia", "Arial"];

    public ColorMode ColorMode { get; set; } = ColorMode.Glass;

    /// <summary>"#RRGGBB" of the Custom color mode.</summary>
    public string CustomColor { get; set; } = "#5B6CF0";

    /// <summary>Opacity of the Cover and Custom backgrounds, in percent.</summary>
    public int Opacity { get; set; } = 85;

    public int Width { get; set; }
    public int CornerRadius { get; set; }
    public int FontSize { get; set; }
    public string Font { get; set; } = DefaultFont;

    public bool ShowCover { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowVisualizer { get; set; } = true;

    public VisualizerMode Visualizer { get; set; } = VisualizerMode.Spectrum;

    public abstract (int Min, int Max) WidthRange { get; }
    public abstract (int Min, int Max) RadiusRange { get; }
    public abstract (int Min, int Max) FontSizeRange { get; }

    /// <summary>Puts values read from a settings file back within their ranges.</summary>
    protected void NormalizeCommon(SurfaceStyle defaults)
    {
        if (!Enum.IsDefined(ColorMode)) ColorMode = defaults.ColorMode;
        if (!Enum.IsDefined(Visualizer)) Visualizer = defaults.Visualizer;
        CustomColor = Rgb.TryParse(CustomColor, out var c) ? c.ToHex() : defaults.CustomColor;
        Opacity = Math.Clamp(Opacity, 20, 100);
        Width = Width == 0 ? defaults.Width : Math.Clamp(Width, WidthRange.Min, WidthRange.Max);
        CornerRadius = Math.Clamp(CornerRadius, RadiusRange.Min, RadiusRange.Max);
        FontSize = FontSize == 0 ? defaults.FontSize : Math.Clamp(FontSize, FontSizeRange.Min, FontSizeRange.Max);
        if (string.IsNullOrWhiteSpace(Font)) Font = DefaultFont;
    }
}

/// <summary>The widget in the taskbar (defaults: the mock-up of October 21, 2026).</summary>
public sealed class WidgetStyle : SurfaceStyle
{
    public const int Height = 40;

    public WidgetStyle()
    {
        Width = 280;
        CornerRadius = 6;
        FontSize = 12;
    }

    public WidgetPosition Position { get; set; } = WidgetPosition.Left;

    /// <summary>Space added from the edge (Left) or from the clock (NearClock), in pixels at 100 %.</summary>
    public int Offset { get; set; }

    /// <summary>Elapsed time and length around the progress bar, on hover.</summary>
    public bool ShowTimes { get; set; } = true;

    /// <summary>Previous, play / pause, next, on hover.</summary>
    public bool ShowControls { get; set; } = true;

    public IdleMode Idle { get; set; } = IdleMode.Hide;

    /// <summary>The floating widget only: the "Glass" look (blurred) rather than "Transparent", both of the Glass color mode.</summary>
    public bool Blur { get; set; }

    /// <summary>
    /// The floating widget only: how much of Windows' blur shows behind it, 0 (sharp) to 100 % (Windows sets the
    /// strength; less mixes the sharp background in). -1: a settings file of 0.2.1, set from <see cref="Blur"/> by
    /// <see cref="Normalized"/>.
    /// </summary>
    public int BlurStrength { get; set; } = -1;

    /// <summary>A click on the cover brings the player to the front (asked by the author, October 24, 2026; off by default).</summary>
    public bool CoverOpensPlayer { get; set; }

    /// <summary>The taskbar widget only: on the taskbar of every screen, not only the main one.</summary>
    public bool AllScreens { get; set; }

    /// <summary>The floating widget only: drawn over a blurred background.</summary>
    [JsonIgnore]
    public bool Frosted => BlurStrength > 0;

    /// <summary>The floating widget only: a shadow under it, <see cref="ShadowOpacity"/> percent dark.</summary>
    public bool Shadow { get; set; }

    public int ShadowOpacity { get; set; } = 40;

    public override (int Min, int Max) WidthRange => (200, 420);
    public override (int Min, int Max) RadiusRange => (0, 20);
    public override (int Min, int Max) FontSizeRange => (10, 15);

    /// <summary>The floating widget: over any window, so frosted dark glass by default (the blur behind, a light veil).</summary>
    public static WidgetStyle DefaultFloating()
    {
        var style = new WidgetStyle { Width = 300, CornerRadius = 10, Blur = true };
        style.ApplyFloatingLook();
        return style;
    }

    /// <summary>
    /// The opacity and the shadow each look of the floating widget starts from (the author, October 22, 2026: they mean
    /// different things in each look, and come back to these values when he chooses another one): a light veil over
    /// the blur; a darker one without it, and no shadow (a dark halo around a see-through widget); a nearly solid
    /// color.
    /// </summary>
    public static (int Opacity, bool Shadow, int ShadowOpacity, int BlurStrength) FloatingLook(ColorMode mode, bool blur) => mode switch
    {
        ColorMode.Glass when blur => (25, true, 40, 100),
        ColorMode.Glass => (55, false, 40, 0),
        _ => (90, true, 40, 0),
    };

    /// <summary>Gives the opacity, the shadow and the blur of the look chosen (<see cref="SurfaceStyle.ColorMode"/>, <see cref="Blur"/>).</summary>
    public void ApplyFloatingLook() => (Opacity, Shadow, ShadowOpacity, BlurStrength) = FloatingLook(ColorMode, Blur);

    public WidgetStyle Normalized()
    {
        NormalizeCommon(new WidgetStyle());
        if (!Enum.IsDefined(Position)) Position = WidgetPosition.Left;
        if (!Enum.IsDefined(Idle)) Idle = IdleMode.Hide;
        Offset = Math.Clamp(Offset, 0, 600);
        ShadowOpacity = Math.Clamp(ShadowOpacity, 10, 100);
        BlurStrength = BlurStrength < 0 ? (ColorMode == ColorMode.Glass && Blur ? 100 : 0) : Math.Clamp(BlurStrength, 0, 100);
        return this;
    }
}

/// <summary>One overlay layout for OBS; each has its own style (asked by the author).</summary>
public sealed class OverlayStyle : SurfaceStyle
{
    public bool ShowProgress { get; set; } = true;

    /// <summary>Shown all the time, or only a few seconds at each new track.</summary>
    public OverlayAppearance Appearance { get; set; } = OverlayAppearance.Always;

    /// <summary>How long it stays at each new track, in seconds (<see cref="OverlayAppearance.OnNewTrack"/>).</summary>
    public int AppearSeconds { get; set; } = DefaultAppearSeconds;

    /// <summary>Shown again when the playback starts again after a pause, too.</summary>
    public bool AppearOnResume { get; set; }

    public const int DefaultAppearSeconds = 8;
    public static readonly (int Min, int Max) AppearSecondsRange = (3, 30);
    public bool ShowTimes { get; set; } = true;

    public override (int Min, int Max) WidthRange => (320, 800);
    public override (int Min, int Max) RadiusRange => (0, 40);
    public override (int Min, int Max) FontSizeRange => (12, 28);

    public static OverlayStyle DefaultBar() => new()
    {
        ColorMode = ColorMode.Glass, Opacity = 70, Width = 480, CornerRadius = 14, FontSize = 16,
        ShowVisualizer = true, ShowProgress = false, ShowTimes = false,
    };

    public static OverlayStyle DefaultCard() => new()
    {
        ColorMode = ColorMode.Cover, Opacity = 80, Width = 470, CornerRadius = 16, FontSize = 16,
        ShowVisualizer = false, ShowProgress = true, ShowTimes = true,
    };

    public static OverlayStyle Default(OverlayLayout layout) => layout == OverlayLayout.Bar ? DefaultBar() : DefaultCard();

    public OverlayStyle Normalized(OverlayLayout layout)
    {
        NormalizeCommon(Default(layout));
        if (!Enum.IsDefined(Appearance)) Appearance = OverlayAppearance.Always;
        AppearSeconds = AppearSeconds == 0 ? DefaultAppearSeconds : Math.Clamp(AppearSeconds, AppearSecondsRange.Min, AppearSecondsRange.Max);
        return this;
    }

    /// <summary>The size of the Browser Source to create in OBS for this layout (a little room around).</summary>
    public (int Width, int Height) SourceSize(OverlayLayout layout) =>
        (Width + 20, layout == OverlayLayout.Bar ? 96 : 170);
}
