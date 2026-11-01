using NowPlaying.Core;

namespace NowPlaying.Tests;

/// <summary>The widget on a 1920 px taskbar at 100 %, in the layouts of issue #1 and the usual ones.</summary>
public class TaskbarPlacementTests
{
    private static readonly TaskbarRect Bar = new(0, 1032, 1920, 48);
    private static readonly TaskbarRect SmallBar = new(0, 1049, 1920, 31);

    private static WidgetStyle Style(WidgetPosition position = WidgetPosition.Left, int offset = 0) =>
        new() { Width = 280, Position = position, Offset = offset };

    [Fact]
    public void Left_GoesAfterTheWeather()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartLeft: 760, NotifyLeft: 1620));
        Assert.Equal(168, place.X);
        Assert.Equal(1036, place.Y);
        Assert.Equal(WidgetStyle.Height, place.Height);
    }

    [Fact]
    public void Left_WithoutTheWeather_StartsAtTheEdge()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(offset: 12), new TaskbarButtons(StartLeft: 760, NotifyLeft: 1620));
        Assert.Equal(20, place.X);
    }

    [Fact]
    public void IconsAlignedLeft_GoesBeforeTheWeatherNextToTheClock()
    {
        // Issue #1: Start at the very edge, the weather moved next to the notification area.
        var buttons = new TaskbarButtons(Weather: (1460, 1560), StartLeft: 0, NotifyLeft: 1620, IconsAlignedLeft: true);
        var place = TaskbarPlacement.Place(Bar, 1, Style(), buttons);
        Assert.Equal(1460 - 280 - 8, place.X);
    }

    [Fact]
    public void StartAtTheEdge_IsSeenEvenWithoutTheSetting()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(StartLeft: 0, NotifyLeft: 1620));
        Assert.Equal(1620 - 280 - 8, place.X);
    }

    [Fact]
    public void NoRoomBeforeTheIcons_GoesNextToTheClock()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartLeft: 400, NotifyLeft: 1620));
        Assert.Equal(1620 - 280 - 8, place.X);
    }

    [Fact]
    public void NearClock_KeepsTheWeatherOnTheLeftAlone()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(WidgetPosition.NearClock, offset: 10),
            new TaskbarButtons(Weather: (0, 160), StartLeft: 760, NotifyLeft: 1620));
        Assert.Equal(1620 - 280 - 18, place.X);
    }

    [Fact]
    public void SecondaryTaskbar_GoesLeftOfItsClock()
    {
        var place = TaskbarPlacement.Place(Bar, 1, Style(WidgetPosition.NearClock), new TaskbarButtons(StartLeft: 760, ClockLeft: 1800));
        Assert.Equal(1800 - 280 - 8, place.X);
    }

    [Fact]
    public void SmallTaskbar_FitsInside()
    {
        // Issue #1: Windows' small taskbar is 31 px high, the widget 40.
        var place = TaskbarPlacement.Place(SmallBar, 1, Style(), new TaskbarButtons(Weather: (0, 160), StartLeft: 760, NotifyLeft: 1620));
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
        var place = TaskbarPlacement.Place(bar, scale, Style(), new TaskbarButtons(StartLeft: 1000));
        Assert.Equal(expected, place.Height);
        Assert.InRange(place.Y - bar.Top, 2, 6);
    }
}
