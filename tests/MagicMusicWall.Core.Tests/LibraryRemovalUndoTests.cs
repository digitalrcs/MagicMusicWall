using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class LibraryRemovalUndoTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-undo-" + Guid.NewGuid().ToString("N"));
    private LibraryCatalog Catalog()
    {
        Directory.CreateDirectory(_directory);
        var c = new LibraryCatalog(Path.Combine(_directory, "data"));
        c.Document.Items.AddRange(new[] { "one", "two" }.Select(id => new MediaItem { Id = id, Path = Path.Combine(_directory, id + ".wav"), Title = id }));
        foreach (var item in c.Document.Items) File.WriteAllText(item.Path, "untouched media fixture");
        c.SetPlaylistItems(c.Document.Playlists[0].Id, ["one", "two", "one"]);
        return c;
    }

    [Fact]
    public void RemovalUndoRestoresDuplicateOccurrencesAndNeverChangesMedia()
    {
        var c = Catalog(); var paths = c.Document.Items.Select(i => i.Path).ToArray();
        Assert.Equal(new LibraryRemovalPreview(2, 1), c.PreviewAddedFilesRemoval());
        c.RemoveItems(["one"]);
        Assert.Equal(["two"], c.Document.Playlists[0].ItemIds);
        Assert.True(c.UndoRemoval()); Assert.False(c.UndoRemoval());
        Assert.Equal(["one", "two", "one"], c.Document.Playlists[0].ItemIds);
        var reopened = new LibraryCatalog(Path.Combine(_directory, "data")); reopened.Load();
        Assert.Equal(["one", "two", "one"], reopened.Document.Playlists[0].ItemIds);
        Assert.All(paths, p => Assert.Equal("untouched media fixture", File.ReadAllText(p)));
    }

    [Fact]
    public void InterveningEditRejectsUndoWithoutOverwritingIt()
    {
        var c = Catalog(); c.RemoveItems(["one"]); c.RenamePlaylist(c.Document.Playlists[0].Id, "Changed");
        Assert.False(c.CanUndoRemoval); Assert.False(c.UndoRemoval());
        Assert.Equal("Changed", c.Document.Playlists[0].Name); Assert.Single(c.Document.Items);
    }

    [Fact]
    public void NestedSourcePreservesCoveredTracksAndUndoRestoresSource()
    {
        var c = Catalog(); var nested = Path.Combine(_directory, "album");
        c.Document.Folders.Add(new() { Id = "root", Path = _directory });
        c.Document.Folders.Add(new() { Id = "nested", Path = nested });
        c.Document.Items[0].Path = Path.Combine(nested, "one.wav"); c.Save();
        Assert.Equal(new LibraryRemovalPreview(1, 1), c.PreviewFolderRemoval("root"));
        c.RemoveFolder("root"); Assert.Equal("one", Assert.Single(c.Document.Items).Id);
        Assert.True(c.UndoRemoval()); Assert.Equal(2, c.Document.Folders.Count); Assert.Equal(2, c.Document.Items.Count);
    }

    [Fact]
    public void FailedSaveRollsBackRemovalAndUndoRemainsRetryable()
    {
        var c = Catalog();
        using (var held = File.Open(c.LibraryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => c.RemoveItems(["one"]));
            Assert.Equal(2, c.Document.Items.Count); Assert.False(c.CanUndoRemoval);
        }
        c.RemoveItems(["one"]);
        using (var held = File.Open(c.LibraryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => c.UndoRemoval());
            Assert.Single(c.Document.Items); Assert.True(c.CanUndoRemoval);
        }
        Assert.True(c.UndoRemoval());
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
