using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using NowPlaying.Controls;
using NowPlaying.Core;
using NowPlaying.Services;

namespace NowPlaying;

public partial class App : Application
{
    /// <summary>Started with Windows (Run key): nothing to open, the widget and the overlays just work.</summary>
    public const string BackgroundArgument = "--background";

    // Windows display language, kept to come back to it when the user picks "Windows language".
    private static readonly CultureInfo SystemUICulture = CultureInfo.CurrentUICulture;

    private static bool s_themeApplied;
    private static DispatcherTimer? s_saveTimer;
    private static DispatcherTimer? s_levelsTimer;
    private static bool s_levelsHeld;
    private SingleInstance? _instance;

    /// <summary>The settings, loaded once at startup; the window changes and saves this instance.</summary>
    public static AppSettings Settings { get; private set; } = new();

    internal static MediaSessions Media { get; private set; } = null!;

    internal static AudioSpectrum Spectrum { get; private set; } = null!;

    internal static OverlayServer Overlay { get; private set; } = null!;

    internal static TrayHost Tray { get; private set; } = null!;

    internal static TaskbarWidget Widget { get; private set; } = null!;

    /// <summary>A style changed (the window's previews follow).</summary>
    public static event EventHandler? StyleChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.TryStart(e.Args, _ => Dispatcher.BeginInvoke(() => OpenMainWindow()));
        if (_instance is null)
        {
            Shutdown(); // already running: it has been told to open its window
            return;
        }

        Settings = AppSettings.Load();

        // UI language: the one chosen in the settings, else the Windows one (French, otherwise English).
        // NOWPLAYING_LANG=en|fr forces one, handy to test both.
        if (Environment.GetEnvironmentVariable("NOWPLAYING_LANG") is { Length: > 0 } forced)
            ApplyLanguage(forced);
        else
            ApplyLanguage(Settings.Language);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "NowPlaying", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Media = new MediaSessions(Dispatcher);
        Spectrum = new AudioSpectrum(Dispatcher);
        Overlay = new OverlayServer();
        Overlay.ClientsChanged += (_, _) => Dispatcher.BeginInvoke(UpdateOverlayLevels);
        Overlay.Start(Settings.OverlayPort);

        Tray = new TrayHost();
        Tray.IconClicked += (_, _) => OpenMainWindow();
        Tray.MenuRequested += (_, _) => PopupMenu.ShowTray();
        Tray.TaskbarCreated += (_, _) => Widget.FindTaskbarButtons();

        Widget = new TaskbarWidget();
        Widget.BackdropChanged += (_, _) =>
        {
            RefreshWidget();
            StyleChanged?.Invoke(null, EventArgs.Empty);
        };
        Widget.TaskbarButtonsChanged += (_, _) => StyleChanged?.Invoke(null, EventArgs.Empty);
        Widget.View.Spectrum = Spectrum;
        Wire(Widget.View);
        Widget.View.MenuRequested += (_, _) => PopupMenu.ShowWidget();
        // The window exists (handle, hooks) before it is shown for the first time.
        new System.Windows.Interop.WindowInteropHelper(Widget).EnsureHandle();

        Media.Changed += (_, _) => MediaChanged();
        ApplyStyles(save: false);
        _ = Media.StartAsync();

        // Start with Windows: on by default for the installed app, once; the user can turn it off in Settings.
        if (!Settings.StartupConfigured && WindowsSettings.IsInstalled)
        {
            WindowsSettings.StartWithWindows = true;
            Settings.StartupConfigured = true;
            Settings.Save();
        }

        if (!e.Args.Contains(BackgroundArgument))
            OpenMainWindow();

        _ = Updater.CheckAtStartupAsync();
        MemoryTrim.Soon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        s_saveTimer?.Stop();
        Overlay?.Dispose();
        Spectrum?.Dispose();
        Tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    // ---------------------------------------------------------------- What plays

    private static void MediaChanged()
    {
        var media = Media.Current;
        RefreshWidget();
        Overlay.UpdateState(media, keepWhenIdle: false);
        // The "Cover" color follows the track.
        Overlay.UpdateStyle(OverlayLayout.Bar, Settings.OverlayBar, media?.CoverColor);
        Overlay.UpdateStyle(OverlayLayout.Card, Settings.OverlayCard, media?.CoverColor);
        UpdateOverlayLevels();
    }

    /// <summary>The widget in the taskbar: its colors (taskbar light or dark, the cover), what it shows, where it is.</summary>
    internal static void RefreshWidget()
    {
        var media = Media.Current;
        var style = Settings.Widget;
        Widget.View.ApplyStyle(style, SurfaceColors.Resolve(style, media?.CoverColor, TaskbarIsDark, overlay: false));
        // Nothing known to Windows: hidden, or the last track greyed if the user asked to keep it.
        if (media is not null)
            s_lastMedia = media;
        bool idle = media is null;
        Widget.View.ShowMedia(idle ? s_lastMedia : media, idle);
        bool wanted = Settings.WidgetEnabled && (media is not null || (style.Idle == IdleMode.KeepLast && s_lastMedia is not null));
        Widget.SetWanted(wanted);
    }

    private static MediaSnapshot? s_lastMedia;

    /// <summary>
    /// Whether the taskbar behind the widget is dark: read on the screen once the widget is placed (any taskbar color,
    /// or a wallpaper through a see-through taskbar), else Windows' light / dark setting.
    /// </summary>
    internal static bool TaskbarIsDark => Widget?.BackdropIsDark ?? WindowsSettings.TaskbarIsDark;

