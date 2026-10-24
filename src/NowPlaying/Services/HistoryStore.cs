using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// The tracks heard (<see cref="PlayHistory"/>), saved on this PC only: %LOCALAPPDATA%\NowPlaying\history.json, and
/// their covers as small pictures in the "covers" folder next to it (one file per cover, shared by the entries that
/// have it), read only when the History page shows them: the resident app keeps none of them in memory.
/// </summary>
internal sealed class HistoryStore
{
    private const int CoverSize = 64;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowPlaying");
    private static readonly string FilePath = Path.Combine(Folder, "history.json");
    public static readonly string CoversFolder = Path.Combine(Folder, "covers");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private HistoryStore(PlayHistory history)
    {
        History = history;
        _saveTimer.Tick += (_, _) => Save();
    }

    public PlayHistory History { get; }

    /// <summary>An entry was added, or the cover of the last one found, or the history emptied.</summary>
    public event EventHandler? Changed;

    public static HistoryStore Load()
    {
        var history = new PlayHistory();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<PlayHistory>(File.ReadAllText(FilePath)) is { } read)
                history = read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Debug.WriteLine($"History: {ex.Message}");
        }
        history.Entries.RemoveAll(e => e is null);
        history.Prune(DateTimeOffset.Now);
        return new HistoryStore(history);
    }

    /// <summary>A track playing now: a new entry if it is another track (its cover kept as a small picture).</summary>
    public void Heard(MediaSnapshot media)
    {
        if (!History.IsNew(media.Title, media.Artist, media.AppName, media.Cover is not null))
            return;
        string? cover = media.Cover is { } picture ? SaveCover(picture) : null;
        if (!History.Heard(media.Title, media.Artist, media.AppName, cover, DateTimeOffset.Now))
            return;
        Changed?.Invoke(this, EventArgs.Empty);
        SaveSoon();
    }

    public void Clear()
    {
        History.Entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        Save();
    }

    /// <summary>The picture of an entry's cover, at the size of the page; null without one.</summary>
    public static ImageSource? Cover(HistoryEntry entry)
    {
        if (entry.Cover is not { } name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        string path = Path.Combine(CoversFolder, name);
        if (!File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes the history now (the app quits), and forgets the covers no entry uses any more.</summary>
    public void Save()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(History));
            if (Directory.Exists(CoversFolder))
            {
                var used = History.Entries.Select(e => e.Cover).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.EnumerateFiles(CoversFolder, "*.jpg"))
                    if (!used.Contains(Path.GetFileName(file)))
                        File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"History: {ex.Message}");
        }
    }

    // The cover at 64 px, as a JPEG named after its content (the same cover is written once).
    private static string? SaveCover(BitmapSource cover)
    {
        try
        {
            double scale = (double)CoverSize / Math.Max(cover.PixelWidth, cover.PixelHeight);
            var small = new TransformedBitmap(cover, new ScaleTransform(scale, scale));
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(small));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            byte[] bytes = memory.ToArray();
            string name = Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant() + ".jpg";
            Directory.CreateDirectory(CoversFolder);
            string path = Path.Combine(CoversFolder, name);
            if (!File.Exists(path))
                File.WriteAllBytes(path, bytes);
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            Debug.WriteLine($"History cover: {ex.Message}");
            return null;
        }
    }
}
