using System.Globalization;

namespace NowPlaying.Core;

/// <summary>
/// Position of a track between two reports of the player. Players tell Windows where they are only now and then
/// (when a track starts, on a seek, on pause): while playing, the position goes on from the last report.
/// </summary>
public static class Timeline
{
    /// <summary>
    /// The position at <paramref name="now"/>, from the last report (<paramref name="position"/> at
    /// <paramref name="reportedAt"/>), kept within the track (0 to <paramref name="duration"/> when known).
    /// </summary>
    public static TimeSpan Extrapolate(TimeSpan position, DateTimeOffset reportedAt, DateTimeOffset now, bool playing,
        double rate, TimeSpan duration)
    {
        var at = position;
        // A report time far away is a player that never fills it in: no guess then.
        if (playing && reportedAt > now - TimeSpan.FromDays(1) && reportedAt <= now + TimeSpan.FromMinutes(1))
            at += (now - reportedAt) * (rate > 0 ? rate : 1);
        if (at < TimeSpan.Zero)
            at = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && at > duration)
            at = duration;
        return at;
    }

    /// <summary>"3:34", "1:02:05" for an hour or more (a podcast).</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
    }

    /// <summary>Share of the track played, 0 to 1 (0 when the length is unknown).</summary>
    public static double Fraction(TimeSpan position, TimeSpan duration) =>
        duration > TimeSpan.Zero ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1) : 0;

    /// <summary>
    /// What the progress shows: the times and the bar of a track (its length is known), "live" (no length, but the
    /// player reports a position: a live stream), or nothing (a player that tells neither: "live" would be wrong for a
    /// song). <paramref name="reportedPosition"/>: the player's, not <see cref="Extrapolate"/>'s, which goes on from 0
    /// for a player that reports no timeline at all (Firefox on some sites).
    /// </summary>
    public static ProgressKind Kind(TimeSpan reportedPosition, TimeSpan duration) =>
        duration > TimeSpan.Zero ? ProgressKind.Track : reportedPosition > TimeSpan.Zero ? ProgressKind.Live : ProgressKind.None;
}

/// <summary>See <see cref="Timeline.Kind"/>.</summary>
public enum ProgressKind
{
    None,
    Track,
    Live,
}
