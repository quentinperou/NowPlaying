namespace NowPlaying.Core;

/// <summary>
/// Which of the players known to Windows the widget and the overlay show (the author's rule, October 21, 2026):
/// the last one that started playing; a player chosen by hand (right click on the widget) stays until another one
/// starts playing. While nothing plays, the one shown stays (paused) as long as it exists.
/// </summary>
public sealed class SourcePicker
{
    private readonly Dictionary<string, Entry> _entries = [];
    private string? _pinned;
    private long _order;

    private sealed class Entry
    {
        public bool Playing;
        public long StartedOrder; // when it last started playing; 0: never seen playing
        public long SeenOrder;
    }

    /// <summary>The source shown, null when Windows knows no player.</summary>
    public string? Current { get; private set; }

    public IReadOnlyCollection<string> Sources => _entries.Keys;

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
            // A player that starts takes over from one chosen by hand.
            if (_pinned != id)
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
        if (!_entries.ContainsKey(id))
            return false;
        _pinned = id;
        return Choose();
    }

    public bool IsPlaying(string id) => _entries.TryGetValue(id, out var e) && e.Playing;

    private bool Choose()
    {
        string? next;
        if (_pinned is not null && _entries.ContainsKey(_pinned))
            next = _pinned;
        else
        {
            next = _entries.Where(e => e.Value.Playing).OrderByDescending(e => e.Value.StartedOrder).Select(e => e.Key).FirstOrDefault();
            // Nothing plays: the one shown stays, else the last one that played, else the last one seen.
            if (next is null)
                next = Current is not null && _entries.ContainsKey(Current)
                    ? Current
                    : _entries.OrderByDescending(e => e.Value.StartedOrder).ThenByDescending(e => e.Value.SeenOrder).Select(e => e.Key).FirstOrDefault();
        }
        bool changed = next != Current;
        Current = next;
        return changed;
    }
}
