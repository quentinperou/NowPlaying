using System.Text.Json;
using System.Text.Json.Serialization;
using NowPlaying.Core;

namespace NowPlaying.Services;

/// <summary>Light or dark appearance of the app window; <see cref="System"/> follows Windows.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// User preferences, saved in %LOCALAPPDATA%\NowPlaying\settings.json. The app keeps one instance for its whole run
/// (<see cref="App.Settings"/>): the window changes it and saves it, and the change shows at once.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Follow the Windows display language.</summary>
    public const string SystemLanguage = "system";

    /// <summary>UI languages with a translation (Properties/Strings*.resx). English is the default.</summary>
    public static readonly string[] SupportedLanguages = ["en", "fr"];

    public const int DefaultOverlayPort = 6740;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowPlaying", "settings.json");

    /// <summary><see cref="SystemLanguage"/> or one of <see cref="SupportedLanguages"/>.</summary>
    public string Language { get; set; } = SystemLanguage;

    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>How the player shown is chosen: the last one started, or the order of <see cref="Players"/>.</summary>
    public SourceMode SourceMode { get; set; } = SourceMode.Auto;

    /// <summary>In <see cref="SourceMode.Priority"/>: a player that plays goes before one in pause.</summary>
    public bool PreferPlaying { get; set; } = true;

    /// <summary>How long a player chosen in the menu of a widget stays.</summary>
    public PinMode PinMode { get; set; } = PinMode.UntilNextStart;

    /// <summary>The players seen by the app, in the order of priority (the Sources page).</summary>
    public List<KnownPlayer> Players { get; set; } = [];

    /// <summary>The widget is shown in the taskbar (off: only the overlays, for a streamer who wants nothing more).</summary>
    public bool WidgetEnabled { get; set; } = true;

    public WidgetStyle Widget { get; set; } = new();

    /// <summary>The floating widget, anywhere on the screen and above the other windows (off until the user asks).</summary>
    public bool FloatingEnabled { get; set; }

    public WidgetStyle Floating { get; set; } = WidgetStyle.DefaultFloating();

    /// <summary>Top-left corner of the floating widget, in screen pixels; null: the bottom right of the main screen.</summary>
    public int? FloatingX { get; set; }

    public int? FloatingY { get; set; }

    /// <summary>Above the other windows (off: under them, as a widget on the desktop).</summary>
    public bool FloatingTopmost { get; set; } = true;

    /// <summary>The floating widget cannot be dragged (no move by mistake).</summary>
    public bool FloatingLocked { get; set; }

    /// <summary>The overlay pages for OBS are served (off until the user asks: most people do not stream).</summary>
    public bool OverlayEnabled { get; set; }

    public OverlayStyle OverlayBar { get; set; } = OverlayStyle.DefaultBar();

    public OverlayStyle OverlayCard { get; set; } = OverlayStyle.DefaultCard();

    /// <summary>Port of the overlay pages (http://localhost:port/...), on this PC only.</summary>
    public int OverlayPort { get; set; } = DefaultOverlayPort;

    /// <summary>
    /// Set once the “Start with Windows” default (on) has been applied, so that turning it off in Settings stays off.
    /// </summary>
    public bool StartupConfigured { get; set; }

    /// <summary>Ask GitHub for a newer version when the app starts.</summary>
    public bool CheckForUpdatesAtStartup { get; set; } = true;

    /// <summary>Releases offered as updates: stable only, or betas too.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateChannel>))]
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    // Settings of a newer version (a dev build, a beta) are written back as they are, not dropped.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherSettings { get; set; }

    public OverlayStyle Overlay(OverlayLayout layout) => layout == OverlayLayout.Bar ? OverlayBar : OverlayCard;

    public SourcePolicy SourcePolicy() => new(SourceMode, PreferPlaying, PinMode,
        [.. Players.Select(p => p.AppId)], [.. Players.Where(p => p.Hidden).Select(p => p.AppId)]);

    /// <summary>
    /// The players known to Windows now: a new one goes at the end of <see cref="Players"/>, the others get their name
    /// and today's date. True when something changed (to be saved).
    /// </summary>
    public bool RememberPlayers(IEnumerable<(string AppId, string Name)> present)
    {
        bool changed = false;
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (appId, name) in present)
        {
            if (appId.Length == 0)
                continue;
            var player = Players.Find(p => string.Equals(p.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (player is null)
            {
                Players.Add(new KnownPlayer { AppId = appId, Name = name, LastSeen = today });
                changed = true;
            }
            else if (player.Name != name || player.LastSeen != today)
            {
                (player.Name, player.LastSeen) = (name, today);
                changed = true;
            }
        }
        return changed;
    }

    // A missing or damaged file just means default settings: preferences are never worth an error message.
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                if (!SupportedLanguages.Contains(settings.Language))
                    settings.Language = SystemLanguage;
                settings.Widget = (settings.Widget ?? new WidgetStyle()).Normalized();
                settings.Floating = (settings.Floating ?? WidgetStyle.DefaultFloating()).Normalized();
                settings.OverlayBar = (settings.OverlayBar ?? OverlayStyle.DefaultBar()).Normalized(OverlayLayout.Bar);
                settings.OverlayCard = (settings.OverlayCard ?? OverlayStyle.DefaultCard()).Normalized(OverlayLayout.Card);
                if (settings.OverlayPort is < 1024 or > 65535)
                    settings.OverlayPort = DefaultOverlayPort;
                settings.Players = [.. (settings.Players ?? []).Where(p => p is { AppId.Length: > 0 })
                    .DistinctBy(p => p.AppId, StringComparer.OrdinalIgnoreCase)];
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A player seen by the app (its media app id, as Windows gives it), for the Sources page.</summary>
public sealed class KnownPlayer
{
    public string AppId { get; set; } = "";

    /// <summary>Its name when last seen (to list it while it is closed).</summary>
    public string Name { get; set; } = "";

    /// <summary>Never shown by the widgets nor the overlay, nor offered in their menu.</summary>
    public bool Hidden { get; set; }

    public DateOnly LastSeen { get; set; }
}
