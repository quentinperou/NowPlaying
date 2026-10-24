namespace NowPlaying.Core;

/// <summary>A track heard, for the History page.</summary>
public sealed class HistoryEntry
{
    public DateTimeOffset Played { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";

    /// <summary>The player's name ("Google Chrome", "Spotify").</summary>
    public string Player { get; set; } = "";

    /// <summary>A small cover (JPEG in base 64), when known.</summary>
    public string? Cover { get; set; }

    public bool IsSameTrack(string title, string artist, string player) =>
        string.Equals(Title, title, StringComparison.Ordinal) && string.Equals(Artist, artist, StringComparison.Ordinal)
        && string.Equals(Player, player, StringComparison.Ordinal);
}

/// <summary>
/// The tracks heard (the History page, asked by the author on October 24, 2026), the newest first: a new entry when
/// another track plays (not when the same one goes on after a pause), kept <see cref="MaxDays"/> days and
/// <see cref="MaxEntries"/> entries at most. The app saves it on this PC only (history.json).
/// </summary>
public sealed class PlayHistory
{
    public const int MaxDays = 30;
    public const int MaxEntries = 1000;

    public List<HistoryEntry> Entries { get; set; } = [];

    /// <summary>Whether this is another track than the last one heard (a title, not the same as the last entry).</summary>
    public bool IsNewTrack(string title, string artist, string player) =>
        !string.IsNullOrWhiteSpace(title) && (Entries.Count == 0 || !Entries[0].IsSameTrack(title, artist, player));

    /// <summary>
    /// A track playing now. True when the history changed: a new entry, or another cover for the last one. The cover
    /// often comes after the title, and a browser first gives its own logo, then the page's picture: the last one
    /// given stays.
    /// </summary>
    public bool Heard(string title, string artist, string player, string? cover, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        if (Entries.Count > 0 && Entries[0].IsSameTrack(title, artist, player))
        {
            if (cover is null || cover == Entries[0].Cover)
                return false;
            Entries[0].Cover = cover;
            return true;
        }
        Entries.Insert(0, new HistoryEntry { Played = now, Title = title, Artist = artist, Player = player, Cover = cover });
        Prune(now);
        return true;
    }

    /// <summary>Forgets what is older than <see cref="MaxDays"/> days, and the oldest beyond <see cref="MaxEntries"/>.</summary>
    public void Prune(DateTimeOffset now)
    {
        Entries.RemoveAll(e => now - e.Played > TimeSpan.FromDays(MaxDays));
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
    }
}
