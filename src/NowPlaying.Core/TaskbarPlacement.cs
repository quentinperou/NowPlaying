namespace NowPlaying.Core;

/// <summary>A horizontal taskbar, in pixels of the screen.</summary>
public readonly record struct TaskbarRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
}

/// <summary>
/// What the taskbar holds, in pixels (null: not there, or not found): the weather of Windows (its Widgets button), the
/// Start button, the notification area (the main taskbar) or the clock (the taskbar of another screen), and whether
/// its icons are aligned left.
/// </summary>
public readonly record struct TaskbarButtons(
    (int Left, int Right)? Weather = null,
    int? StartLeft = null,
    int? NotifyLeft = null,
    int? ClockLeft = null,
    bool IconsAlignedLeft = false);

/// <summary>Where the widget goes on the taskbar, and its height there.</summary>
/// <param name="Height">In pixels at 100 %: <see cref="WidgetStyle.Height"/>, less in Windows' small taskbar.</param>
public readonly record struct TaskbarPlace(int X, int Y, double Height);

/// <summary>
/// Places the widget on a horizontal taskbar: after the weather of Windows (Left), or left of the notification area
/// (Near the clock), never over the icons. Pure, so that every layout of the taskbar can be tested.
/// </summary>
public static class TaskbarPlacement
{
    /// <summary>Space kept above and below the widget in a taskbar lower than it (Windows' small taskbar).</summary>
    public const int Inset = 2;

    public static TaskbarPlace Place(TaskbarRect bar, double scale, WidgetStyle style, TaskbarButtons buttons)
    {
        int Px(double value) => (int)Math.Round(value * scale);

        // Windows' small taskbar (31 px at 100 %) is lower than the widget: it fits inside then (issue #1).
        double height = Math.Min(WidgetStyle.Height, Math.Round(bar.Height / scale) - 2 * Inset);
        int width = Px(style.Width);
        int y = bar.Top + (bar.Height - Px(height)) / 2;
        int x = 0;

        bool nearClock = style.Position == WidgetPosition.NearClock;
        if (!nearClock)
        {
            // After the weather of Windows (its Widgets button) when it is there, on by default on Windows 11.
            int left = buttons.Weather is { } weather && weather.Right > bar.Left && weather.Right < bar.Left + bar.Width / 2
                ? weather.Right
                : bar.Left;
            x = left + Px(8 + style.Offset);
            // Icons aligned left (Start first, at the very edge: issue #1), or no room before them: next to the clock
            // rather than over them.
            if (buttons.IconsAlignedLeft || buttons.StartLeft is int start && start >= bar.Left && x + width > start - Px(4))
                nearClock = true;
        }
        if (nearClock)
        {
            // Left of the notification area; the taskbar of another screen has none: left of its clock. Else about
            // where it is.
            int right = buttons.NotifyLeft is int notify && notify > bar.Left
                ? notify
                : buttons.ClockLeft is int clock && clock > bar.Left + bar.Width / 2 && clock < bar.Right
                    ? clock
                    : bar.Right - Px(220);
            // Icons aligned left put the weather next to the clock: before the weather then.
            if (buttons.Weather is { } weather && weather.Left > bar.Left + bar.Width / 2 && weather.Left < right)
                right = weather.Left;
            x = right - width - Px(8 + (style.Position == WidgetPosition.NearClock ? style.Offset : 0));
        }
        return new TaskbarPlace(Math.Max(bar.Left, x), y, height);
    }
}
