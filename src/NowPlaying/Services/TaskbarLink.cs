using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Windows.Media.Imaging;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// What the widget process shows, sent by the app at each change (one line of JSON): the widget, and for its menu the
/// players known to Windows, the language and the theme of the app.
/// </summary>
internal sealed record TaskbarState(WidgetStyle Style, bool Wanted, bool Idle, TaskbarMedia? Media,
    IReadOnlyList<MediaSource> Sources, string Language, AppTheme Theme);

/// <summary>A <see cref="MediaSnapshot"/> without its cover, which goes in a line of its own when it changes.</summary>
internal sealed record TaskbarMedia(
    string Id, string AppId, string AppName, string Title, string Artist, string Album,
    int CoverRevision, string? CoverColor,
    bool IsPlaying, long PositionMs, long ReportedAtMs, long DurationMs, double Rate,
    bool CanPlayPause, bool CanNext, bool CanPrevious, bool CanSeek)
{
    /// <summary>Which cover goes with this track.</summary>
    public string CoverKey => $"{Id}|{CoverRevision}";

    public static TaskbarMedia From(MediaSnapshot m) => new(
        m.Id, m.AppId, m.AppName, m.Title, m.Artist, m.Album, m.CoverRevision, m.CoverColor?.ToHex(),
        m.IsPlaying, (long)m.Position.TotalMilliseconds, m.PositionReportedAt.ToUnixTimeMilliseconds(), (long)m.Duration.TotalMilliseconds,
        m.Rate, m.CanPlayPause, m.CanNext, m.CanPrevious, m.CanSeek);

    public MediaSnapshot ToSnapshot(BitmapSource? cover) => new(
        Id, AppId, AppName, Title, Artist, Album, cover, null, "", CoverRevision,
        CoverColor is not null && Rgb.TryParse(CoverColor, out var color) ? color : null,
        IsPlaying, TimeSpan.FromMilliseconds(PositionMs), DateTimeOffset.FromUnixTimeMilliseconds(ReportedAtMs).ToLocalTime(),
        TimeSpan.FromMilliseconds(DurationMs), Rate, CanPlayPause, CanNext, CanPrevious, CanSeek);
}

/// <summary>A cover for the widget process: its key (<see cref="TaskbarMedia.CoverKey"/>) and its bytes.</summary>
internal sealed record TaskbarCover(string Key, byte[]? Data);

/// <summary>
/// Lines of text both ways over a pipe between the app and the widget process ("verb payload"). Reading and writing
/// each run on a thread of their own: a side that hangs never holds the other one, its lines just wait (and are
/// dropped past a few hundred, by then the watchdog has acted).
/// </summary>
internal sealed class LineChannel : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly PipeStream _pipe;
    private readonly BlockingCollection<string> _out = new(boundedCapacity: 256);
    private readonly Action<string> _received;
    private readonly Action _closed;
    private int _isClosed;

    /// <param name="received">Each line read, on the reading thread.</param>
    /// <param name="closed">Once, when the pipe breaks or the channel is disposed, on a thread of the channel.</param>
    public LineChannel(PipeStream pipe, Action<string> received, Action closed)
    {
        _pipe = pipe;
        _received = received;
        _closed = closed;
        new Thread(Read) { IsBackground = true, Name = "Pipe in" }.Start();
        new Thread(Write) { IsBackground = true, Name = "Pipe out" }.Start();
    }

    public void Send(string verb, string? payload = null)
    {
        try
        {
            _out.TryAdd(payload is null ? verb : $"{verb} {payload}");
        }
        catch (InvalidOperationException)
        {
            // Closed meanwhile.
        }
    }

    /// <summary>Splits a line into its verb and its payload ("" when none).</summary>
    public static (string Verb, string Payload) Parse(string line)
    {
        int space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..]);
    }

    private void Read()
    {
        try
        {
            using var reader = new StreamReader(_pipe, Utf8, false, 1 << 16, leaveOpen: true);
            while (reader.ReadLine() is { } line)
                _received(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        Close();
    }

    private void Write()
    {
        try
        {
            foreach (string line in _out.GetConsumingEnumerable())
            {
                _pipe.Write(Utf8.GetBytes(line + "\n"));
                _pipe.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
        }
        Close();
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            return;
        _out.CompleteAdding();
        try
        {
            _pipe.Dispose();
        }
        catch (IOException)
        {
        }
        _closed();
    }

    public void Dispose() => Close();
}
