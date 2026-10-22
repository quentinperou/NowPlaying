using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NowPlaying.Controls;
using NowPlaying.Core;
using NowPlaying.Services;

namespace NowPlaying;

/// <summary>The pages of the app window, in the order of its menu.</summary>
public enum MainPage
{
    Sources,
    Widget,
    Floating,
    Overlay,
    General,
    Updates,
    About,
}

/// <summary>
/// The app window: the customisation of the widget and of the overlays (each with its own style, as the author asked),
/// and the general settings. Pages are built in code from <see cref="Ui"/> rows; a change applies and saves at once.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<MainPage, FrameworkElement> _pages = [];
    private WidgetView? _widgetPreview;
    private Border? _widgetStrip;
    private WidgetView? _floatingPreview;
    private Border? _floatingStage;
    private OverlayPreview? _overlayPreview;
    private Border? _overlayStage;
    private bool _sceneBackground;
    private OverlayLayout _overlayLayout = OverlayLayout.Bar;

    public MainWindow(MainPage page)
    {
        InitializeComponent();
        MicaBackdrop.Apply(this);
        foreach (var p in Enum.GetValues<MainPage>())
            Nav.Items.Add(NavItem(p));
        ShowPage(page);
        App.Media.Changed += MediaChanged;
        App.StyleChanged += StyleChanged;
        Closed += (_, _) =>
        {
            App.Media.Changed -= MediaChanged;
            App.StyleChanged -= StyleChanged;
        };
        MediaChanged(null, EventArgs.Empty);
    }

    /// <summary>Set when the language was changed: the window is built again, on this page.</summary>
    public MainPage? LanguageChanged { get; private set; }

    public void ShowPage(MainPage page)
    {
        Nav.SelectedIndex = (int)page;
    }

    private MainPage CurrentPage => (MainPage)Math.Max(0, Nav.SelectedIndex);

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var page = CurrentPage;
        if (!_pages.TryGetValue(page, out var content))
        {
            content = page switch
            {
                MainPage.Sources => BuildSourcesPage(),
                MainPage.Widget => BuildWidgetPage(),
                MainPage.Floating => BuildFloatingPage(),
                MainPage.Overlay => BuildOverlayPage(),
                MainPage.Updates => BuildUpdatesPage(),
                MainPage.About => BuildAboutPage(),
                _ => BuildGeneralPage(),
            };
            _pages[page] = content;
        }
        PageHost.Content = content;
        Scroller.ScrollToTop();
        RefreshPreviews();
    }

    private static ListBoxItem NavItem(MainPage page)
    {
        var (glyph, key) = page switch
        {
            MainPage.Sources => ("", "PageSources"),
            MainPage.Widget => ("", "PageWidget"),
            MainPage.Floating => ("", "PageFloating"),
            MainPage.Overlay => ("", "PageOverlay"),
            MainPage.Updates => ("", "PageUpdates"),
            MainPage.About => ("", "PageAbout"),
            _ => ("", "PageGeneral"),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
        row.Children.Add(new TextBlock { Text = glyph, FontSize = 16, Style = (Style)Application.Current.FindResource("IconText"), Margin = new Thickness(0, 0, 14, 0) });
        row.Children.Add(new TextBlock { Text = Strings.Get(key), FontSize = 14 });
        var item = new ListBoxItem { Content = row };
        System.Windows.Automation.AutomationProperties.SetName(item, Strings.Get(key));
        return item;
    }

    // ---------------------------------------------------------------- Sources page

    private StackPanel? _players;
    private CheckBox? _preferPlaying;
    private string? _playersShown;

    // Which player the widgets and the overlay show (the last one started, or an order of priority), how long a choice
    // made by right click stays, and the players seen, to sort or hide (the mock-up the author approved).
    private FrameworkElement BuildSourcesPage()
    {
        var settings = App.Settings;
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageSources")));

        page.Children.Add(Ui.Card("", Strings.Get("SourceMode"), Strings.Get("SourceModeHint"),
            Segmented.Of(
                [(SourceMode.Auto, Strings.Get("SourceModeAuto")), (SourceMode.Priority, Strings.Get("SourceModePriority"))],
                settings.SourceMode, mode =>
                {
                    settings.SourceMode = mode;
                    App.ApplySources();
                    ShowPlayers();
                })));
        _preferPlaying = Ui.Toggle(settings.PreferPlaying, on =>
        {
            settings.PreferPlaying = on;
            App.ApplySources();
        }, Strings.Get("PreferPlaying"));
        page.Children.Add(Ui.Card("", Strings.Get("PreferPlaying"), Strings.Get("PreferPlayingHint"), _preferPlaying));
        page.Children.Add(Ui.Card("", Strings.Get("PinMode"), Strings.Get("PinModeHint"),
            Ui.Choice([(PinMode.UntilNextStart, Strings.Get("PinModeUntilNextStart")), (PinMode.UntilChanged, Strings.Get("PinModeUntilChanged"))],
                settings.PinMode, mode =>
                {
                    settings.PinMode = mode;
                    App.ApplySources();
                })));

        page.Children.Add(Ui.Section(Strings.Get("SectionPlayers")));
        _players = new StackPanel();
        page.Children.Add(_players);

        // A browser tells Windows of one tab only (the last one that made a sound).
        var info = new DockPanel();
        var infoIcon = new TextBlock { Text = "", Style = (Style)Application.Current.FindResource("IconText"), Margin = new Thickness(0, 1, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(infoIcon, Dock.Left);
        info.Children.Add(infoIcon);
        info.Children.Add(new TextBlock { Text = Strings.Get("BrowsersOneTab"), TextWrapping = TextWrapping.Wrap });
        page.Children.Add(new Border { Style = (Style)Application.Current.FindResource("InfoBar"), Child = info, Margin = new Thickness(0, 8, 0, 0) });

        ShowPlayers();
        return page;
    }

    /// <summary>The rows of the players seen, built again when what they show changed (not at each position report).</summary>
    private void ShowPlayers()
    {
        if (_players is null || _preferPlaying is null)
            return;
        var settings = App.Settings;
        bool priority = settings.SourceMode == SourceMode.Priority;
        _preferPlaying.IsEnabled = priority;

        var present = App.Media.Players;
        var rows = new List<(KnownPlayer Player, string State, bool Playing)>();
        foreach (var player in settings.Players)
        {
            var sessions = present.Where(s => string.Equals(s.AppId, player.AppId, StringComparison.OrdinalIgnoreCase)).ToList();
            var playing = sessions.FirstOrDefault(s => s.IsPlaying);
            string state = player.Hidden ? Strings.Get("PlayerHidden")
                : playing is not null ? Join(Strings.Get("PlayerPlaying"), playing.Title)
                : sessions.Count > 0 ? Join(Strings.Get("PlayerPaused"), sessions[0].Title)
                : Strings.Format("PlayerSeen", player.LastSeen.ToString("M", System.Globalization.CultureInfo.CurrentUICulture));
            rows.Add((player, state, playing is not null && !player.Hidden));
        }

        string shown = $"{priority}|" + string.Join("|", rows.Select(r => $"{r.Player.AppId}/{r.Player.Name}/{r.Player.Hidden}/{r.State}/{r.Playing}"));
        if (shown == _playersShown)
            return;
        _playersShown = shown;

        _players.Children.Clear();
        if (rows.Count == 0)
        {
            _players.Children.Add(Ui.Card("", Strings.Get("NoPlayers"), Strings.Get("NoPlayersHint")));
            return;
        }
        int rank = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var (player, state, isPlaying) = rows[i];
            _players.Children.Add(PlayerRow(player, player.Hidden ? null : ++rank, state, isPlaying, priority,
                canUp: i > 0, canDown: i < rows.Count - 1));
        }
        _players.Children.Add(new TextBlock { Text = Strings.Get("PlayersHint"), Style = (Style)Application.Current.FindResource("CardDescription"), Margin = new Thickness(2, 4, 0, 0) });

        static string Join(string state, string title) => string.IsNullOrWhiteSpace(title) ? state : $"{state} · {title}";
    }

    // One player: its rank, a tile with its initial, its name and state, up / down, shown or not.
    private Border PlayerRow(KnownPlayer player, int? rank, string state, bool isPlaying, bool priority, bool canUp, bool canDown)
    {
        string name = player.Name.Length > 0 ? player.Name : player.AppId;
        var dock = new DockPanel();

        var rankText = new TextBlock
        {
            Text = !priority ? "" : rank is { } r ? r.ToString() : "–",
            Width = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        };
        rankText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        DockPanel.SetDock(rankText, Dock.Left);
        dock.Children.Add(rankText);

        var tile = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 14, 0),
            Background = new SolidColorBrush(TileColor(name)),
            Child = new TextBlock
            {
                Text = name[..1].ToUpperInvariant(), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);

        var show = Ui.Toggle(!player.Hidden, on =>
        {
            player.Hidden = !on;
            App.ApplySources();
            ShowPlayers();
        }, Strings.Format("PlayerShow", name));
        show.VerticalAlignment = VerticalAlignment.Center;
        show.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(show, Dock.Right);
        dock.Children.Add(show);

        // The order only matters in the priority mode (greyed otherwise, still shown).
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        arrows.Children.Add(Arrow("", Strings.Format("PlayerUp", name), priority && !player.Hidden && canUp, () => Move(player, -1)));
        arrows.Children.Add(Arrow("", Strings.Format("PlayerDown", name), priority && !player.Hidden && canDown, () => Move(player, +1)));
        DockPanel.SetDock(arrows, Dock.Right);
        dock.Children.Add(arrows);

        var texts = Ui.Texts(name, state);
        if (isPlaying && texts.Children[^1] is TextBlock stateText)
            stateText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        dock.Children.Add(texts);

        if (player.Hidden)
        {
            tile.Opacity = 0.45;
            texts.Opacity = 0.55;
        }
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Child = dock, Padding = new Thickness(12, 10, 16, 10) };
    }

    private static Button Arrow(string glyph, string automationName, bool enabled, Action click)
    {
        var button = new Button
        {
            // The icon keeps its color in a disabled button of the Fluent theme: greyed here.
            Content = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("IconText"), FontSize = 12, Opacity = enabled ? 1 : 0.35 },
            Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => click();
        return button;
    }

    // One place up or down in the order of priority.
    private void Move(KnownPlayer player, int by)
    {
        var players = App.Settings.Players;
        int from = players.IndexOf(player), to = from + by;
        if (from < 0 || to < 0 || to >= players.Count)
            return;
        (players[from], players[to]) = (players[to], players[from]);
        App.ApplySources();
        ShowPlayers();
    }

    // The color of a tile, from its name (the same at every start).
    private static Color TileColor(string name)
    {
        Color[] palette =
        [
            Color.FromRgb(0x1D, 0xB9, 0x54), Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0x7C, 0x3A, 0xED),
            Color.FromRgb(0xDB, 0x27, 0x77), Color.FromRgb(0x08, 0x91, 0xB2), Color.FromRgb(0xDC, 0x26, 0x26), Color.FromRgb(0x4F, 0x46, 0xE5),
        ];
        int sum = 0;
        foreach (char c in name.ToLowerInvariant())
            sum = unchecked(sum * 31 + c);
        return palette[(int)((uint)sum % palette.Length)];
    }

    // ---------------------------------------------------------------- Taskbar page

    private FrameworkElement BuildWidgetPage()
    {
        var style = App.Settings.Widget;
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageWidget")));

        page.Children.Add(Ui.Card("", Strings.Get("WidgetShow"), Strings.Get("WidgetShowHint"),
            Synced(() => App.Settings.WidgetEnabled, Ui.Toggle(App.Settings.WidgetEnabled, App.SetWidgetEnabled, Strings.Get("WidgetShow")))));

        // Live preview on a strip of taskbar, in the colors of the real one.
        _widgetPreview = new WidgetView { Spectrum = App.Spectrum, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 4, 0, 4) };
        App.Wire(_widgetPreview);
        _widgetStrip = new Border { Height = 48, CornerRadius = new CornerRadius(6), Child = _widgetPreview, Padding = new Thickness(4, 0, 4, 0) };
        var previewLabel = new TextBlock { Text = Strings.Get("WidgetPreviewHint"), Style = (Style)Application.Current.FindResource("CardDescription"), Margin = new Thickness(0, 0, 0, 10) };
        var preview = new StackPanel();
        preview.Children.Add(previewLabel);
        preview.Children.Add(_widgetStrip);
        page.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Child = preview, Padding = new Thickness(16, 12, 16, 16) });

        page.Children.Add(Ui.Section(Strings.Get("SectionPlace")));
        page.Children.Add(Ui.Card("", Strings.Get("WidgetPosition"), Strings.Get("WidgetPositionHint"),
            Segmented.Of(
                [(WidgetPosition.Left, Strings.Get("PositionLeft")), (WidgetPosition.NearClock, Strings.Get("PositionNearClock"))],
                style.Position, p => { style.Position = p; App.ApplyStyles(); })));
        page.Children.Add(Ui.Card("", Strings.Get("WidgetOffset"), Strings.Get("WidgetOffsetHint"),
            Ui.Slider(0, 400, style.Offset, 0, "px", v => { style.Offset = v; App.ApplyStyles(); }, Strings.Get("WidgetOffset"))));

        // The weather of Windows takes the left of the taskbar. Windows lets no app turn it off (the setting is
        // protected): its Settings page, where the user turns "Widgets" off, and the widget takes its place.
        _weatherText = Ui.Description("");
        var weatherTexts = Ui.Texts(Strings.Get("WindowsWeather"));
        weatherTexts.Children.Add(_weatherText);
        page.Children.Add(new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Child = Ui.Row("", weatherTexts,
                Ui.Button(Strings.Get("OpenTaskbarSettings"), () => OpenUrl("ms-settings:taskbar"), glyph: "")),
        });
        ShowWeatherState();

        page.Children.Add(Ui.Section(Strings.Get("SectionStyle")));
        var editor = new StyleEditor(style, new WidgetStyle(), overlay: false,
            [
                (Strings.Get("ElementCover"), () => style.ShowCover, v => style.ShowCover = v),
                (Strings.Get("ElementArtist"), () => style.ShowArtist, v => style.ShowArtist = v),
                (Strings.Get("ElementVisualizer"), () => style.ShowVisualizer, v => style.ShowVisualizer = v),
                (Strings.Get("ElementTimes"), () => style.ShowTimes, v => style.ShowTimes = v),
                (Strings.Get("ElementControls"), () => style.ShowControls, v => style.ShowControls = v),
            ], withVisualizer: false);
        editor.Changed += (_, _) => App.ApplyStyles();
        page.Children.Add(editor);

        page.Children.Add(Ui.Section(Strings.Get("SectionBehavior")));
        page.Children.Add(Ui.Card("", Strings.Get("StyleVisualizer"), Strings.Get("StyleVisualizerHint"),
            Segmented.Of(
                [(VisualizerMode.Spectrum, Strings.Get("VisualizerSpectrum")), (VisualizerMode.Animation, Strings.Get("VisualizerAnimation"))],
                style.Visualizer, v => { style.Visualizer = v; App.ApplyStyles(); })));
        page.Children.Add(Ui.Card("", Strings.Get("WidgetIdle"), Strings.Get("WidgetIdleHint"),
            Ui.Choice([(IdleMode.Hide, Strings.Get("IdleHide")), (IdleMode.KeepLast, Strings.Get("IdleKeepLast"))],
                style.Idle, v => { style.Idle = v; App.ApplyStyles(); })));
        page.Children.Add(Ui.Card("", Strings.Get("WidgetSources"), Strings.Get("WidgetSourcesHint"),
            Ui.Button(Strings.Get("PageSources"), () => ShowPage(MainPage.Sources))));
        return page;
    }

    // ---------------------------------------------------------------- Floating widget page

    private FrameworkElement BuildFloatingPage()
    {
        var style = App.Settings.Floating;
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageFloating")));

        page.Children.Add(Ui.Card("", Strings.Get("FloatingShow"), Strings.Get("FloatingShowHint"),
            Synced(() => App.Settings.FloatingEnabled, Ui.Toggle(App.Settings.FloatingEnabled, App.SetFloatingEnabled, Strings.Get("FloatingShow")))));

        // Live preview over a picture: the widget floats over any window.
        _floatingPreview = new WidgetView { Spectrum = App.Spectrum, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        App.Wire(_floatingPreview);
        _floatingStage = new Border { Height = 96, CornerRadius = new CornerRadius(6), Child = _floatingPreview, ClipToBounds = true };
        var preview = new StackPanel();
        preview.Children.Add(new TextBlock { Text = Strings.Get("FloatingPreviewHint"), Style = (Style)Application.Current.FindResource("CardDescription"), Margin = new Thickness(0, 0, 0, 10) });
        preview.Children.Add(_floatingStage);
        page.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Child = preview, Padding = new Thickness(16, 12, 16, 16) });

        page.Children.Add(Ui.Section(Strings.Get("SectionPlace")));
        page.Children.Add(Ui.Card("", Strings.Get("FloatingPosition"), Strings.Get("FloatingPositionHint"),
            Ui.Button(Strings.Get("FloatingReset"), App.ResetFloatingPosition, glyph: "")));
        page.Children.Add(Ui.Card("", Strings.Get("FloatingTopmost"), Strings.Get("FloatingTopmostHint"),
            Synced(() => App.Settings.FloatingTopmost, Ui.Toggle(App.Settings.FloatingTopmost, App.SetFloatingTopmost, Strings.Get("FloatingTopmost")))));
        page.Children.Add(Ui.Card("", Strings.Get("FloatingLocked"), Strings.Get("FloatingLockedHint"),
            Synced(() => App.Settings.FloatingLocked, Ui.Toggle(App.Settings.FloatingLocked, App.SetFloatingLocked, Strings.Get("FloatingLocked")))));

        page.Children.Add(Ui.Section(Strings.Get("SectionStyle")));
        // The shadow: on or off, and how dark (the slider is greyed while it is off).
        var density = Ui.Slider(10, 100, style.ShadowOpacity, () => WidgetStyle.FloatingLook(style.ColorMode, style.Blur).ShadowOpacity, "%",
            v => { style.ShadowOpacity = v; App.ApplyStyles(); }, Strings.Get("FloatingShadow"), 160);
        density.IsEnabled = style.Shadow;
        var shadow = new StackPanel { Orientation = Orientation.Horizontal };
        shadow.Children.Add(density);
        var shadowToggle = Ui.Toggle(style.Shadow, on =>
        {
            style.Shadow = on;
            density.IsEnabled = on;
            App.ApplyStyles();
        }, Strings.Get("FloatingShadow"));
        shadow.Children.Add(Spaced(shadowToggle));
        // How much of Windows' blur shows behind it (its strongest is Windows'; less mixes the sharp background in).
        var blurStrength = Ui.Slider(0, 100, style.BlurStrength, () => WidgetStyle.FloatingLook(style.ColorMode, style.Blur).BlurStrength, "%",
            v => { style.BlurStrength = v; App.ApplyStyles(); }, Strings.Get("FloatingBlur"));
        var blurCard = Ui.Card("", Strings.Get("FloatingBlur"), Strings.Get("FloatingBlurHint"), blurStrength);
        var editor = new StyleEditor(style, WidgetStyle.DefaultFloating(), overlay: true,
            [
                (Strings.Get("ElementCover"), () => style.ShowCover, v => style.ShowCover = v),
                (Strings.Get("ElementArtist"), () => style.ShowArtist, v => style.ShowArtist = v),
                (Strings.Get("ElementVisualizer"), () => style.ShowVisualizer, v => style.ShowVisualizer = v),
                (Strings.Get("ElementTimes"), () => style.ShowTimes, v => style.ShowTimes = v),
                (Strings.Get("ElementControls"), () => style.ShowControls, v => style.ShowControls = v),
            ], withVisualizer: false, glassOpacity: true,
            blur: (() => style.Blur, on => style.Blur = on),
            defaultOpacity: () => WidgetStyle.FloatingLook(style.ColorMode, style.Blur).Opacity,
            afterColor: new StackPanel { Children = { blurCard, Ui.Card("", Strings.Get("FloatingShadow"), Strings.Get("FloatingShadowHint"), shadow) } });
        editor.Changed += (_, _) => App.ApplyStyles();
        // Each look starts from its own opacity and shadow (asked by the author): shown on their rows at once.
        editor.LookChanged += (_, _) =>
        {
            style.ApplyFloatingLook();
            editor.ShowOpacity();
            Ui.ShowToggle(shadowToggle, style.Shadow);
            density.IsEnabled = style.Shadow;
            Ui.SetSlider(density, style.ShadowOpacity);
            Ui.SetSlider(blurStrength, style.BlurStrength);
        };
        page.Children.Add(editor);

        page.Children.Add(Ui.Section(Strings.Get("SectionBehavior")));
        page.Children.Add(Ui.Card("", Strings.Get("StyleVisualizer"), Strings.Get("StyleVisualizerHint"),
            Segmented.Of(
                [(VisualizerMode.Spectrum, Strings.Get("VisualizerSpectrum")), (VisualizerMode.Animation, Strings.Get("VisualizerAnimation"))],
                style.Visualizer, v => { style.Visualizer = v; App.ApplyStyles(); })));
        page.Children.Add(Ui.Card("", Strings.Get("WidgetIdle"), Strings.Get("WidgetIdleHint"),
            Ui.Choice([(IdleMode.Hide, Strings.Get("IdleHide")), (IdleMode.KeepLast, Strings.Get("IdleKeepLast"))],
                style.Idle, v => { style.Idle = v; App.ApplyStyles(); })));
        return page;
    }

    // ---------------------------------------------------------------- Overlay page

    private FrameworkElement BuildOverlayPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageOverlay")));

        if (App.Overlay.Error is { } error)
            page.Children.Add(new Border
            {
                Style = (Style)Application.Current.FindResource("WarningBar"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock { Text = Strings.Format("OverlayServerError", App.Settings.OverlayPort, error), TextWrapping = TextWrapping.Wrap },
            });

        // Off (the default), OBS gets nothing; its settings stay shown, as on the other pages.
        var content = new StackPanel();
        page.Children.Add(Ui.Card("", Strings.Get("OverlayEnabled"), Strings.Get("OverlayEnabledHint"),
            Ui.Toggle(App.Settings.OverlayEnabled, App.SetOverlayEnabled, Strings.Get("OverlayEnabled"))));
        page.Children.Add(content);

        var layoutHost = new ContentControl();
        content.Children.Add(Ui.Card("", Strings.Get("OverlayLayout"), Strings.Get("OverlayLayoutHint"),
            Segmented.Of(
                [(OverlayLayout.Bar, Strings.Get("LayoutBar")), (OverlayLayout.Card, Strings.Get("LayoutCard"))],
                _overlayLayout, layout =>
                {
                    _overlayLayout = layout;
                    layoutHost.Content = BuildOverlayLayout(layout);
                    RefreshPreviews();
                })));
        layoutHost.Content = BuildOverlayLayout(_overlayLayout);
        content.Children.Add(layoutHost);

        // The port of the pages (http://localhost:port/...), for when another program already uses it.
        var settings = App.Settings;
        content.Children.Add(Ui.Section(Strings.Get("SectionAdvanced")));
        var port = new TextBox { Width = 90, Text = settings.OverlayPort.ToString(), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(port, Strings.Get("OverlayPort"));
        var portRow = new StackPanel { Orientation = Orientation.Horizontal };
        portRow.Children.Add(port);
        portRow.Children.Add(Spaced(Ui.Button(Strings.Get("Apply"), () =>
        {
            if (int.TryParse(port.Text, out int value) && value is >= 1024 and <= 65535 && value != settings.OverlayPort)
            {
                settings.OverlayPort = value;
                settings.Save();
                App.RestartOverlayServer();
                // Built again, with the new addresses.
                _pages.Remove(MainPage.Overlay);
                Dispatcher.BeginInvoke(() => Nav_SelectionChanged(Nav, null!));
            }
            port.Text = settings.OverlayPort.ToString();
        })));
        content.Children.Add(Ui.Card("", Strings.Get("OverlayPort"), Strings.Get("OverlayPortHint"), portRow));
        return page;
    }

    // The rows of one layout: its preview, its address, its style.
    private FrameworkElement BuildOverlayLayout(OverlayLayout layout)
    {
        var style = App.Settings.Overlay(layout);
        var panel = new StackPanel();

        _overlayPreview = new OverlayPreview { Spectrum = App.Spectrum };
        _overlayStage = new Border { Height = 210, CornerRadius = new CornerRadius(6), Child = _overlayPreview, ClipToBounds = true };
        var sceneToggle = Ui.LabeledToggle(Strings.Get("OverlaySceneBackground"), _sceneBackground, on => { _sceneBackground = on; RefreshPreviews(); });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(sceneToggle, Dock.Right);
        header.Children.Add(sceneToggle);
        header.Children.Add(new TextBlock { Text = Strings.Get("OverlayPreviewHint"), Style = (Style)Application.Current.FindResource("CardDescription"), VerticalAlignment = VerticalAlignment.Center });
        var preview = new StackPanel();
        preview.Children.Add(header);
        preview.Children.Add(_overlayStage);
        panel.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Child = preview, Padding = new Thickness(16, 12, 16, 16) });

        string url = OverlayServer.Url(App.Settings.OverlayPort, layout);
        var (width, height) = style.SourceSize(layout);
        var address = new StackPanel { Orientation = Orientation.Horizontal };
        var urlBox = new TextBox { Text = url, IsReadOnly = true, Width = 280, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(urlBox, Strings.Get("OverlayAddress"));
        address.Children.Add(urlBox);
        address.Children.Add(Spaced(Ui.Button(Strings.Get("Copy"), () => CopyText(url), accent: true, glyph: "")));
        address.Children.Add(Spaced(Ui.Button(Strings.Get("OpenInBrowser"), () => OpenUrl(url), glyph: "")));
        panel.Children.Add(Ui.Card("", Strings.Get("OverlayAddress"), Strings.Format("OverlayAddressHint", width, height), address));

        panel.Children.Add(Ui.Section(Strings.Get("SectionStyle")));
        var elements = layout == OverlayLayout.Bar
            ? new List<(string, Func<bool>, Action<bool>)>
            {
                (Strings.Get("ElementCover"), () => style.ShowCover, v => style.ShowCover = v),
                (Strings.Get("ElementArtist"), () => style.ShowArtist, v => style.ShowArtist = v),
                (Strings.Get("ElementProgress"), () => style.ShowProgress, v => style.ShowProgress = v),
                (Strings.Get("ElementTimes"), () => style.ShowTimes, v => style.ShowTimes = v),
                (Strings.Get("ElementVisualizer"), () => style.ShowVisualizer, v => style.ShowVisualizer = v),
            }
            : [
                (Strings.Get("ElementCover"), () => style.ShowCover, v => style.ShowCover = v),
                (Strings.Get("ElementArtist"), () => style.ShowArtist, v => style.ShowArtist = v),
                (Strings.Get("ElementProgress"), () => style.ShowProgress, v => style.ShowProgress = v),
                (Strings.Get("ElementTimes"), () => style.ShowTimes, v => style.ShowTimes = v),
            ];
        // In OBS, "Glass" is a veil: a Browser Source is drawn alone, it cannot blur the scene behind it.
        var editor = new StyleEditor(style, OverlayStyle.Default(layout), overlay: true, elements, withVisualizer: layout == OverlayLayout.Bar,
            colorHint: "OverlayColorHint");
        editor.Changed += (_, _) => App.ApplyStyles();
        panel.Children.Add(editor);
        return panel;
    }

    private static FrameworkElement Spaced(FrameworkElement element)
    {
        element.Margin = new Thickness(8, 0, 0, 0);
        return element;
    }

    // ---------------------------------------------------------------- General page

    private FrameworkElement BuildGeneralPage()
    {
        var settings = App.Settings;
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageGeneral")));

        page.Children.Add(Ui.Card("", Strings.Get("StartWithWindows"), Strings.Get("StartWithWindowsHint"),
            Ui.Toggle(WindowsSettings.StartWithWindows, on => WindowsSettings.StartWithWindows = on, Strings.Get("StartWithWindows"))));

        page.Children.Add(Ui.Card("", Strings.Get("Language"), null,
            Ui.Choice(
                [(AppSettings.SystemLanguage, Strings.Get("LanguageSystem")), ("en", "English"), ("fr", "Français")],
                settings.Language, language =>
                {
                    if (language == settings.Language)
                        return;
                    settings.Language = language;
                    settings.Save();
                    LanguageChanged = CurrentPage;
                    Dispatcher.BeginInvoke(Close);
                })));

        page.Children.Add(Ui.Card("", Strings.Get("Theme"), null,
            Ui.Choice(
                [(AppTheme.System, Strings.Get("ThemeSystem")), (AppTheme.Light, Strings.Get("ThemeLight")), (AppTheme.Dark, Strings.Get("ThemeDark"))],
                settings.Theme, theme =>
                {
                    settings.Theme = theme;
                    settings.Save();
                    App.ApplyTheme(theme);
                })));

        page.Children.Add(Ui.Card("", Strings.Get("MenuQuit"), Strings.Get("QuitDescription"),
            Ui.Button(Strings.Get("MenuQuit"), App.Quit)));

        return page;
    }

    // ---------------------------------------------------------------- Updates page

    // As in ScreenClip and PageCompose: one card, the state and the check / install button first, then the check at
    // startup and the channel.
    private TextBlock? _updateStatus;
    private TextBlock? _updateNotes;
    private ProgressBar? _updateProgress;
    private Button? _updateButton;
    private bool _updating;

    private FrameworkElement BuildUpdatesPage()
    {
        var settings = App.Settings;
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageUpdates")));

        _updateStatus = Ui.Description("");
        _updateNotes = Ui.Description("");
        var notes = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Strings.Get("UpdateWhatsNew")));
        notes.Click += (_, _) =>
        {
            if (Updater.Available is { } release)
                Updater.OpenReleasePage(release);
        };
        _updateNotes.Inlines.Clear();
        _updateNotes.Inlines.Add(notes);
        _updateProgress = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 8, 0, 2), Visibility = Visibility.Collapsed };
        _updateButton = new Button { MinWidth = 220 };
        _updateButton.Click += async (_, _) =>
        {
            if (Updater.Available is { } release)
                await InstallUpdateAsync(release);
            else
                await CheckForUpdatesAsync();
        };
        var texts = Ui.Texts(Strings.Get("PageUpdates"));
        texts.Children.Add(_updateStatus);
        texts.Children.Add(_updateNotes);
        texts.Children.Add(_updateProgress);

        var atStartup = Ui.Toggle(settings.CheckForUpdatesAtStartup, on =>
        {
            settings.CheckForUpdatesAtStartup = on;
            settings.Save();
        }, Strings.Get("UpdateCheckAtStartup"));
        var channel = Ui.Choice(
            [(UpdateChannel.Stable, Strings.Get("UpdateChannelStable")), (UpdateChannel.Beta, Strings.Get("UpdateChannelBeta"))],
            settings.UpdateChannel, async value =>
            {
                if (value == settings.UpdateChannel)
                    return;
                settings.UpdateChannel = value;
                settings.Save();
                // What is offered depends on the channel: look again.
                if (!_updating)
                    await CheckForUpdatesAsync();
            }, 220);
        System.Windows.Automation.AutomationProperties.SetName(channel, Strings.Get("UpdateChannel"));

        page.Children.Add(Ui.Group(
            Ui.Row("", texts, _updateButton),
            Ui.Row("", Strings.Get("UpdateCheckAtStartup"), Strings.Get("UpdateCheckAtStartupDescription"), atStartup),
            Ui.Row("", Strings.Get("UpdateChannel"), Strings.Get("UpdateChannelDescription"), channel)));

        Updater.Checked += UpdaterChecked;
        Closed += (_, _) => Updater.Checked -= UpdaterChecked;
        ShowUpdateState();
        return page;
    }

    private void UpdaterChecked(object? sender, EventArgs e) => ShowUpdateState();

    /// <summary>Shows what the last check found, or <paramref name="status"/> (progress, error) instead.</summary>
    private void ShowUpdateState(string? status = null)
    {
        if (_updateStatus is null || _updateNotes is null || _updateButton is null)
            return;
        var available = Updater.Available;
        _updateStatus.Text = status
            ?? (available is not null ? Strings.Format("UpdateStatusAvailable", Updater.CurrentVersion, available.Version)
                : Updater.HasChecked ? Strings.Format("UpdateStatusUpToDate", Updater.CurrentVersion)
                : Strings.Format("UpdateStatusIdle", Updater.CurrentVersion));
        _updateNotes.Visibility = available is not null && status is null ? Visibility.Visible : Visibility.Collapsed;
        _updateButton.Content = available is null ? Strings.Get("UpdateCheck") : Strings.Get("UpdateInstall");
        if (available is null)
            _updateButton.ClearValue(StyleProperty);
        else
            _updateButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        _updateButton.IsEnabled = !_updating;
    }

    private async Task CheckForUpdatesAsync()
    {
        _updating = true;
        ShowUpdateState(Strings.Get("UpdateStatusChecking"));
        string? error = null;
        try
        {
            await Updater.CheckAsync(App.Settings.UpdateChannel);
        }
        catch (Exception ex)
        {
            error = Strings.Format("UpdateStatusFailed", Updater.CurrentVersion, Updater.Describe(ex));
        }
        _updating = false;
        ShowUpdateState(error);
    }

    private async Task InstallUpdateAsync(ReleaseInfo release)
    {
        if (_updateProgress is null || _updateStatus is null)
            return;
        _updating = true;
        ShowUpdateState(Strings.Get("UpdateDownloading"));
        _updateProgress.Value = 0;
        _updateProgress.IsIndeterminate = false;
        _updateProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(p =>
        {
            _updateProgress.Value = p;
            if (p < 1)
                return;
            // Downloaded: the installer now waits for the UAC prompt, then installs.
            _updateProgress.IsIndeterminate = true;
            _updateStatus.Text = Strings.Get("UpdateStarting");
        });

        string status;
        try
        {
            if (await Updater.InstallAsync(release, progress))
                return; // the app is closing, the installer takes over
            status = Strings.Get("UpdateCancelled");
        }
        catch (Exception ex)
        {
            status = Strings.Format("ErrorUpdate", Updater.Describe(ex));
        }
        _updating = false;
        _updateProgress.Visibility = Visibility.Collapsed;
        ShowUpdateState(status);
    }

    // ---------------------------------------------------------------- About page

    // As in ScreenClip: the app, its version and its links, then quitting it (it has no window of its own to close).
    private FrameworkElement BuildAboutPage()
    {
        var page = new StackPanel();
        page.Children.Add(Ui.PageTitle(Strings.Get("PageAbout")));

        var about = new DockPanel();
        var icon = new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/NowPlaying.png")),
            Width = 40, Height = 40, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(icon, Dock.Left);
        about.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = "NowPlaying", FontWeight = FontWeights.SemiBold });
        texts.Children.Add(Ui.Description(Strings.Format("AboutVersion", Updater.CurrentVersion)));
        var developedBy = Ui.Description("");
        developedBy.Inlines.Clear();
        developedBy.Inlines.Add(new System.Windows.Documents.Run(Strings.Get("AboutDevelopedBy")));
        developedBy.Inlines.Add(Link("QuentinPerou", "https://github.com/quentinperou"));
        texts.Children.Add(developedBy);
        var source = Ui.Description("");
        source.Inlines.Clear();
        source.Inlines.Add(Link(Strings.Get("AboutSourceCode"), "https://github.com/quentinperou/NowPlaying"));
        texts.Children.Add(source);
        about.Children.Add(texts);
        page.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Child = about });
        return page;
    }

    private static System.Windows.Documents.Hyperlink Link(string text, string url)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) => OpenUrl(e.Uri.AbsoluteUri);
        return link;
    }

    // ---------------------------------------------------------------- Live preview

    private void MediaChanged(object? sender, EventArgs e)
    {
        var media = App.Media.Current;
        NowLine.Text = media is null ? Strings.Get("NothingPlaying") : $"{media.Title} · {media.Artist}";
        RefreshPreviews();
    }

    private void StyleChanged(object? sender, EventArgs e)
    {
        RefreshPreviews();
        ShowWeatherState();
        // Changed elsewhere too (the menu of a widget, of the notification icon): the switches of the pages follow.
        foreach (var (toggle, isOn) in _synced)
            Ui.ShowToggle(toggle, isOn());
    }

    private readonly List<(CheckBox Toggle, Func<bool> IsOn)> _synced = [];

    // A switch whose setting can also change outside this window.
    private CheckBox Synced(Func<bool> isOn, CheckBox toggle)
    {
        _synced.Add((toggle, isOn));
        return toggle;
    }

    private TextBlock? _weatherText;

    private void ShowWeatherState()
    {
        if (_weatherText is not null)
            _weatherText.Text = Strings.Get(App.Taskbar.WindowsWeatherShown == false ? "WindowsWeatherHidden" : "WindowsWeatherShown");
    }

    private void RefreshPreviews()
    {
        // Nothing plays: an example, so that the style can still be set.
        var media = App.Media.Current ?? new MediaSnapshot("", "", "", Strings.Get("NothingPlaying"), Strings.Get("NothingPlayingHint"), "",
            null, null, "", 0, null, false, TimeSpan.FromSeconds(44), DateTimeOffset.Now, TimeSpan.FromSeconds(214), 1, false, false, false, false);
        bool dark = App.TaskbarIsDark;
        if (_widgetPreview is not null && _widgetStrip is not null)
        {
            _widgetStrip.Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xEE, 0xF0, 0xF4));
            var style = App.Settings.Widget;
            _widgetPreview.ApplyStyle(style, SurfaceColors.Resolve(style, media?.CoverColor, dark, overlay: false));
            _widgetPreview.ShowMedia(media, idle: false);
        }
        if (_floatingPreview is not null && _floatingStage is not null)
        {
            _floatingStage.Background = Checkerboard.Scene();
            var style = App.Settings.Floating;
            _floatingPreview.ApplyStyle(style, App.FloatingColors(style, media?.CoverColor),
                App.GlassLook(style) ? style.CornerRadius : null);
            // The dusk of the picture around the preview: its colors at the edges of the glass.
            _floatingPreview.ShowBehind(new Rgb(0x5F, 0x48, 0x85), new Rgb(0x35, 0x32, 0x6B), new Rgb(0x5F, 0x48, 0x85), new Rgb(0xB3, 0x68, 0x89));
            _floatingPreview.ShowMedia(media, idle: false);
        }
        if (_overlayPreview is not null && _overlayStage is not null)
        {
            _overlayStage.Background = _sceneBackground ? Checkerboard.Scene() : Checkerboard.Create();
            _overlayPreview.Show(_overlayLayout, App.Settings.Overlay(_overlayLayout), media);
        }
    }

    // ---------------------------------------------------------------- Helpers

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is held by another app for a moment.
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Open failed: {ex.Message}");
        }
    }
}
