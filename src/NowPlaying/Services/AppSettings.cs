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

    /// <summary>The widget is shown in the taskbar (off: only the overlays, for a streamer who wants nothing more).</summary>
    public bool WidgetEnabled { get; set; } = true;

    public WidgetStyle Widget { get; set; } = new();

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
                settings.OverlayBar = (settings.OverlayBar ?? OverlayStyle.DefaultBar()).Normalized(OverlayLayout.Bar);
                settings.OverlayCard = (settings.OverlayCard ?? OverlayStyle.DefaultCard()).Normalized(OverlayLayout.Card);
                if (settings.OverlayPort is < 1024 or > 65535)
                    settings.OverlayPort = DefaultOverlayPort;
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
