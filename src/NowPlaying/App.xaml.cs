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

    /// <summary>The widget in the taskbar, which runs in a process of its own.</summary>
    internal static TaskbarHost Taskbar { get; private set; } = null!;

    /// <summary>The floating widget, built the first time it is turned on.</summary>
    internal static FloatingWidget? Floating { get; private set; }

    /// <summary>A style changed (the window's previews follow).</summary>
    public static event EventHandler? StyleChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The widget process, started by the app (TaskbarHost): the widget of the taskbar, nothing else.
        if (e.Args is [TaskbarHost.Argument, var pipe])
        {
            TaskbarClient.Run(this, pipe);
            return;
        }

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
        Overlay.CommandReceived += command => Dispatcher.BeginInvoke(() => _ = command switch
        {
            "next" => Media.NextAsync(),
            "previous" => Media.PreviousAsync(),
            _ => Media.TogglePlayPauseAsync(),
        });
        Overlay.PagesEnabled = Settings.OverlayEnabled;
        Overlay.Start(Settings.OverlayPort);

        Tray = new TrayHost();
        Tray.IconClicked += (_, _) => OpenMainWindow();
        Tray.MenuRequested += (_, _) => PopupMenu.ShowTray();

        Taskbar = new TaskbarHost(Dispatcher);
        Taskbar.BackdropChanged += (_, _) => StyleChanged?.Invoke(null, EventArgs.Empty);
        Taskbar.WeatherChanged += (_, _) => StyleChanged?.Invoke(null, EventArgs.Empty);
        Taskbar.CommandReceived += command => _ = command switch
        {
            "next" => Media.NextAsync(),
            "previous" => Media.PreviousAsync(),
            _ => Media.TogglePlayPauseAsync(),
        };
        Taskbar.SeekRequested += fraction => _ = Media.SeekAsync(fraction);
        // Its menu is shown by the widget process (it got the click, so its menu may take the focus, and closes when
        // the user clicks elsewhere): the choices come back here.
        Taskbar.PinRequested += id => Media.Pin(id);
        Taskbar.OpenRequested += (_, _) => OpenMainWindow(MainPage.Widget);
        Taskbar.QuitRequested += (_, _) => Quit();

        Media.Changed += (_, _) => MediaChanged();
        Media.SetPolicy(Settings.SourcePolicy());
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
        Taskbar?.Dispose();
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
        // The players seen, listed on the Sources page (also while they are closed).
        if (Settings.RememberPlayers(Media.Players.Select(p => (p.AppId, p.AppName))))
            SaveSoon();
        RefreshWidget();
        Overlay.UpdateState(media, keepWhenIdle: false);
        // The "Cover" color follows the track.
        Overlay.UpdateStyle(OverlayLayout.Bar, Settings.OverlayBar, media?.CoverColor);
        Overlay.UpdateStyle(OverlayLayout.Card, Settings.OverlayCard, media?.CoverColor);
        UpdateOverlayLevels();
    }

    /// <summary>
    /// The widgets: the one in the taskbar (its process resolves its colors and its place) and the floating one, what
    /// they show and in which style.
    /// </summary>
    internal static void RefreshWidget()
    {
        var media = Media.Current;
        // Nothing known to Windows: hidden, or the last track greyed if the user asked to keep it.
        if (media is not null)
            s_lastMedia = media;
        bool idle = media is null;
        var shown = idle ? s_lastMedia : media;
        bool Wanted(bool enabled, WidgetStyle style) => enabled && (media is not null || (style.Idle == IdleMode.KeepLast && s_lastMedia is not null));

        var widget = Settings.Widget;
        Taskbar.Update(widget, shown, idle, Wanted(Settings.WidgetEnabled, widget), Settings.WidgetEnabled,
            Media.Sources, Settings.Language);

        var floating = Settings.Floating;
        if (Settings.FloatingEnabled && Floating is null)
            CreateFloating();
        if (Floating is not null)
        {
            // Over any window: the colors of a dark surface (the glass is dark there), its veil as opaque as asked.
            Floating.OnTop = Settings.FloatingTopmost;
            Floating.Locked = Settings.FloatingLocked;
            // Over a blur as strong as asked (none: "Transparent"), in colors read on what is behind it.
            s_floatingColors = FloatingColors(floating, shown?.CoverColor, Floating.Backdrop);
            Floating.View.ApplyStyle(floating, s_floatingColors, GlassLook(floating) ? floating.CornerRadius : null);
            ShowBehindFloating();
            Floating.SetLook(floating.BlurStrength / 100.0, floating.CornerRadius, floating.Shadow ? floating.ShadowOpacity / 100.0 : 0);
            Floating.View.ShowMedia(shown, idle);
            Floating.SetWanted(Wanted(Settings.FloatingEnabled, floating));
            // What is behind it is read again now and then only while something plays.
            Floating.WatchBackdrop = media is { IsPlaying: true };
        }
    }

    /// <summary>
    /// The colors of the floating widget (also those of its preview in the window): the text dark or light after
    /// <paramref name="backdrop"/>, what is behind it, when known.
    /// </summary>
    internal static SurfaceColors FloatingColors(WidgetStyle style, Rgb? coverColor, Rgb? backdrop = null) =>
        SurfaceColors.Resolve(style, coverColor, onDarkSurface: true, overlay: true, glassOpacity: true, backdrop: backdrop);

    private static SurfaceColors s_floatingColors;

    private static void ShowBehindFloating()
    {
        var (left, top, right, bottom) = Floating!.BackdropSides;
        Floating.View.ShowBehind(left, top, right, bottom);
    }

    /// <summary>The floating widget drawn as glass (its edges catch the light): Glass and Transparent, or any look over a blur.</summary>
    internal static bool GlassLook(WidgetStyle style) => style.Frosted || style.ColorMode == ColorMode.Glass;

    private static void CreateFloating()
    {
        Floating = new FloatingWidget();
        Floating.View.Spectrum = Spectrum;
        Wire(Floating.View);
        // Another page or window under it: the colors at its edges follow, the rest is drawn again only if its colors
        // change (most often they do not).
        Floating.BackdropChanged += (_, _) =>
        {
            ShowBehindFloating();
            if (FloatingColors(Settings.Floating, (Media.Current ?? s_lastMedia)?.CoverColor, Floating.Backdrop) != s_floatingColors)
                RefreshWidget();
        };
        Floating.View.MenuRequested += (_, _) => PopupMenu.ShowFloating();
        Floating.PlaceChanged += (_, place) =>
        {
            (Settings.FloatingX, Settings.FloatingY) = (place.X, place.Y);
            Settings.Save();
        };
        Floating.PlaceAt(Settings.FloatingX, Settings.FloatingY);
    }

    private static MediaSnapshot? s_lastMedia;

    /// <summary>
    /// Whether the taskbar behind the widget is dark: read on the screen by the widget once placed (any taskbar color,
    /// or a wallpaper through a see-through taskbar), else Windows' light / dark setting.
    /// </summary>
    internal static bool TaskbarIsDark => Taskbar?.BackdropIsDark ?? WindowsSettings.TaskbarIsDark;

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

    /// <summary>The rules of the Sources page changed: the player shown and the menus follow, saved a moment later.</summary>
    internal static void ApplySources()
    {
        Media.SetPolicy(Settings.SourcePolicy());
        RefreshWidget();
        SaveSoon();
    }

    internal static void SetWidgetEnabled(bool enabled)
    {
        Settings.WidgetEnabled = enabled;
        Settings.Save();
        RefreshWidget();
        StyleChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The OBS pages on or off (the server stays, for the widget of the Windows Widgets board).</summary>
    internal static void SetOverlayEnabled(bool enabled)
    {
        Settings.OverlayEnabled = enabled;
        Settings.Save();
        Overlay.PagesEnabled = enabled;
        UpdateOverlayLevels();
    }

    internal static void SetFloatingEnabled(bool enabled)
    {
        Settings.FloatingEnabled = enabled;
        Settings.Save();
        RefreshWidget();
        StyleChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void SetFloatingTopmost(bool onTop)
    {
        Settings.FloatingTopmost = onTop;
        Settings.Save();
        RefreshWidget();
        StyleChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void SetFloatingLocked(bool locked)
    {
        Settings.FloatingLocked = locked;
        Settings.Save();
        RefreshWidget();
        StyleChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The floating widget back at the bottom right of the main screen.</summary>
    internal static void ResetFloatingPosition()
    {
        (Settings.FloatingX, Settings.FloatingY) = (null, null);
        Settings.Save();
        Floating?.PlaceAt(null, null);
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
    /// Loads the Fluent theme (light/dark/system) the first time the window opens: until then the app does not hold the
    /// theme's resources. NOWPLAYING_THEME=Light|Dark forces one.
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
        // The widgets are not themed (they draw their own colors): they stay see-through.
        Floating?.KeepTransparent();
        foreach (var widget in Current.Windows.OfType<TaskbarWidget>())
            widget.KeepTransparent();
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
                RefreshWidget(); // the menu of the taskbar widget follows
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
