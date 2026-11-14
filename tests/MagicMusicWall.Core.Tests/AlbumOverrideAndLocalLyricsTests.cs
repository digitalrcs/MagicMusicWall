using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class AlbumOverrideAndLocalLyricsTests : IDisposable
{
    private const string Source = "https://musicbrainz.org/recording/11111111-1111-1111-1111-111111111111";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-Metadata-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    public AlbumOverrideAndLocalLyricsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AlbumOverrideSurvivesRestartAndRescanWhileTaggedAlbumUpdates()
    {
        var path = WriteWave("Original tag");
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var catalog = NewCatalog();
        await catalog.AddFilesAsync([path]);
        var item = Assert.Single(catalog.Document.Items);
        var id = item.Id;
        Assert.Equal("Original tag", item.TaggedAlbum);
        var playlist = catalog.CreatePlaylist("Override test");
        catalog.AddToPlaylist(playlist.Id, [id]);
        catalog.SetAlbumOverride(id, "  Chosen album  ", Source);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        catalog = NewCatalog();
        item = Assert.Single(catalog.Document.Items);
        Assert.Equal("Chosen album", item.Album);
        Assert.Equal("Chosen album", item.AlbumOverride);
        Assert.Equal("Original tag", item.TaggedAlbum);
        Assert.Equal(Source, item.AlbumLookupSource);
        using (var media = TagLib.File.Create(path)) { media.Tag.Album = "Changed file tag"; media.Save(); }
        var externallyChanged = SHA256.HashData(File.ReadAllBytes(path));
        await catalog.RescanAsync();
        Assert.Equal("Changed file tag", item.TaggedAlbum);
        Assert.Equal("Chosen album", item.Album);
        Assert.Equal(id, item.Id);
        Assert.Equal([id], catalog.Document.Playlists.Single(p => p.Id == playlist.Id).ItemIds);
        catalog.ClearAlbumOverride(id);
        var restored = Assert.Single(NewCatalog().Document.Items);
        Assert.Equal("Changed file tag", restored.Album);
        Assert.Empty(restored.AlbumOverride);
        Assert.Empty(restored.AlbumLookupSource);
        Assert.Equal(externallyChanged, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LegacyAlbumBecomesTaggedAlbumAndCanBeRestoredWithoutMediaFile()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "library.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Items = new[] { new { Id = "legacy", Path = Path.Combine(_root, "unavailable.mp3"), Album = "Legacy album" } },
            Folders = Array.Empty<object>(), Playlists = Array.Empty<object>()
        }));
        var catalog = NewCatalog();
        var item = Assert.Single(catalog.Document.Items);
        Assert.Equal("Legacy album", item.TaggedAlbum);
        catalog.SetAlbumOverride(item.Id, "Online album", Source);
        catalog.ClearAlbumOverride(item.Id);
        Assert.Equal("Legacy album", Assert.Single(NewCatalog().Document.Items).Album);
    }

    [Fact]
    public async Task EmptyTaggedAlbumDoesNotBecomeTheOverrideOnReload()
    {
        var catalog = NewCatalog();
        await catalog.AddFilesAsync([WriteWave("")]);
        var item = Assert.Single(catalog.Document.Items);
        catalog.SetAlbumOverride(item.Id, "Online album", Source);
        catalog = NewCatalog();
        catalog.ClearAlbumOverride(item.Id);
        Assert.Empty(Assert.Single(catalog.Document.Items).Album);
    }

    [Theory]
    [InlineData("http://musicbrainz.org/recording/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://musicbrainz.org.example/recording/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://user@musicbrainz.org/recording/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://musicbrainz.org/recording/not-an-id")]
    [InlineData("https://musicbrainz.org/artist/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://musicbrainz.org:444/recording/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://musicbrainz.org/recording/11111111-1111-1111-1111-111111111111?other=1")]
    public void AlbumOverrideRejectsUnrelatedSourcesWithoutChangingCatalog(string source)
    {
        var catalog = NewCatalog();
        var item = new MediaItem { Path = Path.Combine(_root, "missing.mp3"), Album = "Original" };
        catalog.Document.Items.Add(item);
        Assert.Throws<ArgumentException>(() => catalog.SetAlbumOverride(item.Id, "Chosen", source));
        Assert.Equal("Original", item.Album);
        Assert.Empty(item.AlbumOverride);
    }

    [Fact]
    public void AlbumOverrideValidatesNameAndItem()
    {
        var catalog = NewCatalog();
        var item = new MediaItem { Path = Path.Combine(_root, "missing.mp3") };
        catalog.Document.Items.Add(item);
        Assert.Throws<ArgumentException>(() => catalog.SetAlbumOverride(item.Id, " ", Source));
        Assert.Throws<ArgumentException>(() => catalog.SetAlbumOverride(item.Id, new string('x', 501), Source));
        Assert.Throws<ArgumentException>(() => catalog.SetAlbumOverride("missing", "Album", Source));
        Assert.Throws<ArgumentException>(() => catalog.ClearAlbumOverride("missing"));
        catalog.SetAlbumOverride(item.Id, new string('x', 500), Source);
        Assert.Equal(500, item.Album.Length);
    }

    [Fact]
    public void AlbumOverrideRollsBackWhenSavingFails()
    {
        if (!OperatingSystem.IsWindows()) return;
        var catalog = NewCatalog();
        var item = new MediaItem { Path = Path.Combine(_root, "missing.mp3"), Album = "Original", TaggedAlbum = "Original" };
        catalog.Document.Items.Add(item);
        catalog.Save();
        using (var locked = new FileStream(catalog.LibraryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => catalog.SetAlbumOverride(item.Id, "Chosen", Source));
        Assert.Equal("Original", item.Album);
        Assert.Equal("Original", Assert.Single(NewCatalog().Document.Items).Album);
        Assert.Empty(item.AlbumOverride);
    }

    [Fact]
    public void EmbeddedLyricsTakePriorityAndNeverChangeTheMedia()
    {
        var path = WriteWave("Album", "Synthetic embedded line");
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), "[00:01.00]Synthetic sidecar line");
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var result = LocalLyrics.Read(new MediaItem { Path = path, Title = "Song", Artist = "Artist", Album = "Album" });
        Assert.NotNull(result);
        Assert.Equal("Embedded lyrics", result.SourceName);
        Assert.Equal("Synthetic embedded line", result.PlainText);
        Assert.Empty(result.SourceUrl);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void LrcTakesPriorityOverTextAndExposesPlainAndTimedVersions()
    {
        var path = WriteWave("");
        var lrc = "[ar:Synthetic artist]\r\n[00:01.00]Synthetic first line\r\n[00:02.00][00:03.00]Synthetic repeated line";
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), lrc);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), "Synthetic fallback line");
        var result = LocalLyrics.Read(new MediaItem { Path = path });
        Assert.NotNull(result);
        Assert.Equal("Local lyrics file", result.SourceName);
        Assert.Equal(lrc, result.SyncedText);
        Assert.Equal("Synthetic first line" + Environment.NewLine + "Synthetic repeated line", result.PlainText);
        Assert.Empty(result.SourceUrl);
    }

    [Fact]
    public void OversizedLrcFallsBackToBoundedBomEncodedText()
    {
        var path = WriteWave("");
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), new string('x', 1024 * 1024 + 1));
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), "Synthetic Unicode café line", Encoding.Unicode);
        var result = LocalLyrics.Read(new MediaItem { Path = path });
        Assert.NotNull(result);
        Assert.Equal("Synthetic Unicode café line", result.PlainText);
        Assert.Empty(result.SyncedText);
    }

    [Fact]
    public void MetadataOnlyLrcFallsBackAndMissingFilesReturnNull()
    {
        var path = Path.Combine(_root, "missing.mp3");
        Assert.Null(LocalLyrics.Read(new MediaItem { Path = path }));
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), "[ar:Synthetic artist]\n[ti:Synthetic title]");
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), "Synthetic fallback");
        Assert.Equal("Synthetic fallback", LocalLyrics.Read(new MediaItem { Path = path })?.PlainText);
        Assert.Null(LocalLyrics.Read(new MediaItem { Path = "\0invalid" }));
    }

    private LibraryCatalog NewCatalog() { var catalog = new LibraryCatalog(Data); catalog.Load(); return catalog; }

    private string WriteWave(string album, string lyrics = "")
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int rate = 8000, bytes = rate * 2;
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(bytes); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        using var media = TagLib.File.Create(path);
        media.Tag.Album = album; media.Tag.Title = "Synthetic song"; media.Tag.Performers = ["Synthetic artist"]; media.Tag.Lyrics = lyrics; media.Save();
        return path;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