    /// <summary>
    /// The bars of the bar overlay: the real spectrum is computed here and sent while a page of it is open and
    /// something plays (the "Animation" bars are drawn by the page itself).
    /// </summary>
    private static void UpdateOverlayLevels()
    {
        var bar = Settings.OverlayBar;
        bool needed = Overlay.HasClients(OverlayLayout.Bar) && bar.ShowVisualizer && bar.Visualizer == VisualizerMode.Spectrum
            && Media.Current is { IsPlaying: true };
        if (needed && s_levelsTimer is not { IsEnabled: true })
        {
            if (s_levelsTimer is null)
            {
                s_levelsTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
                s_levelsTimer.Tick += (_, _) => Overlay.PushLevels(Spectrum.Levels());
            }
            if (!s_levelsHeld)
            {
                Spectrum.Acquire();
                s_levelsHeld = true;
            }
            s_levelsTimer.Start();
        }
        else if (!needed && s_levelsTimer is { IsEnabled: true })
        {
            s_levelsTimer.Stop();
            if (s_levelsHeld)
            {
                Spectrum.Release();
                s_levelsHeld = false;
            }
        }
    }

    /// <summary>Connects a widget (the one in the taskbar, or the preview of the window) to the player.</summary>
    internal static void Wire(WidgetView view)
    {
        view.PlayPauseRequested += (_, _) => _ = Media.TogglePlayPauseAsync();
        view.NextRequested += (_, _) => _ = Media.NextAsync();
        view.PreviousRequested += (_, _) => _ = Media.PreviousAsync();
        view.SeekRequested += (_, fraction) => _ = Media.SeekAsync(fraction);
        view.VolumeWheel += (_, notches) =>
        {
            if (Media.Current is not { } media)
                return;
            // 2 % a notch, as the volume flyout of Windows.
            float? volume = AppVolume.Get(media.AppId);
            if (volume is { } v && notches != 0)
                volume = AppVolume.Set(media.AppId, (float)Math.Round(v + notches * 0.02, 2));
            view.ShowVolume(volume);
        };
        view.VolumeRequested += (_, fraction) =>
        {
            if (Media.Current is { } media)
                view.ShowVolume(AppVolume.Set(media.AppId, (float)fraction));
        };
    }

    // ---------------------------------------------------------------- Settings

    /// <summary>A style changed in the window: shown at once everywhere, saved a moment later.</summary>
    internal static void ApplyStyles(bool save = true)
    {
        var media = Media.Current;
        RefreshWidget();
        Overlay.UpdateStyle(OverlayLayout.Bar, Settings.OverlayBar, media?.CoverColor);
        Overlay.UpdateStyle(OverlayLayout.Card, Settings.OverlayCard, media?.CoverColor);
        UpdateOverlayLevels();
        StyleChanged?.Invoke(null, EventArgs.Empty);
        if (save)
            SaveSoon();
    }

    internal static void SetWidgetEnabled(bool enabled)
    {
        Settings.WidgetEnabled = enabled;
        Settings.Save();
        RefreshWidget();
    }

    internal static void RestartOverlayServer()
    {
        Overlay.Start(Settings.OverlayPort);
        MediaChanged();
    }

    // Sliders change the style many times a second: one write once they stop.
    private static void SaveSoon()
    {
        if (s_saveTimer is null)
        {
            s_saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            s_saveTimer.Tick += (_, _) =>
            {
                s_saveTimer.Stop();
                Settings.Save();
            };
        }
        s_saveTimer.Stop();
        s_saveTimer.Start();
    }

    // ---------------------------------------------------------------- Windows, theme, language

    /// <summary>
    /// Loads the Fluent theme (light/dark/system) the first time the window or a menu opens: until then the app does not
    /// hold the theme's resources. NOWPLAYING_THEME=Light|Dark forces one.
    /// </summary>
    public static void EnsureTheme()
    {
        if (s_themeApplied)
            return;
        s_themeApplied = true;
        ApplyTheme(Environment.GetEnvironmentVariable("NOWPLAYING_THEME") switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => Settings.Theme,
        });
    }

    /// <summary>Sets the theme of every window, at once: all colors come from the Fluent theme through DynamicResource.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        // The widget is not themed (it draws its own colors): it stays see-through.
        Widget?.KeepTransparent();
    }

    /// <summary>Sets the UI language: <see cref="AppSettings.SystemLanguage"/> or a culture name ("en", "fr").</summary>
    public static void ApplyLanguage(string language)
    {
        var culture = language == AppSettings.SystemLanguage ? SystemUICulture : CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// Opens the app window, or brings it to the front. Texts are read once, when the window is built: after a language
    /// change, the window is built again on the same page.
    /// </summary>
    public static void OpenMainWindow(MainPage? page = null)
    {
        if (Current.Windows.OfType<MainWindow>().FirstOrDefault() is { } open)
        {
            if (page is { } requested)
                open.ShowPage(requested);
            if (open.WindowState == WindowState.Minimized)
                open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        EnsureTheme();
        var window = new MainWindow(page ?? MainPage.Widget);
        window.Closed += (_, _) =>
        {
            if (window.LanguageChanged is { } reopenOn)
            {
                ApplyLanguage(Settings.Language);
                Current.Dispatcher.BeginInvoke(() => OpenMainWindow(reopenOn), DispatcherPriority.ApplicationIdle);
            }
            else
                MemoryTrim.Soon();
        };
        window.Show();
        window.Activate();
    }

    /// <summary>Quits NowPlaying: the widget, the overlays and the notification icon go with it.</summary>
    public static void Quit() => Current.Shutdown();
}
