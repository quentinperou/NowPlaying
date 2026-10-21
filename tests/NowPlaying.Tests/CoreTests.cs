using System.Text.Json;
using NowPlaying.Core;

namespace NowPlaying.Tests;

public class RgbTests
{
    [Theory]
    [InlineData("#5B6CF0", 0x5B, 0x6C, 0xF0)]
    [InlineData("5b6cf0", 0x5B, 0x6C, 0xF0)]
    [InlineData(" #000000 ", 0, 0, 0)]
    public void Parse_ReadsHex(string text, int r, int g, int b)
    {
        Assert.True(Rgb.TryParse(text, out var color));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Parse_RefusesOtherText(string? text) => Assert.False(Rgb.TryParse(text, out _));

    [Fact]
    public void Hsl_RoundTrips()
    {
        foreach (var color in new[] { new Rgb(200, 30, 90), new Rgb(10, 120, 60), new Rgb(128, 128, 128), Rgb.White })
        {
            var (h, s, l) = color.ToHsl();
            var back = Rgb.FromHsl(h, s, l);
            Assert.InRange(Math.Abs(back.R - color.R) + Math.Abs(back.G - color.G) + Math.Abs(back.B - color.B), 0, 3);
        }
    }

    [Fact]
    public void IsLight_PicksTheReadableText()
    {
        Assert.True(Rgb.White.IsLight);
        Assert.True(new Rgb(245, 165, 36).IsLight);
        Assert.False(Rgb.Black.IsLight);
        Assert.False(new Rgb(53, 58, 146).IsLight);
    }
}

public class DominantColorTests
{
    private static byte[] Picture(int width, int height, Func<int, int, Rgb> color)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = color(x, y);
                int i = (y * width + x) * 4;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255;
            }
        return pixels;
    }

    [Fact]
    public void TheMostPresentVividHueWins()
    {
        // Two thirds red, a third blue, with a dark band: red, not a brown average.
        var pixels = Picture(90, 90, (x, y) => y > 80 ? new Rgb(10, 10, 10) : x < 60 ? new Rgb(220, 40, 40) : new Rgb(40, 60, 220));
        var color = DominantColor.FromBgra(pixels, 90, 90, 90 * 4)!.Value;
        var (h, _, _) = color.ToHsl();
        Assert.True(h < 15 || h > 345, $"Hue {h}");
    }

    [Fact]
    public void AGreyPictureGivesAGrey()
    {
        var pixels = Picture(40, 40, (x, _) => new Rgb((byte)(x * 6), (byte)(x * 6), (byte)(x * 6)));
        var (_, s, _) = DominantColor.FromBgra(pixels, 40, 40, 40 * 4)!.Value.ToHsl();
        Assert.True(s < 0.05);
    }

    [Fact]
    public void TheResultKeepsWhiteTextReadable()
    {
        var pixels = Picture(20, 20, (_, _) => new Rgb(255, 230, 120));
        var color = DominantColor.FromBgra(pixels, 20, 20, 20 * 4)!.Value;
        Assert.False(color.IsLight);
    }

    [Fact]
    public void AnEmptyPictureGivesNothing() => Assert.Null(DominantColor.FromBgra([], 0, 0, 0));
}

