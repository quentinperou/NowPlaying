using NowPlaying.Core;

namespace NowPlaying.Tests;

/// <summary>The widget on a 1920 × 1080 screen at 100 %, in the layouts of issue #1, the usual ones and a vertical taskbar.</summary>
public class TaskbarPlacementTests
{
    private static readonly TaskbarRect Bar = new(0, 1032, 1920, 48);
    private static readonly TaskbarRect SmallBar = new(0, 1049, 1920, 31);
    private static readonly TaskbarRect LeftBar = new(0, 0, 48, 1080);
    private static readonly TaskbarRect RightBar = new(1872, 0, 48, 1080);

    private static WidgetStyle Style(WidgetPosition position = WidgetPosition.Left, int offset = 0) =>
        new() { Width = 280, Position = position, Offset = offset };

    [Fact]
    public void Left_GoesAfterTheWeather()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartAt: 760, NotifyAt: 1620));
        Assert.Equal(168, place.X);
        Assert.Equal(1036, place.Y);
        Assert.Equal(WidgetStyle.Height, place.Height);
    }

    [Fact]
    public void Left_WithoutTheWeather_StartsAtTheEdge()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(offset: 12), new TaskbarButtons(StartAt: 760, NotifyAt: 1620));
        Assert.Equal(20, place.X);
    }

    [Fact]
    public void IconsAlignedLeft_GoesBeforeTheWeatherNextToTheClock()
    {
        // Issue #1: Start at the very edge, the weather moved next to the notification area.
        var buttons = new TaskbarButtons(Weather: (1460, 1560), StartAt: 0, NotifyAt: 1620, IconsAlignedLeft: true);
        var place = TaskbarPlacement.Place(Bar, 1, Style(), buttons);
        Assert.Equal(1460 - 280 - 8, place.X);
    }

    [Fact]
    public void StartAtTheEdge_IsSeenEvenWithoutTheSetting()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(StartAt: 0, NotifyAt: 1620));
        Assert.Equal(1620 - 280 - 8, place.X);
    }

    [Fact]
    public void NoRoomBeforeTheIcons_GoesNextToTheClock()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartAt: 400, NotifyAt: 1620));
        Assert.Equal(1620 - 280 - 8, place.X);
    }

    [Fact]
    public void NearClock_KeepsTheWeatherOnTheLeftAlone()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(WidgetPosition.NearClock, offset: 10),
            new TaskbarButtons(Weather: (0, 160), StartAt: 760, NotifyAt: 1620));
        Assert.Equal(1620 - 280 - 18, place.X);
    }

    [Fact]
    public void SecondaryTaskbar_GoesLeftOfItsClock()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(WidgetPosition.NearClock), new TaskbarButtons(StartAt: 760, ClockAt: 1800));
        Assert.Equal(1800 - 280 - 8, place.X);
    }

    [Fact]
    public void SmallTaskbar_FitsInside()
    {
        // Issue #1: Windows' small taskbar is 31 px high, the widget 40.
        var place = TaskbarPlacement.Place(SmallBar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartAt: 760, NotifyAt: 1620));
        Assert.Equal(27, place.Height);
        Assert.Equal(SmallBar.Top + TaskbarPlacement.Inset, place.Y);
    }

    [Theory]
    [InlineData(1.25, 39, 27)]
    [InlineData(1.5, 72, 40)]
    [InlineData(1.25, 60, 40)]
    public void Height_FollowsTheScale(double scale, int barHeight, double expected)
    {
        var bar = new TaskbarRect(0, 1000, 2400, barHeight);
        var place = TaskbarPlacement.Place(bar, scale, Style(), new TaskbarButtons(StartAt: 1000));
        Assert.Equal(expected, place.Height);
        Assert.InRange(place.Y - bar.Top, 2, 6);
    }

    [Fact]
    public void Vertical_Left_IsTheCoverUnderTheWeather()
    {
        var place = TaskbarPlacement.Place(LeftBar, 1, Style(), new TaskbarButtons(Weather: (0, 60), StartAt: 400, NotifyAt: 900));
        Assert.Equal(40, place.Height);
        Assert.Equal(4, place.X); // centered across the 48 px of the taskbar
        Assert.Equal(68, place.Y);
    }

    [Fact]
    public void Vertical_NearClock_IsTheCoverAboveTheClock()
    {
        var place = TaskbarPlacement.Place(RightBar, 1, Style(WidgetPosition.NearClock), new TaskbarButtons(StartAt: 400, NotifyAt: 900));
        Assert.Equal(1872 + 4, place.X);
        Assert.Equal(900 - 40 - 8, place.Y);
    }

    [Fact]
    public void Vertical_IconsAtTheTop_GoesAboveTheClock()
    {
        var place = TaskbarPlacement.Place(LeftBar, 1, Style(), new TaskbarButtons(StartAt: 0, NotifyAt: 900, IconsAlignedLeft: true));
        Assert.Equal(900 - 40 - 8, place.Y);
    }

    [Fact]
    public void Vertical_Unfolds_OverTheScreen()
    {
        Assert.False(TaskbarPlacement.OnTheRight(LeftBar, 960));
        Assert.True(TaskbarPlacement.OnTheRight(RightBar, 960));
        // From the cover at 4 (40 px): to the right on the left, to the left on the right, the cover where it was.
        Assert.Equal(4, TaskbarPlacement.UnfoldedLeft(false, 4, 40, 280));
        Assert.Equal(1876 + 40 - 280, TaskbarPlacement.UnfoldedLeft(true, 1876, 40, 280));
    }
}

public class UnfoldedColorsTests
{
    [Fact]
    public void Glass_IsOpaqueAsTheMenusOfWindows()
    {
        var style = new WidgetStyle { ColorMode = ColorMode.Glass };
        var light = SurfaceColors.Unfolded(style, null, onDarkTaskbar: false);
        var dark = SurfaceColors.Unfolded(style, null, onDarkTaskbar: true);
        Assert.True(light.Background.A >= 235 && light.Background.Color.IsLight && !light.Text.Color.IsLight);
        Assert.True(dark.Background.A >= 235 && !dark.Background.Color.IsLight && dark.Text.Color.IsLight);
    }

    [Fact]
    public void SeeThroughColors_BecomeNearlyOpaque()
    {
        var style = new WidgetStyle { ColorMode = ColorMode.Cover, Opacity = 30 };
        var colors = SurfaceColors.Unfolded(style, new Rgb(20, 40, 120), onDarkTaskbar: false);
        Assert.True(colors.Background.A >= 235);
        Assert.Equal(new Rgb(20, 40, 120), colors.Background.Color);
        Assert.True(colors.Text.Color.IsLight);
    }
}
