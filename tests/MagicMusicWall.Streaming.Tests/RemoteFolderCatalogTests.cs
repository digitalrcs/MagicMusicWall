using System.Text.Json;
using MagicMusicWall.Core;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteFolderCatalogTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SyntheticRemoteCollection");
    private MediaItem Item(string relative, string title, bool video = false) => new()
    {
        Path = Path.Combine(_root, relative), Title = title, Artist = "Sample artist", Album = "Sample album", IsVideo = video
    };

    [Fact]
    public void HomeShowsRootsAndLooseFilesWithOpaqueIdsAndNoFilePaths()
    {
        var sources = new[] { new LibraryFolder { Path = _root } };
        var audio = Item("Track.mp3", "Track");
        var video = Item(Path.Combine("Movies", "Film.mp4"), "Film", true);
        var loose = new MediaItem { Path = Path.Combine(_root + "-other", "Loose.flac"), Title = "Loose" };
        var page = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse(sources, [audio, video, loose], null)!);
        Assert.Equal("Your folders", page.Title);
        Assert.Null(page.Id); Assert.Null(page.ParentId);
        Assert.Empty(page.Items); Assert.Equal(0, page.TotalItems); Assert.Equal(3, page.DescendantCount);
        Assert.Equal(2, page.Folders.Count);
        var root = page.Folders.Single(folder => folder.Name == "SyntheticRemoteCollection");
        var added = page.Folders.Single(folder => folder.Name == "Added files");
        Assert.Equal(2, root.Count); Assert.Equal(1, added.Count);
        Assert.Matches("^[0-9A-F]{64}$", root.Id);
        Assert.Matches("^[0-9A-F]{64}$", added.Id);
        Assert.DoesNotContain(_root, JsonSerializer.Serialize(page));
        var loosePage = RemoteFolderCatalog.Browse(sources, [audio, video, loose], added.Id)!;
        Assert.Equal(loose, Assert.Single(loosePage.Files));
        Assert.Null(RemoteFolderCatalog.ToRemote(loosePage).ParentId);
    }

    [Fact]
    public void ImmediateFoldersHaveRecursiveCountsAndVideosRemainBrowsableAndPlayable()
    {
        var sources = new[] { new LibraryFolder { Path = _root } };
        var direct = Item("Intro.mp3", "Intro");
        var nested = Item(Path.Combine("Artist", "Album", "Track.flac"), "Track");
        var video = Item(Path.Combine("Artist", "Video.mp4"), "Video", true);
        var page = RemoteFolderCatalog.Browse(sources, [direct, nested, video], RemoteFolderCatalog.IdFor(_root))!;
        Assert.Equal(direct, Assert.Single(page.Files));
        Assert.Equal(3, page.Descendants.Count);
        var artist = Assert.Single(RemoteFolderCatalog.ToRemote(page).Folders);
        Assert.Equal("Artist", artist.Name); Assert.Equal(2, artist.Count);
        var artistPage = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse(sources, [direct, nested, video], artist.Id)!);
        Assert.Equal(page.Path is null ? null : RemoteFolderCatalog.IdFor(page.Path), artistPage.ParentId);
        Assert.True(Assert.Single(artistPage.Items).IsVideo);
        Assert.Equal(1, artistPage.TotalItems); Assert.Equal(2, artistPage.DescendantCount);
        var album = Assert.Single(artistPage.Folders);
        var albumPage = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse(sources, [direct, nested, video], album.Id)!);
        Assert.Equal(artist.Id, albumPage.ParentId);
        Assert.Equal(nested.Id, Assert.Single(albumPage.Items).Id);
    }

    [Fact]
    public void OnlyCatalogRootsAndIndexedAncestorsCanBeResolved()
    {
        var source = new LibraryFolder { Path = _root };
        var item = Item(Path.Combine("Offline", "Deep", "Track.flac"), "Offline track");
        item.IsAvailable = false;
        Assert.NotNull(RemoteFolderCatalog.Browse([source], [item], RemoteFolderCatalog.IdFor(Path.Combine(_root, "Offline"))));
        foreach (var unknown in new[] { Path.Combine(_root, "Never indexed"), _root + "-other", Path.GetDirectoryName(_root)!, item.Path })
            Assert.Null(RemoteFolderCatalog.Browse([source], [item], RemoteFolderCatalog.IdFor(unknown)));
        Assert.Null(RemoteFolderCatalog.Browse([source], [item], _root));
        Assert.Null(RemoteFolderCatalog.Browse([source], [item], RemoteFolderCatalog.IdFor(FolderBrowser.LooseFiles)));
        Assert.NotNull(RemoteFolderCatalog.Browse([source], [], RemoteFolderCatalog.IdFor(_root)));
    }

    [Fact]
    public void IdsNormalizeCaseAndTrailingSeparatorsAndRemainStableAcrossCatalogOrder()
    {
        Assert.Equal(RemoteFolderCatalog.IdFor(_root), RemoteFolderCatalog.IdFor(_root.ToUpperInvariant() + Path.DirectorySeparatorChar));
        var sources = new[] { new LibraryFolder { Path = _root } };
        var one = Item(Path.Combine("Artist", "One.mp3"), "One");
        var two = Item(Path.Combine("Artist", "Two.mp3"), "Two");
        var first = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse(sources, [one, two], RemoteFolderCatalog.IdFor(_root))!);
        var reordered = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse(sources, [two, one], RemoteFolderCatalog.IdFor(_root).ToLowerInvariant())!);
        Assert.Equal(Assert.Single(first.Folders).Id, Assert.Single(reordered.Folders).Id);
    }

    [Fact]
    public void DisplayCapDoesNotTruncateFolderPlaybackAndMusicSortOrderIsPreserved()
    {
        var items = Enumerable.Range(1, 510).Select(number => new MediaItem
        {
            Path = Path.Combine(_root, number + ".mp3"), Title = "Track " + number, Artist = number <= 255 ? "Alpha" : "Zebra",
            Album = "Album", DiscNumber = (uint)(number % 2 + 1), TrackNumber = (uint)number
        }).Reverse().ToArray();
        var page = RemoteFolderCatalog.Browse([new() { Path = _root }], items, RemoteFolderCatalog.IdFor(_root))!;
        var remote = RemoteFolderCatalog.ToRemote(page);
        Assert.Equal(500, remote.Items.Count); Assert.Equal(510, remote.TotalItems); Assert.Equal(510, remote.DescendantCount);
        Assert.Equal(510, page.Descendants.Count);
        Assert.Equal(new uint[] { 2, 4, 6 }, page.Descendants.Take(3).Select(item => item.TrackNumber));
        Assert.True(page.Descendants.Take(255).All(item => item.Artist == "Alpha"));
        Assert.Equal(page.Files.Select(item => item.Id), page.Descendants.Select(item => item.Id));
    }

    [Fact]
    public void AlbumOrderingPrecedesDiscAndTrackNumbersWithinEachArtist()
    {
        MediaItem Track(string id, string artist, string album, uint disc, uint track, string title) => new()
        {
            Id = id, Path = Path.Combine(_root, id + ".mp3"), Artist = artist, Album = album,
            DiscNumber = disc, TrackNumber = track, Title = title
        };
        var items = new[]
        {
            Track("A", "Alpha", "Beta", 1, 1, "Alpha"), Track("B", "Alpha", "Alpha", 2, 9, "Beta"),
            Track("C", "Beta", "Alpha", 1, 1, "Alpha"), Track("D", "Alpha", "Beta", 1, 2, "Beta"),
            Track("E", "Alpha", "Alpha", 1, 3, "Beta")
        };
        var page = RemoteFolderCatalog.Browse([new() { Path = _root }], items, RemoteFolderCatalog.IdFor(_root))!;
        Assert.Equal(new[] { "E", "B", "A", "D", "C" }, page.Descendants.Select(item => item.Id));
    }

    [Fact]
    public void DriveRootTitlesDoNotReturnAFileSystemPath()
    {
        var drive = Path.GetPathRoot(_root)!;
        var home = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse([new() { Path = drive }], [], null)!);
        var row = Assert.Single(home.Folders);
        Assert.NotEqual(drive, row.Name);
        var rootPage = RemoteFolderCatalog.ToRemote(RemoteFolderCatalog.Browse([new() { Path = drive }], [], row.Id)!);
        Assert.Equal(row.Name, rootPage.Title);
        Assert.DoesNotContain(drive, JsonSerializer.Serialize(rootPage));
    }
}