public class TimelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 14, 32, 0, TimeSpan.Zero);

    [Fact]
    public void WhilePlaying_GoesOnFromTheLastReport()
    {
        var at = Timeline.Extrapolate(TimeSpan.FromSeconds(40), Now.AddSeconds(-4), Now, playing: true, 1, TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(44), at);
    }

    [Fact]
    public void WhilePaused_StaysWhereItWas()
    {
        var at = Timeline.Extrapolate(TimeSpan.FromSeconds(40), Now.AddSeconds(-4), Now, playing: false, 1, TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(40), at);
    }

    [Fact]
    public void NeverGoesPastTheEnd() =>
        Assert.Equal(TimeSpan.FromMinutes(3),
            Timeline.Extrapolate(TimeSpan.FromSeconds(170), Now.AddSeconds(-60), Now, true, 1, TimeSpan.FromMinutes(3)));

    [Fact]
    public void AReportTimeNeverFilledIn_IsNotUsed() =>
        Assert.Equal(TimeSpan.FromSeconds(10),
            Timeline.Extrapolate(TimeSpan.FromSeconds(10), default, Now, true, 1, TimeSpan.FromMinutes(3)));

    [Theory]
    [InlineData(44, "0:44")]
    [InlineData(214, "3:34")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "0:00")]
    public void Format_ReadsLikeAPlayer(int seconds, string expected) =>
        Assert.Equal(expected, Timeline.Format(TimeSpan.FromSeconds(seconds)));

    // A live stream (YouTube, Twitch): no length, or a negative one from a player whose end is before its start.
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void ALiveStream_GoesOnWithoutLimitNorProgress(int lengthSeconds)
    {
        var length = TimeSpan.FromSeconds(lengthSeconds);
        var at = Timeline.Extrapolate(TimeSpan.FromHours(5), Now.AddSeconds(-4), Now, true, 1, length);
        Assert.Equal(TimeSpan.FromHours(5) + TimeSpan.FromSeconds(4), at);
        Assert.Equal(0, Timeline.Fraction(at, length));
        Assert.Equal("0:00", Timeline.Format(length));
        Assert.Equal(ProgressKind.Live, Timeline.Kind(at, length));
    }

    [Fact]
    public void Kind_ShowsLiveOnlyWhenThePositionGoesOn()
    {
        Assert.Equal(ProgressKind.Track, Timeline.Kind(TimeSpan.FromSeconds(44), TimeSpan.FromMinutes(3)));
        Assert.Equal(ProgressKind.Track, Timeline.Kind(TimeSpan.Zero, TimeSpan.FromMinutes(3)));
        // A player that tells neither its length nor its position: nothing, not "live".
        Assert.Equal(ProgressKind.None, Timeline.Kind(TimeSpan.Zero, TimeSpan.Zero));
        // Firefox on some sites: no timeline at all while a video plays. The estimate goes on from 0, the reported
        // position stays 0: still nothing.
        var reportedAt = DateTimeOffset.Now.AddSeconds(-30);
        Assert.True(Timeline.Extrapolate(TimeSpan.Zero, reportedAt, DateTimeOffset.Now, true, 1, TimeSpan.Zero) > TimeSpan.Zero);
        Assert.Equal(ProgressKind.None, Timeline.Kind(TimeSpan.Zero, TimeSpan.Zero));
    }
}

