using System.Text.Json.Serialization;

namespace NowPlaying.Core;

/// <summary>What the text file for OBS holds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrackTextFormat>))]
public enum TrackTextFormat
{
    /// <summary>"Title – Artist".</summary>
    TitleArtist,
    /// <summary>"Artist – Title".</summary>
    ArtistTitle,
    /// <summary>The title alone.</summary>
    Title,
}

/// <summary>A track as one line of text: the text file for OBS, "Copy title – artist" of the widgets' menus.</summary>
public static class TrackText
{
    public const string Separator = " – ";

    /// <summary>The line; one part alone when the other is unknown (a page without artist), empty for none.</summary>
    public static string Format(string? title, string? artist, TrackTextFormat format = TrackTextFormat.TitleArtist)
    {
        title = title?.Trim() ?? "";
        artist = artist?.Trim() ?? "";
        if (format == TrackTextFormat.Title || artist.Length == 0)
            return title;
        if (title.Length == 0)
            return artist;
        return format == TrackTextFormat.ArtistTitle ? artist + Separator + title : title + Separator + artist;
    }
}
