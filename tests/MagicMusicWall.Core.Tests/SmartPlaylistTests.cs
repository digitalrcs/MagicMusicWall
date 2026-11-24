using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class SmartPlaylistTests
{
    [Fact]
    public void RulesCombineAndRecentWindowExcludesFutureAndExpiredPlays()
    {
        var now = DateTimeOffset.Parse("2026-11-24T12:00:00Z");
        MediaItem Item(string id, double age) => new() { Id = id, Favorite = true, Genre = "Rock; Jazz", PlayCount = 1, LastPlayedUtc = now.AddDays(-age) };
        var matches = SmartPlaylists.Evaluate([Item("match", 5), Item("old", 31), Item("future", -1), Item("boundary", 30)],
            new() { FavoritesOnly = true, Genre = "jazz", PlayedWithinDays = 30 }, now);
        Assert.Equal(["match", "boundary"], matches.Select(i => i.Id));
        Assert.Empty(SmartPlaylists.Evaluate(matches, new() { UnplayedOnly = true }));
    }
    [Fact]
    public async Task FavoriteHistoryAndSmartRulesSurviveRescanAndRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-smart-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "track.mp3"); File.WriteAllText(path, "synthetic unreadable tags");
            var c = new LibraryCatalog(Path.Combine(root, "data")); await c.AddFilesAsync([path]);
            var id = c.Document.Items.Single().Id; c.SetFavorite(id, true); c.RecordPlayed(id);
            var smart = c.CreateSmartPlaylist("Favorites", new() { FavoritesOnly = true });
            Assert.Throws<InvalidOperationException>(() => c.AddToPlaylist(smart.Id, [id]));
            Assert.Throws<InvalidOperationException>(() => c.SetPlaylistItems(smart.Id, [id]));
            Assert.Throws<InvalidOperationException>(() => c.RemoveFromPlaylist(smart.Id, [id]));
            await c.AddFilesAsync([path]);
            var reopened = new LibraryCatalog(Path.Combine(root, "data")); reopened.Load();
            var item = Assert.Single(reopened.PlaylistItems(smart.Id));
            Assert.Equal(id, item.Id); Assert.True(item.Favorite); Assert.Equal(1, item.PlayCount); Assert.NotNull(item.LastPlayedUtc);
            reopened.SetFavorite(id, false); Assert.Empty(reopened.PlaylistItems(smart.Id));
        }
        finally { Directory.Delete(root, true); }
    }
}
