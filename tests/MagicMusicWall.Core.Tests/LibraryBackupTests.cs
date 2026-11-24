using System.IO.Compression;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class LibraryBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-backup-" + Guid.NewGuid().ToString("N"));
    public LibraryBackupTests() => Directory.CreateDirectory(_root);
    [Fact]
    public void BackupRoundtripPreservesDataAndCreatesRecoveryArchiveWithoutMedia()
    {
        var data = Path.Combine(_root, "data"); var catalog = new LibraryCatalog(data); catalog.Save();
        var original = File.ReadAllText(catalog.LibraryPath); File.WriteAllText(Path.Combine(data, "private.mp3"), "media");
        var zip = Path.Combine(_root, "backup.zip"); LibraryBackup.Create(data, zip);
        Assert.Equal(["library.json"], LibraryBackup.Inspect(zip));
        catalog.CreatePlaylist("New playlist"); var changed = File.ReadAllText(catalog.LibraryPath);
        var recovery = LibraryBackup.Restore(data, zip);
        Assert.Equal(original, File.ReadAllText(catalog.LibraryPath));
        using var backup = ZipFile.OpenRead(recovery); using var reader = new StreamReader(backup.GetEntry("library.json")!.Open());
        Assert.Equal(changed, reader.ReadToEnd()); Assert.Equal("media", File.ReadAllText(Path.Combine(data, "private.mp3")));
    }
    [Theory]
    [InlineData("../library.json", "{}")]
    [InlineData("library.json", "{\"SchemaVersion\":2,\"Items\":[],\"Playlists\":[]}")]
    [InlineData("library.json", "{\"SchemaVersion\":1,\"Items\":[],\"Playlists\":[{\"Id\":\"smart\",\"ItemIds\":[],\"Rule\":{\"PlayedWithinDays\":0}}]}")]
    [InlineData("settings.json", "not json")]
    public void InvalidArchiveDoesNotTouchExistingData(string name, string content)
    {
        var data = Path.Combine(_root, "data"); var catalog = new LibraryCatalog(data); catalog.Save(); var before = File.ReadAllBytes(catalog.LibraryPath);
        var zip = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(content);
        Assert.ThrowsAny<Exception>(() => LibraryBackup.Restore(data, zip));
        Assert.Equal(before, File.ReadAllBytes(catalog.LibraryPath)); Assert.Single(Directory.GetFiles(data));
    }
    [Fact]
    public void FailedReplacementRollsBackEarlierFiles()
    {
        var source = Path.Combine(_root, "source"); var from = new LibraryCatalog(source); from.Save(); new AppPreferences { Volume = 12 }.Save(source);
        var zip = Path.Combine(_root, "backup.zip"); LibraryBackup.Create(source, zip);
        var data = Path.Combine(_root, "target"); var to = new LibraryCatalog(data); to.Save(); to.CreatePlaylist("Keep me"); new AppPreferences { Volume = 42 }.Save(data);
        var before = File.ReadAllBytes(to.LibraryPath);
        using (var held = File.Open(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => LibraryBackup.Restore(data, zip)) is IOException or UnauthorizedAccessException);
        Assert.Equal(before, File.ReadAllBytes(to.LibraryPath)); Assert.Equal(42, AppPreferences.Load(data).Volume);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
