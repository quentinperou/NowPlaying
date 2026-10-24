using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using NowPlaying.Core;
using NowPlaying.Services;

namespace NowPlaying;

/// <summary>
/// The menus of the notification icon and of the widgets, drawn as Windows' own (Menus.xaml) in Windows' mode. They are
/// built each time they open, so that they show the current players and language, and nothing of them stays in memory
/// afterwards.
/// </summary>
internal static class PopupMenu
{
    // A check mark in the icon column: checkable items of the Fluent theme get a column of their own for it, which
    // shifted them from the icons of the other items.
    private const string Check = "";

    private static ContextMenu? s_open;

    /// <summary>The menu of the notification icon.</summary>
    public static void ShowTray() => Show(() =>
    {
        var menu = new ContextMenu { MinWidth = 240 };
        menu.Items.Add(Item(Strings.Get("MenuOpen"), "", () => App.OpenMainWindow()));
        menu.Items.Add(Item(Strings.Get("MenuHistory"), "", () => App.OpenMainWindow(MainPage.History)));
        menu.Items.Add(Item(Strings.Get("MenuShowWidget"), App.Settings.WidgetEnabled ? Check : "",
            () => App.SetWidgetEnabled(!App.Settings.WidgetEnabled)));
        menu.Items.Add(Item(Strings.Get("MenuShowFloating"), App.Settings.FloatingEnabled ? Check : "",
            () => App.SetFloatingEnabled(!App.Settings.FloatingEnabled)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("MenuQuit"), "", App.Quit));
        return menu;
    });

    /// <summary>The menu of the floating widget (right click): also above the windows or not, locked, hidden.</summary>
    public static void ShowFloating() =>
        ShowSources(App.Media.Sources, App.Media.Pin, () => App.OpenMainWindow(MainPage.Floating), App.Quit,
            copy: TrackText.Format(App.ShownMedia?.Title, App.ShownMedia?.Artist), history: () => App.OpenMainWindow(MainPage.History), options: () =>
        [
            Item(Strings.Get("FloatingTopmost"), App.Settings.FloatingTopmost ? Check : "",
                () => App.SetFloatingTopmost(!App.Settings.FloatingTopmost)),
            Item(Strings.Get("FloatingLocked"), App.Settings.FloatingLocked ? Check : "",
                () => App.SetFloatingLocked(!App.Settings.FloatingLocked)),
            Item(Strings.Get("MenuHideFloating"), "", () => App.SetFloatingEnabled(false)),
        ]);

    /// <summary>
    /// The menu of a widget: the players known to Windows, to choose the one shown, "Customize" and "Quit". Shown by
    /// the process that got the click (the app for the floating widget, the widget process for the one in the
    /// taskbar): only it may bring its menu to the front, and a menu not in front stays open when the user clicks
    /// elsewhere. <paramref name="aboveY"/>: the top of the taskbar, in screen pixels; the menu then opens above it,
    /// never over it (the taskbar may come over any window). <paramref name="options"/>: items of that widget.
    /// <paramref name="copy"/>: the track shown as "title – artist", offered to copy; <paramref name="history"/>: opens the
    /// History page (both asked by the author, October 24, 2026).
    /// </summary>
    public static void ShowSources(IReadOnlyList<MediaSource> sources, Action<string> pin, Action customize, Action quit, int? aboveY = null,
        Func<IEnumerable<MenuItem>>? options = null, string? copy = null, Action? history = null) => Show(() =>
    {
        var menu = new ContextMenu { MinWidth = 280 };
        menu.Items.Add(new MenuItem { Header = Strings.Get("MenuSource"), IsEnabled = false });
        foreach (var source in sources)
        {
            var item = Item(source.AppName, source.IsCurrent ? Check : "", () => pin(source.Id));
            item.InputGestureText = source.Title;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        // The track shown, and the tracks heard.
        if (!string.IsNullOrEmpty(copy))
            menu.Items.Add(Item(Strings.Get("MenuCopyTrack"), "", () => Copy(copy)));
        if (history is not null)
            menu.Items.Add(Item(Strings.Get("MenuHistory"), "", history));
        if (!string.IsNullOrEmpty(copy) || history is not null)
            menu.Items.Add(new Separator());
        if (options is not null)
        {
            foreach (var option in options())
                menu.Items.Add(option);
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item(Strings.Get("MenuCustomize"), "", customize));
        menu.Items.Add(Item(Strings.Get("MenuQuit"), "", quit));
        return menu;
    }, aboveY);

    private static void Show(Func<ContextMenu> build, int? aboveY = null)
    {
        if (s_open is { IsOpen: true })
        {
            s_open.IsOpen = false;
            return;
        }

        // A WPF menu of an app with no window in front loses the mouse capture as it opens, and closes at once. So the
        // menu belongs to a tiny invisible window under the pointer, activated first (a click on the icon or the widget
        // gives the app the right to come to the front), and closed with the menu.
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = 1,
            Height = 1,
            Title = "NowPlaying Menu",
        };
        host.SourceInitialized += (_, _) =>
        {
            NativeMethods.GetCursorPos(out var pointer);
            NativeMethods.SetWindowPos(new WindowInteropHelper(host).Handle, NativeMethods.HWND_TOPMOST,
                pointer.X, aboveY ?? pointer.Y, 1, 1, NativeMethods.SWP_NOACTIVATE);
        };
        host.Show();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(host).Handle);
        host.Activate();

        var menu = build();
        Dress(menu);
        menu.PlacementTarget = host;
        // Above the taskbar: its bottom on the top of the taskbar, where the host is (the room of the shadow leaves a gap,
        // as Windows' own menus). Else its corner at the pointer.
        menu.Placement = aboveY is null ? PlacementMode.MousePoint : PlacementMode.Top;
        menu.HorizontalOffset = -ShadowRoom;
        menu.VerticalOffset = aboveY is null ? -ShadowRoom : 0;
        // Not the popup itself: activating it would deactivate the host, which closes the menu.
        menu.Closed += (_, _) =>
        {
            s_open = null;
            host.Close();
            MemoryTrim.Soon();
        };
        s_open = menu;
        menu.IsOpen = true;
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard (another app may hold it a moment: tried again once).</summary>
    public static void Copy(string text)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }
    }

    // The margin of the menu around its border, for its shadow (Menus.xaml).
    private const int ShadowRoom = 12;

    // The look of Windows 11's menus, in Windows' mode (that of the taskbar), whatever the theme of the app window.
    private static void Dress(ContextMenu menu)
    {
        menu.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/NowPlaying;component/Menus.xaml", UriKind.Relative)));
        menu.Style = (Style)menu.Resources["WindowsMenu"];
        bool dark = WindowsSettings.TaskbarIsDark;
        (string Key, string Light, string Dark)[] colors =
        [
            ("MenuSurface", "#F9F9F9", "#2C2C2C"),
            ("MenuStroke", "#E3E3E3", "#424242"),
            ("MenuText", "#1B1B1B", "#FFFFFF"),
            ("MenuTextSecondary", "#5F5F5F", "#C5C5C5"),
            ("MenuTextDisabled", "#A0A0A0", "#7A7A7A"),
            ("MenuHover", "#EDEDED", "#383838"),
            ("MenuDivider", "#E5E5E5", "#404040"),
        ];
        foreach (var (key, light, darkColor) in colors)
            menu.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? darkColor : light));
    }

    private static MenuItem Item(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header };
        if (glyph.Length > 0)
            item.Icon = new TextBlock { Text = glyph, FontSize = 16, Style = (Style)Application.Current.FindResource("IconText") };
        item.Click += (_, _) => action();
        return item;
    }
}
