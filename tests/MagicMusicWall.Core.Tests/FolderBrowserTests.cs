using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public class FolderBrowserTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicFolders");
    private MediaItem Track(string relative) => new() { Path = Path.Combine(_root, relative), Title = relative };
    [Fact]
    public void ShowsImmediateFoldersAndFilesWithRecursiveCounts()
    {
        var root = new LibraryFolder { Path = _root };
        var direct = Track("Intro.mp3"); var album = Track(Path.Combine("Artist", "Album", "Song.mp3"));
        var page = FolderBrowser.Browse([root], [direct, album], _root);
        Assert.Equal("Artist", Assert.Single(page.Folders).Name); Assert.Single(page.Files); Assert.Equal(2, page.Descendants.Count);
        var next = FolderBrowser.Browse([root], [direct, album], page.Folders[0].Path);
        Assert.Equal("Album", Assert.Single(next.Folders).Name); Assert.Empty(next.Files); Assert.Equal(_root, next.Parent);
    }
    [Fact]
    public void HomeGroupsLooseFilesAndDoesNotConfuseSiblingPrefix()
    {
        var root = new LibraryFolder { Path = _root };
        var inside = Track("Song.mp3"); var outside = new MediaItem { Path = Path.Combine(_root + "-other", "Other.mp3") };
        var page = FolderBrowser.Browse([root], [inside, outside], null);
        Assert.Equal(2, page.Folders.Count); Assert.Empty(page.Files);
        Assert.Equal(1, page.Folders.Single(f => f.Path == _root).Count);
        Assert.Equal(outside, Assert.Single(FolderBrowser.Browse([root], [inside, outside], FolderBrowser.LooseFiles).Files));
    }
    [Fact]
    public void OfflineFilesKeepTheirFolderAndTraversalOutsideRootsIsRejected()
    {
        var item = Track(Path.Combine("Offline album", "Track.flac")); item.IsAvailable = false;
        Assert.Single(FolderBrowser.Browse([new() { Path = _root }], [item], _root).Folders);
        Assert.Throws<ArgumentException>(() => FolderBrowser.Browse([new() { Path = _root }], [item], Path.Combine(_root, "..")));
    }

    [Fact]
    public void ExistingUnindexedMediaOnDiskDoesNotAppearInTheLibrary()
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var root = Path.Combine(temporaryRoot, "MagicMusicWall-Browse-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Unindexed album"));
            var addedPath = Path.Combine(root, "Added.mp3");
            File.WriteAllText(addedPath, "");
            File.WriteAllText(Path.Combine(root, "Not added.mp3"), "");
            File.WriteAllText(Path.Combine(root, "Unindexed album", "Not added either.flac"), "");
            var added = new MediaItem { Path = addedPath, Title = "Added" };

            var page = FolderBrowser.Browse([new() { Path = root }], [added], root);

            Assert.Same(added, Assert.Single(page.Files));
            Assert.Same(added, Assert.Single(page.Descendants));
            Assert.Empty(page.Folders);
        }
        finally
        {
            var resolvedRoot = Path.GetFullPath(root);
            if (resolvedRoot.StartsWith(Path.TrimEndingDirectorySeparator(temporaryRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(resolvedRoot)) Directory.Delete(resolvedRoot, recursive: true);
        }
    }
}
