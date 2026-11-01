namespace NowPlaying.Core;

/// <summary>A taskbar, in pixels of the screen: horizontal (at the bottom or the top), or vertical (left or right).</summary>
public readonly record struct TaskbarRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    /// <summary>On the left or the right of the screen (Windows 11 offers it since 2026).</summary>
    public bool Vertical => Height > Width;
}

/// <summary>
/// What the taskbar holds, as positions along it (x on a horizontal taskbar, y on a vertical one; null: not there, or not
/// found): the weather of Windows (its Widgets button), the Start button, the notification area (the main taskbar) or
/// the clock (the taskbar of another screen), and whether its icons are aligned to its start (left, or top).
/// </summary>
public readonly record struct TaskbarButtons(
    (int Start, int End)? Weather = null,
    int? StartAt = null,
    int? NotifyAt = null,
    int? ClockAt = null,
    bool IconsAlignedLeft = false);

/// <summary>Where the widget goes on the taskbar, and its height there.</summary>
/// <param name="Height">
/// In pixels at 100 %: <see cref="WidgetStyle.Height"/>, less in Windows' small taskbar. On a vertical taskbar the widget
/// is the cover alone, a square of that side, which unfolds beside the taskbar on hover.
/// </param>
public readonly record struct TaskbarPlace(int X, int Y, double Height);

/// <summary>
/// Places the widget on a taskbar: after the weather of Windows ("Left": at the top of a vertical taskbar), or before
/// the notification area ("Near the clock": at the bottom of a vertical one), never over the icons. Worked out along
/// the taskbar, so the same for both directions; pure, so that every layout of the taskbar can be tested.
/// </summary>
public static class TaskbarPlacement
{
    /// <summary>Space kept on both sides of the widget in a taskbar thinner than it (Windows' small taskbar).</summary>
    public const int Inset = 2;

    public static TaskbarPlace Place(TaskbarRect bar, double scale, WidgetStyle style, TaskbarButtons buttons)
    {
        int Px(double value) => (int)Math.Round(value * scale);

        bool vertical = bar.Vertical;
        int start = vertical ? bar.Top : bar.Left, length = vertical ? bar.Height : bar.Width;
        int end = start + length, thickness = vertical ? bar.Width : bar.Height;

        // Windows' small taskbar (31 px at 100 %) is thinner than the widget: it fits inside then (issue #1).
        double height = Math.Min(WidgetStyle.Height, Math.Round(thickness / scale) - 2 * Inset);
        // Along the taskbar: the widget, or on a vertical taskbar the cover alone (a square).
        int size = vertical ? Px(height) : Px(style.Width);
        int across = (vertical ? bar.Left : bar.Top) + (thickness - Px(height)) / 2;
        int along = 0;

        bool nearClock = style.Position == WidgetPosition.NearClock;
        if (!nearClock)
        {
            // After the weather of Windows (its Widgets button) when it is there, on by default on Windows 11.
            int after = buttons.Weather is { } weather && weather.End > start && weather.End < start + length / 2 ? weather.End : start;
            along = after + Px(8 + style.Offset);
            // Icons aligned to the start (Start first, at the very edge: issue #1), or no room before them: next to the
            // clock rather than over them.
            if (buttons.IconsAlignedLeft || buttons.StartAt is int startButton && startButton >= start && along + size > startButton - Px(4))
                nearClock = true;
        }
        if (nearClock)
        {
            // Before the notification area; the taskbar of another screen has none: before its clock. Else about where
            // it is.
            int before = buttons.NotifyAt is int notify && notify > start
                ? notify
                : buttons.ClockAt is int clock && clock > start + length / 2 && clock < end
                    ? clock
                    : end - Px(220);
            // Icons aligned to the start put the weather next to the clock: before the weather then.
            if (buttons.Weather is { } weather && weather.Start > start + length / 2 && weather.Start < before)
                before = weather.Start;
            along = before - size - Px(8 + (style.Position == WidgetPosition.NearClock ? style.Offset : 0));
        }
        along = Math.Max(start, along);
        return vertical ? new TaskbarPlace(across, along, height) : new TaskbarPlace(along, across, height);
    }

    /// <summary>Whether a vertical taskbar is on the right of its screen: the widget then unfolds to the left.</summary>
    public static bool OnTheRight(TaskbarRect bar, int screenCenterX) => bar.Left + bar.Width / 2 >= screenCenterX;

    /// <summary>
    /// The left of the widget unfolded beside a vertical taskbar (<paramref name="width"/> pixels wide, from its cover at
    /// <paramref name="coverLeft"/>): over the screen, to the right of a taskbar on the left, to the left of one on the
    /// right, its cover staying where it was.
    /// </summary>
    public static int UnfoldedLeft(bool onTheRight, int coverLeft, int coverSize, int width) =>
        onTheRight ? coverLeft + coverSize - width : coverLeft;
}
