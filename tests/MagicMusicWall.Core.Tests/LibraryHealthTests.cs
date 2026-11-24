using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class LibraryHealthTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-health-" + Guid.NewGuid().ToString("N"));
    public LibraryHealthTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public void HealthReportsPossibleDuplicatesMissingMetadataAndUnavailableWithoutMutating()
    {
        var catalog = new LibraryCatalog(Path.Combine(_directory, "data"));
        var path = Path.Combine(_directory, "exists.mp3"); File.WriteAllText(path, "original");
        catalog.Document.Items.AddRange([
            new() { Path = path, Title = "Café", Artist = "ARTIST", Album = "Album", DurationSeconds = 60.1 },
            new() { Path = Path.Combine(_directory, "missing.mp3"), Title = "Cafe", Artist = "artist", DurationSeconds = 60.2 },
            new() { Path = Path.Combine(_directory, "different.mp3"), Title = "Cafe", Artist = "artist", DurationSeconds = 65 }]);
        var report = catalog.InspectHealth();
        Assert.Equal(2, report.Unavailable.Count); Assert.Equal(2, report.MissingMetadata.Count);
        Assert.Equal(2, Assert.Single(report.PossibleDuplicates).Count);
        Assert.False(File.Exists(catalog.LibraryPath)); Assert.Equal("original", File.ReadAllText(path));
    }
    [Fact]
    public void ReviewedRelinkKeepsIdPlaylistOccurrencesAndMetadata()
    {
        var catalog = new LibraryCatalog(Path.Combine(_directory, "data"));
        var item = new MediaItem { Id = "one", Path = Path.Combine(_directory, "missing.mp3"), Title = "Saved title" };
        catalog.Document.Items.Add(item); catalog.SetPlaylistItems(catalog.Document.Playlists[0].Id, ["one", "one"]);
        var replacement = Path.Combine(_directory, "replacement.mp3"); File.WriteAllText(replacement, "never rewritten");
        catalog.RelinkItem(item.Id, replacement);
        Assert.Equal(replacement, item.Path); Assert.Equal("Saved title", item.Title);
        Assert.Equal(["one", "one"], catalog.Document.Playlists[0].ItemIds);
        Assert.Equal("never rewritten", File.ReadAllText(replacement));
        Assert.Throws<InvalidOperationException>(() => catalog.RelinkItem(item.Id, replacement));
    }
    [Fact]
    public void RelinkRejectsAlreadyIndexedReplacement()
    {
        var catalog = new LibraryCatalog(Path.Combine(_directory, "data"));
        var missing = Path.Combine(_directory, "missing.mp3"); var existing = Path.Combine(_directory, "exists.mp3"); File.WriteAllText(existing, "original");
        catalog.Document.Items.AddRange([new() { Id = "one", Path = missing }, new() { Id = "two", Path = existing }]);
        Assert.Throws<InvalidOperationException>(() => catalog.RelinkItem("one", existing));
        Assert.Equal(missing, catalog.Document.Items[0].Path);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
