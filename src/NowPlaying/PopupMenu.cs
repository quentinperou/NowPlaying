using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NowPlaying.Services;

namespace NowPlaying;

/// <summary>
/// The menus of the notification icon and of the widgets (Fluent theme, light or dark). They are built each time they
/// open, so that they show the current players and language, and nothing of them stays in memory afterwards.
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
        ShowSources(App.Media.Sources, App.Media.Pin, () => App.OpenMainWindow(MainPage.Floating), App.Quit, options: () =>
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
    /// </summary>
    public static void ShowSources(IReadOnlyList<MediaSource> sources, Action<string> pin, Action customize, Action quit, int? aboveY = null,
        Func<IEnumerable<MenuItem>>? options = null) => Show(() =>
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

        App.EnsureTheme();
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
        menu.PlacementTarget = host;
        // Above the taskbar: its bottom on the top of the taskbar, where the host is.
        menu.Placement = aboveY is null ? PlacementMode.MousePoint : PlacementMode.Top;
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

    private static MenuItem Item(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header };
        if (glyph.Length > 0)
            item.Icon = new TextBlock { Text = glyph, FontSize = 16, Style = (Style)Application.Current.FindResource("IconText") };
        item.Click += (_, _) => action();
        return item;
    }
}
