using System.Diagnostics;
using System.Text;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// The text file for OBS (asked by the author on October 24, 2026): the track playing as one line in
/// <see cref="AppSettings.TextFilePath"/>, for a "Text (GDI+)" source reading a file; empty when nothing plays or when
/// the file is turned off. Written only when the line changes, through a temporary file so that OBS never reads half
/// of it.
/// </summary>
internal static class TrackFile
{
    private static string? s_written;

    public static void Update(MediaSnapshot? media, bool enabled, TrackTextFormat format)
    {
        string text = enabled && media is not null ? TrackText.Format(media.Title, media.Artist, format) : "";
        // Off and never written: nothing to empty.
        if (text == s_written || (!enabled && s_written is null && !File.Exists(AppSettings.TextFilePath)))
            return;
        string path = AppSettings.TextFilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
            s_written = text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // OBS reading it at that very moment: the next change writes it again.
            Debug.WriteLine($"Text file: {ex.Message}");
        }
    }
}
