using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class MusicMapIndexTests
{
    private static MediaItem Item(string id, string artist = "Band", string album = "Album", string genre = "Rock", uint year = 1973,
        string? path = null) => new() { Id = id, Title = "Song " + id, Artist = artist, Album = album, Genre = genre, Year = year,
        Path = path ?? @"C:\Music\" + artist + @"\" + album + @"\" + id + ".mp3" };

    [Fact]
    public void AlbumTitlesAreScopedToArtistAndCaseWhitespaceAreNormalized()
    {
        var map = MusicMapIndex.Build([Item("a", "Band A", "Greatest Hits"), Item("b", " band a ", " greatest hits "),
            Item("c", "Band B", "Greatest Hits")]);
        var albums = map.Entities.Where(x => x.Kind == MusicMapKind.Album).ToArray();
        Assert.Equal(2, albums.Length);
        Assert.Contains(albums, x => x.TrackIds.SequenceEqual(new[] { "a", "b" }));
        Assert.Equal(2, map.Entities.Count(x => x.Kind == MusicMapKind.Artist));
    }

    [Fact]
    public void UnavailableMusicIsIncludedVideosExcludedAndOriginalEditsCannotChangeSnapshot()
    {
        var music = Item("music"); music.IsAvailable = false;
        var video = Item("video"); video.IsVideo = true;
        var map = MusicMapIndex.Build([music, video]);
        music.Artist = "Changed"; music.Genre = "Changed"; music.Title = "Changed";
        Assert.Equal(1, map.TrackCount);
        Assert.Equal(1, map.TaggedTrackCount);
        Assert.Equal("Song music", map.Entities.Single(x => x.Kind == MusicMapKind.Song).Name);
        Assert.Equal(new[] { "Rock" }, map.Genres);
        Assert.DoesNotContain(map.Entities, x => x.Name == "Changed");
    }

    [Fact]
    public void UnknownMetadataIsGroupedByFolderWithoutInventingSimilarity()
    {
        var a = Item("a", "", "", "", 0, @"C:\Music\Folder A\a.mp3");
        var b = Item("b", "Unknown Artist", "Unknown Album", "Unclassified", 0, @"C:\Music\Folder B\b.mp3");
        var c = Item("c", "", "", "unknown", 0, @"C:\Music\Folder A\c.mp3");
        var map = MusicMapIndex.Build([a, b, c]);
        Assert.Equal(2, map.Entities.Count(x => x.Kind == MusicMapKind.Artist));
        Assert.Equal(2, map.Entities.Count(x => x.Kind == MusicMapKind.Album));
        Assert.Equal(0, map.TaggedTrackCount);
        Assert.Empty(map.Genres);
        Assert.Empty(map.Related("song:a"));
    }

    [Fact]
    public void UnknownArtistDoesNotMergeNamedAlbumsFromDifferentFolders()
    {
        var map = MusicMapIndex.Build([Item("a", "", "Best Of", path: @"C:\One\a.mp3"),
            Item("b", "", "Best Of", path: @"C:\Two\b.mp3")]);
        Assert.Equal(2, map.Entities.Count(x => x.Kind == MusicMapKind.Album));
    }

    [Fact]
    public void GenreSeparatorsPreserveCompoundNamesAndOnlyCountUsableTags()
    {
        var map = MusicMapIndex.Build([Item("a", genre: " R&B ; Rock, Alternative |rock, alternative|Unknown"),
            Item("b", genre: "(none)")]);
        Assert.Equal(new[] { "R&B", "Rock, Alternative" }, map.Genres);
        Assert.Equal(1, map.TaggedTrackCount);
    }

    [Fact]
    public void RelatedResultsExplainGenreArtistAlbumAndEraWithoutCrossingKinds()
    {
        var map = MusicMapIndex.Build([Item("a"), Item("b", "Other", "Other", "Rock", 1985),
            Item("c", genre: "Jazz"), Item("d", "Distant", "Distant", "Folk", 2003)]);
        var related = map.Related("song:a");
        Assert.Equal("song:b", related[0].Entity.Id);
        Assert.Contains("Shared genre: Rock", related[0].Reason);
        var sibling = related.Single(x => x.Entity.Id == "song:c");
        Assert.Contains("Same artist", sibling.Reason);
        Assert.Contains("Same album", sibling.Reason);
        Assert.Contains("1970s", sibling.Reason);
        Assert.All(related, x => Assert.Equal(MusicMapKind.Song, x.Entity.Kind));
        Assert.DoesNotContain(related, x => x.Entity.Id is "song:a" or "song:d");
        Assert.Single(map.Related("song:a", 1));
        Assert.Empty(map.Related("song:a", 0));
        Assert.Empty(map.Related("not-an-id"));
    }

    [Fact]
    public void BuildIsDeterministicAndAlbumQueueUsesDiscThenTrackOrder()
    {
        var a = Item("a"); a.TrackNumber = 2;
        var b = Item("b"); b.TrackNumber = 1;
        var c = Item("c"); c.DiscNumber = 2; c.TrackNumber = 1;
        var first = MusicMapIndex.Build([a, c, b]);
        var second = MusicMapIndex.Build([b, c, a]);
        Assert.Equal(first.Entities.Select(x => x.Id), second.Entities.Select(x => x.Id));
        Assert.Equal(new[] { "b", "a", "c" }, first.Entities.Single(x => x.Kind == MusicMapKind.Album).TrackIds);
    }

    [Fact]
    public void EmptyLibraryAndDuplicateCatalogEntriesAreSafe()
    {
        var empty = MusicMapIndex.Build([]);
        Assert.Empty(empty.Entities); Assert.Empty(empty.Genres); Assert.Equal(0, empty.TrackCount);
        Assert.Empty(empty.Related("any"));
        var same = Item("same");
        Assert.Equal(1, MusicMapIndex.Build([same, same]).TrackCount);
    }

    [Fact]
    public void SoundRankingUsesMeasuredDescriptorsRatherThanMatchingTags()
    {
        var map = MusicMapIndex.Build([Item("source"), Item("same-tags"), Item("same-sound", "Other", "Other", "Jazz", 2001)],
            new Dictionary<string, AudioFeatures> { ["source"] = Features(1), ["same-tags"] = Features(9), ["same-sound"] = Features(1) });
        Assert.Equal("song:same-tags", map.Related("song:source")[0].Entity.Id);
        var sound = map.Related("song:source", mode: MusicMapSimilarity.Sound);
        Assert.Equal("song:same-sound", sound[0].Entity.Id);
        Assert.True(sound[0].Score > sound[1].Score);
        Assert.Contains("Estimated sound resemblance", sound[0].Reason);
        Assert.DoesNotContain("Shared genre", sound[1].Reason);
        Assert.DoesNotContain(sound, x => x.Entity.Id == "song:source" || x.Entity.Kind != MusicMapKind.Song);
    }

    [Fact]
    public void MissingAnalysisHasNoSoundLinksAndBlendedExplainsMetadataFallback()
    {
        var map = MusicMapIndex.Build([Item("source"), Item("analyzed"), Item("unrelated", "Other", "Other", "Jazz", 2001)],
            new Dictionary<string, AudioFeatures> { ["analyzed"] = Features(1) });
        Assert.Empty(map.Related("song:source", mode: MusicMapSimilarity.Sound));
        Assert.Empty(map.Related("song:analyzed", mode: MusicMapSimilarity.Sound));
        var fallback = Assert.Single(map.Related("song:source", mode: MusicMapSimilarity.Blended));
        Assert.Contains("Metadata only", fallback.Reason);
        Assert.Contains("Shared genre", fallback.Reason);
        Assert.DoesNotContain("Estimated sound resemblance", fallback.Reason);
        Assert.Null(map.AudioSummary("song:source"));
    }

    [Fact]
    public void ArtistAndAlbumSummariesReportPartialAnalysisAndIgnoreUnknownTempo()
    {
        var map = MusicMapIndex.Build([Item("a"), Item("b"), Item("c")], new Dictionary<string, AudioFeatures>
        {
            ["a"] = Features(1) with { Tempo = 120, Rms = .2 }, ["b"] = Features(2) with { Tempo = 0, Rms = .4 }
        });
        Assert.Equal(2, map.AnalyzedTrackCount);
        foreach (var entity in map.Entities.Where(x => x.Kind != MusicMapKind.Song))
        {
            var summary = Assert.IsType<AudioFeatureSummary>(map.AudioSummary(entity.Id));
            Assert.Equal(2, summary.AnalyzedTracks); Assert.Equal(3, summary.TotalTracks);
            Assert.Equal(120, summary.Tempo); Assert.Equal(.3, summary.Rms, 8);
        }
        Assert.Equal(1, map.AudioSummary("song:a")!.AnalyzedTracks);
    }

    [Fact]
    public void InvalidOrUnindexedCachedFeaturesDoNotCountAndFeatureArraysAreSnapshotted()
    {
        var valid = Features(1);
        var invalid = Features(2) with { Mfcc = [double.NaN] };
        var map = MusicMapIndex.Build([Item("a"), Item("b"), Item("invalid")], new Dictionary<string, AudioFeatures>
        {
            ["a"] = valid, ["b"] = Features(1), ["invalid"] = invalid, ["not-in-library"] = Features(3)
        });
        var before = map.Related("song:a", mode: MusicMapSimilarity.Sound).Single().Score;
        Array.Fill(valid.Mfcc, 1000); Array.Fill(valid.Chroma, double.NaN);
        Assert.Equal(2, map.AnalyzedTrackCount);
        Assert.Null(map.AudioSummary("song:invalid"));
        Assert.Equal(before, map.Related("song:a", mode: MusicMapSimilarity.Sound).Single().Score);
    }

    [Fact]
    public void SoundResultsRemainDeterministicAcrossInputAndCacheOrder()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var audio = new Dictionary<string, AudioFeatures> { ["a"] = Features(1), ["b"] = Features(2), ["c"] = Features(3) };
        var a = MusicMapIndex.Build(items, audio);
        var b = MusicMapIndex.Build(items.Reverse(), audio.Reverse().ToDictionary(x => x.Key, x => x.Value));
        Assert.Equal(a.Related("song:a", mode: MusicMapSimilarity.Sound).Select(x => (x.Entity.Id, x.Score, x.Reason)),
            b.Related("song:a", mode: MusicMapSimilarity.Sound).Select(x => (x.Entity.Id, x.Score, x.Reason)));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Related("song:a", mode: (MusicMapSimilarity)100));
    }

    private static AudioFeatures Features(double value) => new(Enumerable.Range(0, 26).Select(i => value * (i + 1) / 26).ToArray(),
        Enumerable.Range(0, 12).Select(i => i == 9 ? 1d : 0d).ToArray(), 120, .8, .3, 800);

    [Theory]
    [InlineData(MusicMapSimilarity.Metadata, 210)]
    [InlineData(MusicMapSimilarity.Sound, 1000)]
    [InlineData(MusicMapSimilarity.Blended, 1000)]
    public void ConnectionsUseTheSameUnroundedScoresAndReasonsAsRelated(MusicMapSimilarity mode, double scale)
    {
        var map = MusicMapIndex.Build([Item("a"), Item("b"), Item("c", "Other", "Other", "Jazz", 2001)],
            new Dictionary<string, AudioFeatures> { ["a"] = Features(1), ["b"] = Features(2), ["c"] = Features(9) });
        var edges = map.Connections(["song:b", "song:a", "song:c", "song:a", "unknown"], mode);
        foreach (var edge in edges)
        {
            Assert.InRange(edge.Strength, double.Epsilon, 1);
            Assert.True(string.CompareOrdinal(edge.SourceId, edge.TargetId) < 0);
            var related = map.Related(edge.SourceId, mode: mode).Single(x => x.Entity.Id == edge.TargetId);
            Assert.Equal(related.Score, Math.Max(1, (int)Math.Round(edge.Strength * scale)));
            Assert.Equal(related.Reason, edge.Reason);
        }
        Assert.Equal(edges.Count, edges.Select(x => (x.SourceId, x.TargetId)).Distinct().Count());
        if (mode != MusicMapSimilarity.Metadata) Assert.Contains(edges, x => Math.Abs(x.Strength * scale - Math.Round(x.Strength * scale)) > .001);
    }

    [Fact]
    public void ConnectionsSkipMissingSoundCrossKindsAndZeroEvidenceAndCapVisibleGraph()
    {
        var map = MusicMapIndex.Build([Item("a"), Item("b"), Item("c", "Other", "Other", "Jazz", 2001)],
            new Dictionary<string, AudioFeatures> { ["a"] = Features(1) });
        Assert.Empty(map.Connections(map.Entities.Select(x => x.Id), MusicMapSimilarity.Sound));
        var edges = map.Connections(map.Entities.Select(x => x.Id), MusicMapSimilarity.Metadata);
        var edge = Assert.Single(edges);
        Assert.Equal("song:a", edge.SourceId); Assert.Equal("song:b", edge.TargetId);
        Assert.Empty(map.Connections(["missing", "missing"], MusicMapSimilarity.Metadata));
        Assert.Throws<ArgumentException>(() => map.Connections(Enumerable.Range(0, 33).Select(i => "id" + i), MusicMapSimilarity.Metadata));
    }
}
