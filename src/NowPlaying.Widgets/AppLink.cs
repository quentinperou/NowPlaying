using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace NowPlaying.Widgets;

/// <summary>What NowPlaying says plays, as the widget shows it (the pictures as data URIs, see <see cref="Artwork"/>).</summary>
internal sealed record NowState(bool Online, bool HasTrack, bool Playing, string Title, string Artist, string App, string Cover,
    string Background, string BackgroundOnLight);

/// <summary>
/// The link with NowPlaying: the events of its local server (as an OBS overlay page gets them), its cover, and the
/// buttons of the widget sent back. When NowPlaying is not running, the widget says so and offers to start it.
/// </summary>
internal sealed class AppLink : IDisposable
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowPlaying", "settings.json");

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _stop = new();
    private string _coverUrl = "";
    private (string Cover, string Background, string BackgroundOnLight) _artwork = ("", "", "");

    public AppLink() => Task.Run(Listen);

    public NowState State { get; private set; } = new(false, false, false, "", "", "", "", "", "");

    /// <summary>What plays changed, or NowPlaying came or went (on a pool thread).</summary>
    public event Action? Changed;

    /// <summary>"en" or "fr", as NowPlaying is set (its settings), else as Windows.</summary>
    public static string Language
    {
        get
        {
            string language = ReadSetting("Language") ?? "system";
            if (language is "en" or "fr")
                return language;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        }
    }

    private static int Port => int.TryParse(ReadSetting("OverlayPort"), out int port) && port is >= 1024 and <= 65535 ? port : 6740;

    /// <summary>Sends a button of the widget to NowPlaying: playpause, next, previous.</summary>
    public async Task SendAsync(string verb)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{Port}/api/{verb}");
            // A header a web page cannot send to another site without asking first: no page can press the buttons.
            request.Headers.Add("X-NowPlaying", "widget");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, cancel.Token);
            Log.Write($"{verb}: {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Log.Write($"{verb} failed: {ex.Message}");
        }
    }

    /// <summary>Starts NowPlaying (installed next to this exe).</summary>
    public static void StartApp()
    {
        // The Store version: NowPlaying is an app of this same package, started by its id so that it runs as the
        // package's app (its startup task, its updates).
        if (StoreAppId() is { } appId)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = false });
            return;
        }
        string exe = Path.Combine(AppContext.BaseDirectory, "NowPlaying.exe");
        // Through File Explorer: started by this process, the app would run with the identity of the widget's package.
        if (File.Exists(exe))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false });
    }

    /// <summary>
    /// The id of NowPlaying in the Store package (store\AppxManifest.xml); null for the version installed from GitHub,
    /// whose package (sparse, package\AppxManifest.xml) holds the widget only.
    /// </summary>
    public static string? StoreAppId()
    {
        try
        {
            var id = Windows.ApplicationModel.Package.Current.Id;
            return id.Name == "NowPlaying.Widgets" ? null : $"{id.FamilyName}!App";
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task Listen()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // "widget": served even with the OBS overlay off.
                using var response = await _http.GetAsync($"http://localhost:{Port}/events?layout=widget",
                    HttpCompletionOption.ResponseHeadersRead, _stop.Token);
                response.EnsureSuccessStatusCode();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(_stop.Token));
                string? name = null;
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                        name = line[7..];
                    else if (line.StartsWith("data: ", StringComparison.Ordinal) && name == "state")
                        await ApplyAsync(line[6..]);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException)
            {
                if (_stop.IsCancellationRequested)
                    return;
            }
            // NowPlaying is not running (or quit): say so, look again in a moment.
            if (State.Online)
            {
                State = State with { Online = false };
                Changed?.Invoke();
            }
            try
            {
                await Task.Delay(3000, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ApplyAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        string coverUrl = Text("cover");
        if (coverUrl != _coverUrl)
        {
            _coverUrl = coverUrl;
            _artwork = coverUrl.Length == 0 ? ("", "", "") : await ArtworkAsync(coverUrl);
        }
        var state = new NowState(true, Flag("hasTrack"), Flag("playing"), Text("title"), Text("artist"), Text("app"), _artwork.Cover,
            _artwork.Background, _artwork.BackgroundOnLight);
        if (state != State)
        {
            State = state;
            Changed?.Invoke();
        }
    }

    // The pictures of the widget, from the cover (see Artwork): the widget host draws them without asking NowPlaying.
    private async Task<(string Cover, string Background, string BackgroundOnLight)> ArtworkAsync(string url)
    {
        try
        {
            using var response = await _http.GetAsync($"http://localhost:{Port}{url}", _stop.Token);
            if (!response.IsSuccessStatusCode)
                return ("", "", "");
            return await Artwork.FromCoverAsync(await response.Content.ReadAsByteArrayAsync(_stop.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return ("", "", "");
        }
        catch (Exception ex)
        {
            // An image Windows cannot read.
            Log.Write($"Cover: {ex.Message}");
            return ("", "", "");
        }
    }

    private static string? ReadSetting(string name)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            return document.RootElement.TryGetProperty(name, out var value) ? value.ToString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
    }
}