public class SourcePickerTests
{
    [Fact]
    public void TheLastPlayerStarted_IsShown()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", true);
        picker.Update("edge", true);
        Assert.Equal("edge", picker.Current);
    }

    [Fact]
    public void WhenTheShownPlayerPauses_OneStillPlayingTakesOver()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", true);
        picker.Update("edge", true);
        picker.Update("edge", false);
        Assert.Equal("spotify", picker.Current);
    }

    [Fact]
    public void WhenEverythingPauses_TheShownPlayerStays()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", true);
        picker.Update("edge", true);
        picker.Update("spotify", false);
        picker.Update("edge", false);
        Assert.Equal("edge", picker.Current);
    }

    [Fact]
    public void APlayerChosenByHand_StaysUntilAnotherOneStarts()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", true);
        picker.Update("edge", true);
        Assert.True(picker.Pin("spotify"));
        Assert.Equal("spotify", picker.Current);
        picker.Update("edge", true); // still playing: no new start
        Assert.Equal("spotify", picker.Current);
        picker.Update("vlc", true);
        Assert.Equal("vlc", picker.Current);
    }

    [Fact]
    public void APlayerThatGoesAway_GivesWayToAnother()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", false);
        picker.Update("edge", true);
        Assert.True(picker.Remove("edge"));
        Assert.Equal("spotify", picker.Current);
        picker.Remove("spotify");
        Assert.Null(picker.Current);
    }

    [Fact]
    public void AnUnknownPlayerCannotBePinned() => Assert.False(new SourcePicker().Pin("nothing"));

    private static SourcePolicy Priority(bool preferPlaying = true, params string[] order) =>
        SourcePolicy.Default with { Mode = SourceMode.Priority, PreferPlaying = preferPlaying, Order = order };

    [Fact]
    public void InPriorityMode_TheFirstOfTheListThatPlaysIsShown()
    {
        var picker = new SourcePicker();
        picker.SetPolicy(Priority(true, "spotify", "chrome"));
        picker.Update("chrome", true);
        picker.Update("vlc", true);
        Assert.Equal("chrome", picker.Current); // vlc is not in the list: after it
        picker.Update("spotify", true);
        Assert.Equal("spotify", picker.Current);
        picker.Update("spotify", false);
        Assert.Equal("chrome", picker.Current); // what plays goes first
    }

    [Fact]
    public void InPriorityMode_WithoutPreferPlaying_TheFirstOfTheListStaysEvenPaused()
    {
        var picker = new SourcePicker();
        picker.SetPolicy(Priority(false, "spotify", "chrome"));
        picker.Update("spotify", false);
        picker.Update("chrome", true);
        Assert.Equal("spotify", picker.Current);
    }

    [Fact]
    public void InPriorityMode_SecondSessionsOfAnAppShareItsRank()
    {
        var picker = new SourcePicker();
        picker.SetPolicy(Priority(true, "chrome", "spotify"));
        picker.Update("spotify", true);
        picker.Update("chrome#2", true);
        Assert.Equal("chrome#2", picker.Current);
    }

    [Fact]
    public void AHiddenPlayer_IsNeverShownNorPinned()
    {
        var picker = new SourcePicker();
        picker.SetPolicy(SourcePolicy.Default with { Hidden = ["chrome"] });
        picker.Update("spotify", true);
        picker.Update("chrome#2", true);
        Assert.Equal("spotify", picker.Current);
        Assert.False(picker.Pin("chrome#2"));
        picker.Remove("spotify");
        Assert.Null(picker.Current);
    }

    [Fact]
    public void HidingTheShownPlayer_GivesWayAtOnce()
    {
        var picker = new SourcePicker();
        picker.Update("spotify", true);
        picker.Update("chrome", true);
        picker.Pin("chrome"); // already shown: chosen by hand as well
        Assert.True(picker.SetPolicy(SourcePolicy.Default with { Hidden = ["chrome"] }));
        Assert.Equal("spotify", picker.Current);
    }

    [Fact]
    public void AChoiceThatStaysUntilChanged_SurvivesAnotherStart()
    {
        var picker = new SourcePicker();
        picker.SetPolicy(SourcePolicy.Default with { Pin = PinMode.UntilChanged });
        picker.Update("spotify", true);
        picker.Update("chrome", true);
        picker.Pin("spotify");
        picker.Update("vlc", true);
        Assert.Equal("spotify", picker.Current);
        picker.Remove("spotify"); // gone: the rules apply again
        Assert.Equal("vlc", picker.Current);
    }

    [Fact]
    public void AppOf_DropsTheNumberOfASecondSession()
    {
        Assert.Equal("chrome", SourcePicker.AppOf("chrome#2"));
        Assert.Equal("Spotify.exe", SourcePicker.AppOf("Spotify.exe"));
    }
}

public class AppIdentityTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Microsoft Edge")]
    [InlineData("Chrome", "Google Chrome")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("C122DBAFB30CA16", "Firefox")] // 15 digits, seen on the shop PC
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("Contoso.SuperPlayer_abc123!App", "SuperPlayer")]
    [InlineData(@"C:\Tools\winamp.exe", "Winamp")]
    public void FriendlyName_ReadsLikeTheApp(string id, string expected) => Assert.Equal(expected, AppIdentity.FriendlyName(id));

    [Theory]
    [InlineData("Spotify.exe", "Spotify", null, true)]
    [InlineData("MSEdge", "msedge", null, true)]
    [InlineData("Chrome", "chrome.exe", null, true)]
    [InlineData("308046B0AF4A39CB", "firefox", null, true)]
    [InlineData("C122DBAFB30CA16", "firefox", null, true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", true)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify", null, false)]
    [InlineData("Spotify.exe", "chrome", null, false)]
    [InlineData("MSEdge", "firefox", null, false)]
    public void Matches_TheProcessOfThePlayer(string id, string process, string? family, bool expected) =>
        Assert.Equal(expected, AppIdentity.Matches(id, process, family));
}

