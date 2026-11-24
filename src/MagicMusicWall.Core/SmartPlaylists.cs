namespace MagicMusicWall.Core;

public sealed record SmartPlaylistRule
{
    // All supplied conditions must match; the result is evaluated when opened.
    public bool FavoritesOnly { get; init; }
    public bool UnplayedOnly { get; init; }
    public int? PlayedWithinDays { get; init; }
    public string Genre { get; init; } = "";
}

public static class SmartPlaylists
{
    public static IReadOnlyList<MediaItem> Evaluate(IEnumerable<MediaItem> items, SmartPlaylistRule rule, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.PlayedWithinDays is < 1 or > 36500) throw new ArgumentOutOfRangeException(nameof(rule), "Recent play days must be between 1 and 36,500.");
        var current = now ?? DateTimeOffset.UtcNow;
        return items.Where(i => !i.IsVideo && (!rule.FavoritesOnly || i.Favorite) && (!rule.UnplayedOnly || i.PlayCount == 0)
            && (rule.PlayedWithinDays is null || i.LastPlayedUtc is { } played && played <= current && played >= current.AddDays(-rule.PlayedWithinDays.Value))
            && (string.IsNullOrWhiteSpace(rule.Genre) || i.Genre.Split(';', StringSplitOptions.TrimEntries).Any(g => g.Equals(rule.Genre.Trim(), StringComparison.OrdinalIgnoreCase))))
            .OrderBy(i => i.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

public sealed partial class LibraryCatalog
{
    private MusicPlaylist EditablePlaylist(string playlistId)
    {
        var playlist = FindPlaylist(playlistId);
        if (playlist.Rule is not null) throw new InvalidOperationException("This smart playlist is filled by its rules. Save its results as a regular playlist to edit tracks manually.");
        return playlist;
    }
    public void SetFavorite(string itemId, bool favorite)
    {
        lock (_gate)
        {
            var item = FindItem(itemId); var previous = item.Favorite;
            try { item.Favorite = favorite; SaveLocked(); }
            catch { item.Favorite = previous; throw; }
        }
    }

    public void RecordPlayed(string itemId, DateTimeOffset? at = null)
    {
        lock (_gate)
        {
            var item = FindItem(itemId); var previous = (item.PlayCount, item.LastPlayedUtc);
            try { item.PlayCount = (int)Math.Min(int.MaxValue, (long)Math.Max(0, item.PlayCount) + 1); item.LastPlayedUtc = at ?? DateTimeOffset.UtcNow; SaveLocked(); }
            catch { (item.PlayCount, item.LastPlayedUtc) = previous; throw; }
        }
    }

    public MusicPlaylist CreateSmartPlaylist(string name, SmartPlaylistRule rule)
    {
        lock (_gate)
        {
            _ = SmartPlaylists.Evaluate([], rule); // Validate before modifying the catalog.
            var playlist = new MusicPlaylist { Name = ValidatePlaylistName(name), Rule = rule };
            Document.Playlists.Add(playlist);
            try { SaveLocked(); return playlist; }
            catch { Document.Playlists.Remove(playlist); throw; }
        }
    }

    public IReadOnlyList<MediaItem> PlaylistItems(string playlistId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            var playlist = FindPlaylist(playlistId);
            if (playlist.Rule is { } rule) return SmartPlaylists.Evaluate(Document.Items, rule, now);
            var known = Document.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
            return playlist.ItemIds.Where(known.ContainsKey).Select(id => known[id]).ToArray();
        }
    }
}
