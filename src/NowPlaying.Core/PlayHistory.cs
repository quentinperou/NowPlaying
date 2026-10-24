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

    /// <summary>Whether <see cref="Heard"/> would change something (a new track, or the cover of the last one found).</summary>
    public bool IsNew(string title, string artist, string player, bool hasCover) =>
        !string.IsNullOrWhiteSpace(title)
        && (Entries.Count == 0 || !Entries[0].IsSameTrack(title, artist, player) || (Entries[0].Cover is null && hasCover));

    /// <summary>A track playing now. True when the history changed: a new entry, or the cover of the last one found.</summary>
    public bool Heard(string title, string artist, string player, string? cover, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        if (Entries.Count > 0 && Entries[0].IsSameTrack(title, artist, player))
        {
            if (Entries[0].Cover is not null || cover is null)
                return false;
            Entries[0].Cover = cover; // the cover often comes a moment after the title
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
