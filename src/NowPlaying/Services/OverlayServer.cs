using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>
/// Serves the overlay pages to OBS (a Browser Source) on this PC only: http://localhost:port/bar and /card. The
/// page gets what plays, its style and the bars of the visualizer as a stream of events (Server-Sent Events): a change
/// in the app shows in OBS at once, and the page reconnects by itself when the app restarts. http.sys does the
/// listening: nothing runs in the app until a page asks.
/// </summary>
internal sealed class OverlayServer : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly List<Client> _clients = [];
    private readonly object _lock = new();
    private HttpListener? _listener;

    // Written on the UI thread, read by the request threads.
    private volatile string _stateJson = "{\"hasTrack\":false}";
    private volatile string _barStyleJson = "{}";
    private volatile string _cardStyleJson = "{}";
    private volatile byte[]? _cover;
    private volatile string _coverType = "image/png";

    private sealed class Client(HttpListenerResponse response, OverlayLayout layout)
    {
        public HttpListenerResponse Response { get; } = response;
        public OverlayLayout Layout { get; } = layout;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    public int Port { get; private set; }

    /// <summary>Why the server could not start (port taken), null when it runs.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => _listener?.IsListening == true;

    /// <summary>A page connected or went away (on a request thread).</summary>
    public event EventHandler? ClientsChanged;

    public static string Url(int port, OverlayLayout layout) =>
        $"http://localhost:{port}/{(layout == OverlayLayout.Bar ? "bar" : "card")}";

    public void Start(int port)
    {
        Stop();
        Port = port;
        Error = null;
        try
        {
            var listener = new HttpListener();
            // "localhost" needs no administrator rights and is reachable from this PC only.
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            _listener = listener;
            _ = Task.Run(() => AcceptLoop(listener));
        }
        catch (HttpListenerException ex)
        {
            Error = ex.Message;
            Debug.WriteLine($"Overlay server: {ex.Message}");
        }
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        if (listener is null)
            return;
        try
        {
            listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        lock (_lock)
        {
            foreach (var client in _clients)
                Close(client);
            _clients.Clear();
        }
    }

    /// <summary>Whether a page of this layout is open (in OBS, or a browser).</summary>
    public bool HasClients(OverlayLayout layout)
    {
        lock (_lock)
            return _clients.Any(c => c.Layout == layout);
    }

    // ---------------------------------------------------------------- What the pages show (UI thread)

    public void UpdateState(MediaSnapshot? media, bool keepWhenIdle)
    {
        _cover = media?.CoverBytes;
        _coverType = media?.CoverType ?? "image/png";
        _stateJson = JsonSerializer.Serialize(new
        {
            hasTrack = media is not null,
            title = media?.Title ?? "",
            artist = media?.Artist ?? "",
            app = media?.AppName ?? "",
            playing = media?.IsPlaying ?? false,
            positionMs = (long)(media?.PositionNow.TotalMilliseconds ?? 0),
            durationMs = (long)(media?.Duration.TotalMilliseconds ?? 0),
            cover = media?.CoverBytes is { Length: > 0 } ? $"/cover?r={media.CoverRevision}" : null,
            keepWhenIdle,
        }, Json);
        Broadcast("state", _stateJson, null);
    }

    public void UpdateStyle(OverlayLayout layout, OverlayStyle style, Rgb? coverColor)
    {
        var colors = SurfaceColors.Resolve(style, coverColor, onDarkSurface: true, overlay: true);
        string json = JsonSerializer.Serialize(new
        {
            layout = layout == OverlayLayout.Bar ? "bar" : "card",
            width = style.Width,
            radius = style.CornerRadius,
            fontSize = style.FontSize,
            font = style.Font,
            background = colors.Background.ToCss(),
            text = colors.Text.ToCss(),
            secondary = colors.SecondaryText.ToCss(),
            track = colors.Track.ToCss(),
            frosted = colors.Frosted,
            showCover = style.ShowCover,
            showArtist = style.ShowArtist,
            showVisualizer = layout == OverlayLayout.Bar && style.ShowVisualizer,
            showProgress = layout == OverlayLayout.Card && style.ShowProgress,
            showTimes = layout == OverlayLayout.Card && style.ShowTimes,
            visualizer = style.Visualizer == VisualizerMode.Spectrum ? "spectrum" : "animation",
        }, Json);
        if (layout == OverlayLayout.Bar)
            _barStyleJson = json;
        else
            _cardStyleJson = json;
        Broadcast("style", json, layout);
    }

    /// <summary>The bars of the spectrum, to the pages of the bar layout (about 30 times a second while playing).</summary>
    public void PushLevels(ReadOnlySpan<float> levels)
    {
        var text = new StringBuilder("[");
        for (int i = 0; i < levels.Length; i++)
        {
            if (i > 0)
                text.Append(',');
            text.Append(levels[i].ToString("0.##", CultureInfo.InvariantCulture));
        }
        text.Append(']');
        Broadcast("levels", text.ToString(), OverlayLayout.Bar, skipIfBusy: true);
    }

    // ---------------------------------------------------------------- Requests

    private async Task AcceptLoop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            string path = (context.Request.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
            switch (path)
            {
                case "":
                    await SendText(response, IndexPage(), "text/html; charset=utf-8");
                    break;
                case "/bar" or "/card":
                    await SendResource(response, "overlay.html", "text/html; charset=utf-8");
                    break;
                case "/overlay.js":
                    await SendResource(response, "overlay.js", "text/javascript; charset=utf-8");
                    break;
                case "/overlay.css":
                    await SendResource(response, "overlay.css", "text/css; charset=utf-8");
                    break;
                case "/cover":
                    if (_cover is { } cover)
                        await SendBytes(response, cover, _coverType);
                    else
                        End(response, 404);
                    break;
                case "/events":
                    await OpenEvents(response, context.Request.QueryString["layout"] == "bar" ? OverlayLayout.Bar : OverlayLayout.Card);
                    break;
                default:
                    End(response, 404);
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            Debug.WriteLine($"Overlay request failed: {ex.Message}");
        }
    }

    private async Task OpenEvents(HttpListenerResponse response, OverlayLayout layout)
    {
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;
        var client = new Client(response, layout);
        // The page starts with the style and what plays now.
        string style = layout == OverlayLayout.Bar ? _barStyleJson : _cardStyleJson;
        await client.Gate.WaitAsync();
        lock (_lock)
            _clients.Add(client);
        try
        {
            await Write(client, $"retry: 2000\nevent: style\ndata: {style}\n\nevent: state\ndata: {_stateJson}\n\n");
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            lock (_lock)
                _clients.Remove(client);
            Close(client);
            return;
        }
        finally
        {
            client.Gate.Release();
        }
        ClientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Broadcast(string name, string json, OverlayLayout? only, bool skipIfBusy = false)
    {
        List<Client> targets;
        lock (_lock)
            targets = [.. _clients.Where(c => only is null || c.Layout == only)];
        if (targets.Count == 0)
            return;
        string message = $"event: {name}\ndata: {json}\n\n";
        foreach (var client in targets)
            _ = Send(client, message, skipIfBusy);
    }

    private async Task Send(Client client, string message, bool skipIfBusy)
    {
        if (!await client.Gate.WaitAsync(skipIfBusy ? 0 : 5000))
            return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.Response.OutputStream.WriteAsync(bytes);
            await client.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // OBS closed the source, or the browser tab.
            bool removed;
            lock (_lock)
                removed = _clients.Remove(client);
            Close(client);
            if (removed)
                ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            client.Gate.Release();
        }
    }

    private static async Task Write(Client client, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await client.Response.OutputStream.WriteAsync(bytes);
        await client.Response.OutputStream.FlushAsync();
    }

    private static void Close(Client client)
    {
        try
        {
            client.Response.Abort();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
        }
    }

    private static async Task SendResource(HttpListenerResponse response, string name, string type)
    {
        using var stream = typeof(OverlayServer).Assembly.GetManifestResourceStream("Overlay." + name);
        if (stream is null)
        {
            End(response, 404);
            return;
        }
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        await SendBytes(response, memory.ToArray(), type);
    }

    private static Task SendText(HttpListenerResponse response, string text, string type) =>
        SendBytes(response, Encoding.UTF8.GetBytes(text), type);

    private static async Task SendBytes(HttpListenerResponse response, byte[] bytes, string type)
    {
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-cache";
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static void End(HttpListenerResponse response, int status)
    {
        response.StatusCode = status;
        response.Close();
    }

    // http://localhost:port/: the two addresses, for someone who opens the root in a browser.
    private string IndexPage() =>
        $$"""
        <!doctype html><html><head><meta charset="utf-8"><title>NowPlaying</title>
        <style>body{font:15px "Segoe UI",sans-serif;margin:40px;color:#1b1b1f}code{background:#eee;padding:2px 6px;border-radius:4px}</style></head>
        <body><h1>NowPlaying</h1><p>OBS → Browser Source:</p>
        <ul><li><a href="/bar">/bar</a> <code>{{Url(Port, OverlayLayout.Bar)}}</code></li>
        <li><a href="/card">/card</a> <code>{{Url(Port, OverlayLayout.Card)}}</code></li></ul></body></html>
        """;

    public void Dispose() => Stop();
}
