using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class PortableLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-PortableTests-" + Guid.NewGuid().ToString("N"));
    public PortableLibraryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MovingPortableFolderRebasesOnlyContainedMediaAndKeepsPlaylistIds()
    {
        var oldBase = Path.Combine(_root, "Before"); var newBase = Path.Combine(_root, "After");
        Directory.CreateDirectory(Path.Combine(newBase, "Media"));
        var newFile = Path.Combine(newBase, "Media", "demo.mp3"); File.WriteAllText(newFile, "synthetic");
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var portable = new MediaItem { Path = Path.Combine(oldBase, "Media", "demo.mp3") };
        var external = new MediaItem { Path = Path.Combine(_root, "Before-other", "external.mp3") };
        catalog.Document.Items.AddRange([portable, external]);
        catalog.Document.Folders.Add(new() { Path = Path.Combine(oldBase, "Media") });
        catalog.PreparePortableLibrary(oldBase);
        var playlist = catalog.CreatePlaylist("USB mix"); catalog.AddToPlaylist(playlist.Id, [external.Id, portable.Id]);
        Assert.Equal(2, catalog.PreparePortableLibrary(newBase));
        Assert.Equal(newFile, portable.Path); Assert.True(portable.IsAvailable);
        Assert.Equal(Path.Combine(_root, "Before-other", "external.mp3"), external.Path);
        var reopened = new LibraryCatalog(Path.Combine(_root, "data")); reopened.Load();
        Assert.Equal(newBase, reopened.Document.PortableBasePath);
        Assert.Equal(new[] { external.Id, portable.Id }, reopened.Document.Playlists.Single(p => p.Id == playlist.Id).ItemIds);
        Assert.Equal(newFile, reopened.Document.Items.Single(i => i.Id == portable.Id).Path);
        Assert.Equal(0, reopened.PreparePortableLibrary(newBase));
    }

    [Fact]
    public void DriveRootBaseAndPathCollisionAreHandledWithoutCorruptingLibrary()
    {
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var driveRoot = Path.GetPathRoot(_root)!; var newBase = Path.Combine(_root, "Portable");
        var item = new MediaItem { Path = Path.Combine(driveRoot, "SyntheticMusic", "test.mp3") };
        catalog.Document.Items.Add(item); catalog.PreparePortableLibrary(driveRoot);
        Assert.Equal(1, catalog.PreparePortableLibrary(newBase));
        Assert.Equal(Path.Combine(newBase, "SyntheticMusic", "test.mp3"), item.Path);
        var laterBase = Path.Combine(_root, "Later");
        catalog.Document.Items.Add(new() { Path = Path.Combine(laterBase, "SyntheticMusic", "test.mp3") });
        catalog.Save(); var before = File.ReadAllBytes(catalog.LibraryPath);
        Assert.Throws<IOException>(() => catalog.PreparePortableLibrary(laterBase));
        Assert.Equal(before, File.ReadAllBytes(catalog.LibraryPath));
        Assert.Equal(newBase, catalog.Document.PortableBasePath);
    }

    [Fact]
    public void FailedCatalogSaveRollsBackPathChanges()
    {
        var catalog = new LibraryCatalog(Path.Combine(_root, "data")); catalog.Load();
        var oldBase = Path.Combine(_root, "Before"); var newBase = Path.Combine(_root, "After");
        var item = new MediaItem { Path = Path.Combine(oldBase, "song.mp3"), IsAvailable = false };
        catalog.Document.Items.Add(item); catalog.PreparePortableLibrary(oldBase);
        var before = File.ReadAllBytes(catalog.LibraryPath);
        using (var locked = new FileStream(catalog.LibraryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => catalog.PreparePortableLibrary(newBase));
        Assert.Equal(Path.Combine(oldBase, "song.mp3"), item.Path);
        Assert.Equal(oldBase, catalog.Document.PortableBasePath);
        Assert.Equal(before, File.ReadAllBytes(catalog.LibraryPath));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
