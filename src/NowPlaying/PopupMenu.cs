using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NowPlaying.Services;

namespace NowPlaying;

/// <summary>
/// The menus of the notification icon and of the widget (Fluent theme, light or dark). They are built each time they
/// open, so that they show the current players and language, and nothing of them stays in memory afterwards.
/// </summary>
internal static class PopupMenu
{
    private static ContextMenu? s_open;

    /// <summary>The menu of the notification icon.</summary>
    public static void ShowTray() => Show(() =>
    {
        var menu = new ContextMenu { MinWidth = 240 };
        menu.Items.Add(Item(Strings.Get("MenuOpen"), "", () => App.OpenMainWindow()));
        var widget = Item(Strings.Get("MenuShowWidget"), "", () => App.SetWidgetEnabled(!App.Settings.WidgetEnabled));
        widget.IsCheckable = true;
        widget.IsChecked = App.Settings.WidgetEnabled;
        menu.Items.Add(widget);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("MenuQuit"), "", App.Quit));
        return menu;
    });

    /// <summary>The menu of the widget (right click): the players known to Windows, to choose the one shown.</summary>
    public static void ShowWidget() => Show(() =>
    {
        var menu = new ContextMenu { MinWidth = 280 };
        var header = new MenuItem { Header = Strings.Get("MenuSource"), IsEnabled = false };
        menu.Items.Add(header);
        foreach (var source in App.Media.Sources)
        {
            var item = Item(source.AppName, "", () => App.Media.Pin(source.Id));
            item.IsCheckable = true;
            item.IsChecked = source.IsCurrent;
            item.InputGestureText = source.Title;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("MenuCustomize"), "", () => App.OpenMainWindow(MainPage.Widget)));
        menu.Items.Add(Item(Strings.Get("MenuQuit"), "", App.Quit));
        return menu;
    });

    private static void Show(Func<ContextMenu> build)
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
                pointer.X, pointer.Y, 1, 1, NativeMethods.SWP_NOACTIVATE);
        };
        host.Show();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(host).Handle);
        host.Activate();

        var menu = build();
        menu.PlacementTarget = host;
        menu.Placement = PlacementMode.MousePoint;
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