public class SpectrumTests
{
    [Fact]
    public void ASineLightsTheBarsOfItsFrequency()
    {
        var analyzer = new SpectrumAnalyzer(bars: 24, fftSize: 2048, sampleRate: 48000);
        var samples = new float[4096];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48000));
        analyzer.AddSamples(samples);
        var levels = new float[24];
        for (int frame = 0; frame < 5; frame++)
            analyzer.Compute(levels);

        int loudest = Array.IndexOf(levels, levels.Max());
        // 1 kHz on a log scale from 50 Hz to 16 kHz: about 52 % of the way.
        Assert.InRange(loudest, 11, 14);
        Assert.True(levels[loudest] > 0.6f);
        Assert.True(levels[0] < 0.3f && levels[23] < 0.3f);
    }

    [Fact]
    public void SilenceGivesFlatBars()
    {
        var analyzer = new SpectrumAnalyzer(bars: 10);
        analyzer.AddSamples(new float[4096]);
        var levels = new float[10];
        analyzer.Compute(levels);
        Assert.All(levels, l => Assert.Equal(0f, l));
    }

    [Fact]
    public void TheDecorativeBarsStayInRange()
    {
        var levels = new float[16];
        for (double t = 0; t < 20; t += 0.37)
        {
            DecorativeBars.Fill(levels, t);
            Assert.All(levels, l => Assert.InRange(l, 0.05f, 1f));
        }
    }
}

public class StyleTests
{
    [Fact]
    public void WidgetDefaults_AreThoseOfTheMockUp()
    {
        var style = new WidgetStyle();
        Assert.Equal((280, 6, 12), (style.Width, style.CornerRadius, style.FontSize));
        Assert.Equal(WidgetPosition.Left, style.Position);
        Assert.Equal(ColorMode.Glass, style.ColorMode);
        Assert.Equal(IdleMode.Hide, style.Idle);
    }

    [Fact]
    public void Normalized_PutsValuesBackInRange()
    {
        var style = new WidgetStyle { Width = 5000, CornerRadius = -3, FontSize = 0, Opacity = 3, CustomColor = "red", Offset = -10 }.Normalized();
        Assert.Equal(420, style.Width);
        Assert.Equal(0, style.CornerRadius);
        Assert.Equal(12, style.FontSize);
        Assert.Equal(20, style.Opacity);
        Assert.Equal("#5B6CF0", style.CustomColor);
        Assert.Equal(0, style.Offset);
    }

    [Fact]
    public void Styles_RoundTripThroughJson_WithReadableEnums()
    {
        var style = new WidgetStyle { ColorMode = ColorMode.Cover, Position = WidgetPosition.NearClock, Visualizer = VisualizerMode.Animation };
        string json = JsonSerializer.Serialize(style);
        Assert.Contains("\"NearClock\"", json);
        var back = JsonSerializer.Deserialize<WidgetStyle>(json)!;
        Assert.Equal((ColorMode.Cover, WidgetPosition.NearClock, VisualizerMode.Animation), (back.ColorMode, back.Position, back.Visualizer));
    }

    [Fact]
    public void OverlayDefaults_DifferByLayout()
    {
        Assert.Equal(ColorMode.Glass, OverlayStyle.DefaultBar().ColorMode);
        Assert.Equal(ColorMode.Cover, OverlayStyle.DefaultCard().ColorMode);
        Assert.Equal((500, 96), OverlayStyle.DefaultBar().SourceSize(OverlayLayout.Bar));
    }

    [Fact]
    public void Colors_KeepTextReadable()
    {
        var light = SurfaceColors.Resolve(new WidgetStyle { ColorMode = ColorMode.Custom, CustomColor = "#F2F2F2", Opacity = 100 }, null, true, false);
        Assert.False(light.Text.Color.IsLight);
        var dark = SurfaceColors.Resolve(new WidgetStyle { ColorMode = ColorMode.Cover, Opacity = 100 }, new Rgb(53, 58, 146), false, false);
        Assert.True(dark.Text.Color.IsLight);
        var glassOnLightTaskbar = SurfaceColors.Resolve(new WidgetStyle(), null, onDarkSurface: false, overlay: false);
        Assert.False(glassOnLightTaskbar.Text.Color.IsLight);
        Assert.True(SurfaceColors.Resolve(OverlayStyle.DefaultBar(), null, true, true).Frosted);
    }
}
