namespace NowPlaying.Core;

/// <summary>
/// The players as Windows names them in its media sessions ("SourceAppUserModelId": an exe name, a short name, a
/// hash, or the id of a Store app), turned into a name for people and matched with the processes that play sound
/// (for the volume of the app that plays).
/// </summary>
public static class AppIdentity
{
    // Ids that read nothing like the app: the first part (before '!' for Store apps) in lower case.
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "Spotify",
        ["spotifyab.spotifymusic_zpdnekdrzrea0"] = "Spotify",
        ["msedge"] = "Microsoft Edge",
        ["chrome"] = "Google Chrome",
        ["firefox"] = "Firefox",
        ["brave"] = "Brave",
        ["opera"] = "Opera",
        ["vivaldi"] = "Vivaldi",
        ["vlc"] = "VLC",
        ["microsoft.zunemusic_8wekyb3d8bbwe"] = "Media Player",
        ["appleinc.applemusicwin_nzyj5cx40ttqa"] = "Apple Music",
        ["deezer"] = "Deezer",
        ["deezer.62021768415af_q7m17pa7q8kj0"] = "Deezer",
        ["tidal"] = "TIDAL",
        ["amazon music"] = "Amazon Music",
        ["foobar2000"] = "foobar2000",
        ["musicbee"] = "MusicBee",
        ["discord"] = "Discord",
    };

    /// <summary>A name to show for the player ("Spotify", "Microsoft Edge"…).</summary>
    public static string FriendlyName(string sourceAppId)
    {
        string key = Key(sourceAppId);
        if (KnownNames.TryGetValue(key, out var name))
            return name;
        if (IsFirefoxHash(key))
            return "Firefox";
        // Store app without a known name: "Publisher.AppName_hash" gives "AppName".
        int underscore = key.IndexOf('_');
        string bare = underscore > 0 ? key[..underscore] : key;
        int dot = bare.LastIndexOf('.');
        if (dot >= 0 && dot < bare.Length - 1)
            bare = bare[(dot + 1)..];
        return bare.Length > 0 ? char.ToUpperInvariant(bare[0]) + bare[1..] : sourceAppId;
    }

    /// <summary>
    /// Whether a process playing sound (<paramref name="processName"/> without ".exe", and its Store package family
    /// when it has one) belongs to the player <paramref name="sourceAppId"/>.
    /// </summary>
    public static bool Matches(string sourceAppId, string processName, string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId) || string.IsNullOrWhiteSpace(processName))
            return false;
        string id = sourceAppId.Trim();
        int bang = id.IndexOf('!');
        if (bang > 0)
            return packageFamilyName is not null && string.Equals(id[..bang], packageFamilyName, StringComparison.OrdinalIgnoreCase);

        string key = Key(id);
        string process = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        if (string.Equals(key, process, StringComparison.OrdinalIgnoreCase))
            return true;
        // Firefox names its sessions with a hash of its install folder.
        return IsFirefoxHash(key) && string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase);
    }

    // The part that names the app: a Store family, an exe name without folder nor extension, or the id itself.
    private static string Key(string sourceAppId)
    {
        string id = sourceAppId.Trim();
        int bang = id.IndexOf('!');
        if (bang > 0)
            return id[..bang];
        int slash = Math.Max(id.LastIndexOf('\\'), id.LastIndexOf('/'));
        if (slash >= 0)
            id = id[(slash + 1)..];
        return id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
    }

    private static bool IsFirefoxHash(string key) => key.Length == 16 && key.All(Uri.IsHexDigit);
}
