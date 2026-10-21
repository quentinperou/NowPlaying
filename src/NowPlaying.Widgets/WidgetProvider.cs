using Microsoft.Windows.Widgets.Providers;

namespace NowPlaying.Widgets;

/// <summary>
/// The NowPlaying widget for the Widgets board: Windows calls it when the widget is added, shown, resized, clicked or
/// removed; it sends the card again when what plays changes (only when its data differs).
/// </summary>
internal sealed class WidgetProvider : IWidgetProvider, IDisposable
{
    private readonly Dictionary<string, string> _widgets = []; // id → the data last sent
    private readonly object _lock = new();
    private readonly AppLink _link = new();
    private readonly Action _noMoreWidgets;

    public WidgetProvider(Action noMoreWidgets)
    {
        _noMoreWidgets = noMoreWidgets;
        // Widgets already on the board when this process starts (after a restart of the PC, an update).
        foreach (var info in WidgetManager.GetDefault().GetWidgetInfos() ?? [])
            _widgets[info.WidgetContext.Id] = "";
        Log.Write($"Provider ready, {_widgets.Count} widget(s) on the board");
        _link.Changed += () => SendAll(force: false);
        SendAll(force: true);
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        Log.Write($"Created {widgetContext.Id} ({widgetContext.Size})");
        lock (_lock)
            _widgets[widgetContext.Id] = "";
        Send(widgetContext.Id, force: true);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Log.Write($"Deleted {widgetId}");
        bool none;
        lock (_lock)
        {
            _widgets.Remove(widgetId);
            none = _widgets.Count == 0;
        }
        if (none)
            _noMoreWidgets();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        string verb = actionInvokedArgs.Verb;
        Log.Write($"Action {verb}");
        if (verb == "open")
            AppLink.StartApp();
        else if (verb is "playpause" or "next" or "previous")
            _ = _link.SendAsync(verb);
    }

    // A new size: the template picks its parts from $host.widgetSize, send it again.
    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs) =>
        Send(contextChangedArgs.WidgetContext.Id, force: true);

    public void Activate(WidgetContext widgetContext)
    {
        lock (_lock)
            _widgets.TryAdd(widgetContext.Id, "");
        Send(widgetContext.Id, force: true);
    }

    public void Deactivate(string widgetId)
    {
    }

    private void SendAll(bool force)
    {
        string[] ids;
        lock (_lock)
            ids = [.. _widgets.Keys];
        foreach (string id in ids)
            Send(id, force);
    }

    private void Send(string id, bool force)
    {
        string data = Card.Data(_link.State);
        lock (_lock)
        {
            if (!_widgets.TryGetValue(id, out string? last) || (!force && last == data))
                return;
            _widgets[id] = data;
        }
        try
        {
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) { Template = Card.Template, Data = data });
        }
        catch (Exception ex)
        {
            Log.Write($"UpdateWidget {id} failed: {ex.Message}");
        }
    }

    public void Dispose() => _link.Dispose();
}
