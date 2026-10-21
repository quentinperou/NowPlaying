using System.Text.Json.Serialization;

namespace NowPlaying.Core;

/// <summary>How the player shown is chosen (the Sources page).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceMode>))]
public enum SourceMode
{
    /// <summary>The last player that started playing (the author's first rule).</summary>
    Auto,
    /// <summary>In the order of the user's list of players.</summary>
    Priority,
}

/// <summary>How long a player chosen by hand (right click on a widget) stays.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinMode>))]
public enum PinMode
{
    UntilNextStart,
    UntilChanged,
}

/// <summary>
/// The rules of the Sources page. <paramref name="Order"/>: the apps (their media app id, without the "#2" of a second
/// session) from the first to the last; an app not in it comes after them. <paramref name="Hidden"/>: apps never shown.
/// </summary>
public sealed record SourcePolicy(SourceMode Mode, bool PreferPlaying, PinMode Pin, IReadOnlyList<string> Order, IReadOnlyCollection<string> Hidden)
{
    public static readonly SourcePolicy Default = new(SourceMode.Auto, true, PinMode.UntilNextStart, [], []);
}

/// <summary>
/// Which of the players known to Windows the widgets and the overlay show. By default (the author's rule, October 21,
/// 2026) the last one that started playing; a player chosen by hand (right click on the widget) stays until another one
/// starts playing. While nothing plays, the one shown stays (paused) as long as it exists. The Sources page may ask for
/// the order of a list instead, keep a choice by hand until another one, or hide players.
/// </summary>
public sealed class SourcePicker
{
    private readonly Dictionary<string, Entry> _entries = [];
    private SourcePolicy _policy = SourcePolicy.Default;
    private string? _pinned;
    private long _order;

    private sealed class Entry
    {
        public bool Playing;
        public long StartedOrder; // when it last started playing; 0: never seen playing
        public long SeenOrder;
    }

    /// <summary>The source shown, null when Windows knows no player (or only hidden ones).</summary>
    public string? Current { get; private set; }

    public IReadOnlyCollection<string> Sources => _entries.Keys;

    /// <summary>The app of a source id: "chrome#2", a second session of Chrome, is "chrome".</summary>
    public static string AppOf(string id)
    {
        int hash = id.LastIndexOf('#');
        return hash > 0 ? id[..hash] : id;
    }

    /// <summary>New rules (the Sources page changed). Returns true when <see cref="Current"/> changed.</summary>
    public bool SetPolicy(SourcePolicy policy)
    {
        _policy = policy;
        if (_pinned is not null && IsHidden(_pinned))
            _pinned = null;
        return Choose();
    }

    public bool IsHidden(string id)
    {
        string app = AppOf(id);
        return _policy.Hidden.Any(h => string.Equals(h, app, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A player appeared, or its playback state changed. Returns true when <see cref="Current"/> changed.</summary>
    public bool Update(string id, bool playing)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            entry = new Entry { SeenOrder = ++_order };
            _entries[id] = entry;
        }
        if (playing && !entry.Playing)
        {
            entry.StartedOrder = ++_order;
            // A player that starts takes over from one chosen by hand (unless the choice stays until another one).
            if (_pinned != id && _policy.Pin == PinMode.UntilNextStart)
                _pinned = null;
        }
        entry.Playing = playing;
        return Choose();
    }

    /// <summary>A player is gone (closed, or its page left). Returns true when <see cref="Current"/> changed.</summary>
    public bool Remove(string id)
    {
        _entries.Remove(id);
        if (_pinned == id)
            _pinned = null;
        return Choose();
    }

    /// <summary>The user chose this player. Returns true when <see cref="Current"/> changed.</summary>
    public bool Pin(string id)
    {
        if (!_entries.ContainsKey(id) || IsHidden(id))
            return false;
        _pinned = id;
        return Choose();
    }

    public bool IsPlaying(string id) => _entries.TryGetValue(id, out var e) && e.Playing;

    // The place of a source in the user's list; after the list when it is not in it.
    private int Rank(string id)
    {
        string app = AppOf(id);
        for (int i = 0; i < _policy.Order.Count; i++)
            if (string.Equals(_policy.Order[i], app, StringComparison.OrdinalIgnoreCase))
                return i;
        return int.MaxValue;
    }

    private bool Choose()
    {
        var shown = _entries.Where(e => !IsHidden(e.Key)).ToList();
        string? next;
        if (_pinned is not null && _entries.ContainsKey(_pinned))
            next = _pinned;
        else if (_policy.Mode == SourceMode.Priority)
        {
            // The first of the list; among those that play only, if what plays goes first.
            var pool = _policy.PreferPlaying && shown.Any(e => e.Value.Playing) ? shown.Where(e => e.Value.Playing) : shown;
            next = pool.OrderBy(e => Rank(e.Key)).ThenByDescending(e => e.Value.StartedOrder).ThenByDescending(e => e.Value.SeenOrder)
                .Select(e => e.Key).FirstOrDefault();
        }
        else
        {
            next = shown.Where(e => e.Value.Playing).OrderByDescending(e => e.Value.StartedOrder).Select(e => e.Key).FirstOrDefault();
            // Nothing plays: the one shown stays, else the last one that played, else the last one seen.
            if (next is null)
                next = Current is not null && _entries.ContainsKey(Current) && !IsHidden(Current)
                    ? Current
                    : shown.OrderByDescending(e => e.Value.StartedOrder).ThenByDescending(e => e.Value.SeenOrder).Select(e => e.Key).FirstOrDefault();
        }
        bool changed = next != Current;
        Current = next;
        return changed;
    }
}
