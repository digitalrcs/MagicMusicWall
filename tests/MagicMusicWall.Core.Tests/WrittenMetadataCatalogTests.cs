using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class WrittenMetadataCatalogTests
{
    [Fact]
    public void RefreshKeepsQueueReferencesAndPlaylistOrderAndOnlyClearsWrittenAlbumOverride()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-MetadataCatalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new LibraryCatalog(directory);
            catalog.Load();
            var item = new MediaItem { Path = Path.Combine(directory, "song.mp3"), Title = "Before", Album = "Override", AlbumOverride = "Override", AlbumLookupSource = "https://musicbrainz.org/recording/example", DurationSeconds = 200 };
            catalog.Document.Items.Add(item);
            var playlist = catalog.CreatePlaylist("Metadata check");
            catalog.AddToPlaylist(playlist.Id, [item.Id]);
            catalog.ApplyWrittenTags(item.Id, new() { Title = "After", Artist = "Artist", Album = "File album", Year = 1980 }, false);
            Assert.Same(item, Assert.Single(catalog.Document.Items));
            Assert.Equal("Override", item.Album);
            Assert.Equal("File album", item.TaggedAlbum);
            Assert.Equal(200, item.DurationSeconds);
            catalog.ApplyWrittenTags(item.Id, new() { Title = "After", AlbumArtist = "Fallback artist", Album = "New album", Genre = "Jazz", TrackNumber = 3 }, true);
            var reopened = new LibraryCatalog(directory); reopened.Load();
            var saved = Assert.Single(reopened.Document.Items);
            Assert.Equal(item.Id, saved.Id);
            Assert.Equal("Fallback artist", saved.Artist);
            Assert.Equal("New album", saved.Album);
            Assert.Empty(saved.AlbumOverride); Assert.Empty(saved.AlbumLookupSource);
            Assert.Equal("Jazz", saved.Genre); Assert.Equal(3u, saved.TrackNumber);
            Assert.Equal(new[] { item.Id }, reopened.Document.Playlists.Single(p => p.Id == playlist.Id).ItemIds);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
