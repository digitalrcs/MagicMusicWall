using System.Text;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class MusicBrainzMapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-MusicBrainz-" + Guid.NewGuid().ToString("N"));
    private const string RecordingId = "6a3ed378-687d-4fcb-b771-dac98bca3c82";
    private const string ReleaseId = "6b2f8f31-d29e-4f5a-bcf5-69ab2e028553";

    [Fact]
    public void SaveReloadAndClearPreserveOriginalTagsFilesAndPlaylistIdentity()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "untouched.mp3");
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var item = Item("a"); item.Path = file; item.Genre = "Local genre"; item.Year = 1991;
        catalog.Document.Items.Add(item);
        var playlist = catalog.CreatePlaylist("Selected music"); catalog.AddToPlaylist(playlist.Id, [item.Id]);
        var info = Info();
        catalog.SetMusicBrainzInfo(item.Id, info);
        info.Genres[0] = "Mutated outside catalog";
        Assert.Same(item, Assert.Single(catalog.Document.Items));
        Assert.Equal("Jazz", item.MusicBrainz!.Genres[0]);
        Assert.Equal("Local genre", item.Genre); Assert.Equal(1991u, item.Year);
        Assert.Equal("Song a", item.Title); Assert.Equal("Artist a", item.Artist); Assert.Equal("Album a", item.Album);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(file));
        var reopened = new LibraryCatalog(Path.Combine(_root, "data")); reopened.Load();
        var saved = Assert.Single(reopened.Document.Items);
        Assert.Equal(RecordingId, saved.MusicBrainz!.RecordingId);
        Assert.Equal(new[] { "Jazz" }, saved.MusicBrainz.Genres);
        Assert.Equal(new[] { "a" }, reopened.Document.Playlists.Single(x => x.Id == playlist.Id).ItemIds);
        reopened.SetMusicBrainzInfo(item.Id, null);
        var cleared = new LibraryCatalog(Path.Combine(_root, "data")); cleared.Load();
        Assert.Null(cleared.Document.Items.Single().MusicBrainz);
        Assert.Equal("Local genre", cleared.Document.Items.Single().Genre);
    }

    [Fact]
    public void FailedSaveRestoresPreviousEnrichmentAndInvalidDataIsRejected()
    {
        var directory = Path.Combine(_root, "data");
        var catalog = new LibraryCatalog(directory); catalog.Load();
        var item = Item("a"); catalog.Document.Items.Add(item); catalog.SetMusicBrainzInfo(item.Id, Info());
        var saved = item.MusicBrainz;
        Assert.Throws<ArgumentException>(() => catalog.SetMusicBrainzInfo(item.Id, Info() with { RecordingId = "wrong" }));
        Assert.Throws<ArgumentException>(() => catalog.SetMusicBrainzInfo(item.Id, Info() with { Sources = ["https://example.com/recording/" + RecordingId] }));
        File.Move(catalog.LibraryPath, Path.Combine(directory, "preserved.json"));
        Directory.CreateDirectory(catalog.LibraryPath);
        Assert.ThrowsAny<IOException>(() => catalog.SetMusicBrainzInfo(item.Id, Info() with { Title = "Replacement" }));
        Assert.Same(saved, item.MusicBrainz);
    }

    [Fact]
    public void MapUsesFallbacksAndSearchLabelsWithoutReplacingKnownLocalMetadata()
    {
        var known = Item("known"); known.Genre = "Folk"; known.Year = 1991; known.MusicBrainz = Info();
        var missing = Item("missing"); missing.Artist = ""; missing.Album = "Unknown album"; missing.Title = ""; missing.MusicBrainz = Info();
        var video = Item("video"); video.IsVideo = true; video.MusicBrainz = Info();
        var map = MusicMapIndex.Build([known, missing, video]);
        Assert.Equal(2, map.TrackCount); Assert.Equal(2, map.EnrichedTrackCount);
        var song = map.Entities.Single(x => x.Id == "song:known");
        Assert.Equal("Song known", song.Name); Assert.Equal("Artist known", song.Artist); Assert.Equal("Album known", song.Album);
        Assert.Equal(new[] { 1990 }, song.Decades); Assert.Equal(new[] { "Folk", "Jazz" }, song.Genres);
        Assert.Contains("mellow", song.Keywords); Assert.Contains("violin", song.Keywords);
        Assert.Contains("Canonical title", song.Keywords); Assert.Contains("Player — violin", song.Credits);
        Assert.Equal(1, song.MusicBrainzTrackCount);
        var fallback = map.Entities.Single(x => x.Id == "song:missing");
        Assert.Equal("Canonical title", fallback.Name); Assert.Equal("Canonical artist", fallback.Artist);
        Assert.Equal("Canonical album", fallback.Album); Assert.Equal(new[] { 1970 }, fallback.Decades);
        Assert.DoesNotContain(map.Entities, x => x.TrackIds.Contains("video"));
        known.MusicBrainz.Genres[0] = "Changed"; known.MusicBrainz.Tags[0] = "Changed";
        Assert.Contains("Jazz", song.Genres); Assert.Contains("mellow", song.Keywords);
    }

    [Fact]
    public void EnrichmentAddsExplainedMetadataLinksButDoesNotAlterSongSoundScores()
    {
        var a = Item("a"); var b = Item("b");
        var audio = new Dictionary<string, AudioFeatures> { [a.Id] = Features(1), [b.Id] = Features(2) };
        var before = MusicMapIndex.Build([a, b], audio);
        Assert.Empty(before.Related("song:a", mode: MusicMapSimilarity.Metadata));
        var sound = Assert.Single(before.Related("song:a", mode: MusicMapSimilarity.Sound));
        a.MusicBrainz = Info() with { FirstReleaseDate = "", Tags = ["mellow", "Jazz", "violin"] };
        b.MusicBrainz = Info() with { FirstReleaseDate = "", Tags = ["mellow", "Jazz", "violin"] };
        var after = MusicMapIndex.Build([a, b], audio);
        var metadata = Assert.Single(after.Related("song:a", mode: MusicMapSimilarity.Metadata));
        Assert.Equal(125, metadata.Score); // Genre100 + instrument20 + mood tag5; duplicate labels are not counted twice.
        Assert.Contains("MusicBrainz genres", metadata.Reason);
        Assert.Contains("Shared credited instrument (MusicBrainz): violin", metadata.Reason);
        Assert.Contains("Shared community tag (MusicBrainz): mellow", metadata.Reason);
        var connection = Assert.Single(after.Connections(["song:a", "song:b"], MusicMapSimilarity.Metadata));
        Assert.Equal(125d / 255, connection.Strength, 10);
        var unchanged = Assert.Single(after.Related("song:a", mode: MusicMapSimilarity.Sound));
        Assert.Equal(sound.Score, unchanged.Score); Assert.Equal(sound.Reason, unchanged.Reason);
        a.MusicBrainz = b.MusicBrainz = null;
        Assert.Empty(MusicMapIndex.Build([a, b]).Related("song:a"));
    }

    [Fact]
    public void ManyCommunityTagsCannotOutrankSharedGenreAndNullOptionalArraysAreSafe()
    {
        var tags = Enumerable.Range(0, 30).Select(i => "tag" + i).ToArray();
        var source = Item("source"); source.Genre = "Folk";
        source.MusicBrainz = Info() with { Genres = [], Tags = tags, Instruments = [], FirstReleaseDate = "" };
        var genre = Item("genre"); genre.Genre = "Folk";
        var tagged = Item("tagged"); tagged.MusicBrainz = Info() with { Genres = [], Tags = tags, Instruments = [], FirstReleaseDate = "" };
        var map = MusicMapIndex.Build([source, genre, tagged]);
        Assert.Equal("song:genre", map.Related("song:source")[0].Entity.Id);
        Assert.Equal(15, map.Related("song:source").Single(x => x.Entity.Id == "song:tagged").Score);
        source.MusicBrainz = Info() with { Genres = null!, Tags = null!, Instruments = null!, Credits = null! };
        Assert.Equal(1, MusicMapIndex.Build([source]).EnrichedTrackCount);
    }

    [Fact]
    public void DenseOnlineEvidenceRemainsBoundedAndBlendedMissingAudioKeepsItsMetadataWeight()
    {
        var a = Item("a"); var b = Item("b"); b.Artist = a.Artist; b.Album = a.Album;
        a.Year = b.Year = 1980;
        a.MusicBrainz = Info() with { Genres = ["Folk", "Jazz", "Rock", "Pop"], Tags = ["mellow", "happy", "relaxing", "bright"], Instruments = ["violin", "guitar", "drums", "flute"] };
        b.MusicBrainz = a.MusicBrainz;
        var map = MusicMapIndex.Build([a, b]);
        Assert.Equal(1d, Assert.Single(map.Connections(["song:a", "song:b"], MusicMapSimilarity.Metadata)).Strength);
        Assert.Equal(.3, Assert.Single(map.Connections(["song:a", "song:b"], MusicMapSimilarity.Blended)).Strength, 10);
        Assert.Empty(map.Related("song:a", mode: MusicMapSimilarity.Sound));
    }

    [Fact]
    public void TitleArtistEditsInvalidateSavedIdentityButFormattingCorrectionsDoNot()
    {
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var item = Item("a"); item.Title = "Café Tune"; item.Artist = "Bánd"; catalog.Document.Items.Add(item);
        catalog.SetMusicBrainzInfo(item.Id, Info());
        catalog.ApplyWrittenTags(item.Id, new() { Title = "Cafe Tune", Artist = " BAND ", Album = "Album a" }, false);
        Assert.NotNull(item.MusicBrainz);
        catalog.ApplyWrittenTags(item.Id, new() { Title = "Different Song", Artist = "Band" }, false);
        Assert.Null(item.MusicBrainz);
    }

    [Fact]
    public void ChangedAlbumClearsReleaseContextButPreservesRecordingOnlyEvidence()
    {
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var item = Item("a"); catalog.Document.Items.Add(item); catalog.SetMusicBrainzInfo(item.Id, Info());
        catalog.SetAlbumOverride(item.Id, "Different release", "https://musicbrainz.org/release/" + ReleaseId);
        Assert.Null(item.MusicBrainz);
        catalog.SetMusicBrainzInfo(item.Id, Info()); catalog.ClearAlbumOverride(item.Id);
        Assert.Null(item.MusicBrainz);
        catalog.SetMusicBrainzInfo(item.Id, Info() with { ReleaseId = "", Album = "" });
        catalog.SetAlbumOverride(item.Id, "Another release", "https://musicbrainz.org/release/" + ReleaseId);
        Assert.NotNull(item.MusicBrainz);
        catalog.ClearAlbumOverride(item.Id);
        Assert.NotNull(item.MusicBrainz);
        catalog.SetMusicBrainzInfo(item.Id, Info());
        catalog.ApplyWrittenTags(item.Id, new() { Title = item.Title, Artist = item.Artist, Album = "Changed file album" }, true);
        Assert.Null(item.MusicBrainz);
    }

    [Fact]
    public async Task RescanRetainsUnchangedEnrichmentAndInvalidatesChangedFileIdentity()
    {
        var file = WriteWave("Original.wav");
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        await catalog.AddFilesAsync([file]); var item = Assert.Single(catalog.Document.Items);
        catalog.SetMusicBrainzInfo(item.Id, Info());
        await catalog.RescanAsync();
        Assert.Equal(RecordingId, item.MusicBrainz!.RecordingId);
        using (var tagFile = TagLib.File.Create(file)) { tagFile.Tag.Title = "Different recording"; tagFile.Save(); }
        await catalog.RescanAsync();
        Assert.Null(item.MusicBrainz);
    }

    [Fact]
    public void OldCatalogsNeedNoEnrichmentAndMalformedOptionalInfoDoesNotLoseTheLibrary()
    {
        var directory = Path.Combine(_root, "data"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "library.json"), """
            {"SchemaVersion":1,"Items":[{"Id":"legacy","Path":"C:\\Music\\Legacy.mp3","Title":"Legacy","Album":"Old album"}],"Folders":[],"Playlists":[]}
            """);
        var catalog = new LibraryCatalog(directory); catalog.Load();
        Assert.Null(Assert.Single(catalog.Document.Items).MusicBrainz);
        Assert.Equal("Legacy", MusicMapIndex.Build(catalog.Document.Items).Entities.Single(x => x.Kind == MusicMapKind.Song).Name);
        catalog.Document.Items[0].MusicBrainz = Info() with { RecordingId = "malformed" }; catalog.Save();
        var reopened = new LibraryCatalog(directory); reopened.Load();
        Assert.Equal("legacy", Assert.Single(reopened.Document.Items).Id); Assert.Null(reopened.Document.Items[0].MusicBrainz);
    }

    private static MediaItem Item(string id) => new() { Id = id, Path = @"C:\Music\" + id + ".mp3", Title = "Song " + id, Artist = "Artist " + id, Album = "Album " + id };
    private static MusicBrainzInfo Info() => new()
    {
        RecordingId = RecordingId, ReleaseId = ReleaseId, Title = "Canonical title", Artist = "Canonical artist", Album = "Canonical album",
        FirstReleaseDate = "1973-03-01", Genres = ["Jazz"], Tags = ["mellow"], Instruments = ["violin"], Credits = ["Player — violin"],
        Sources = ["https://musicbrainz.org/recording/" + RecordingId], RetrievedUtc = DateTimeOffset.Parse("2026-11-16T12:00:00Z")
    };
    private static AudioFeatures Features(double value) => new(Enumerable.Repeat(value, 26).ToArray(),
        Enumerable.Repeat(1d / 12, 12).ToArray(), 120, .8, .3, 800);
    private string WriteWave(string name)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, name);
        const int sampleRate = 8000, sampleCount = 8000;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + sampleCount * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(sampleCount * 2);
        for (var i = 0; i < sampleCount; i++) writer.Write((short)(1000 * Math.Sin(i * Math.PI / 10)));
        return path;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
