using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class LibraryCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-Tests-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    public LibraryCatalogTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SearchCombinesFieldsIgnoresAccentsAndRequiresEveryToken()
    {
        var match = new MediaItem { Title = "Café Alpha", Artist = "Beta", Album = "Gamma", Genre = "Delta", Path = @"C:\Epsilon\song.mp3" };
        var other = new MediaItem { Title = "Alpha", Artist = "Other" };
        Assert.Equal([match], LibraryCatalog.Search([match, other], "CAFE beta gamma delta epsilon").ToArray());
        Assert.Empty(LibraryCatalog.Search([match, other], "cafe queen"));
        Assert.Equal([match, other], LibraryCatalog.Search([match, other], "  \t ").ToArray());
        var floyd = new MediaItem { Title = "Breathe", Artist = "Pink Floyd", Album = "The Dark Side of the Moon" };
        Assert.Equal([floyd], LibraryCatalog.Search([floyd], "dark moon pink").ToArray());
    }

    [Fact]
    public async Task OverlappingFoldersAndManualImportDoNotDuplicateFiles()
    {
        var folder = Path.Combine(_root, "music");
        var nested = Path.Combine(folder, "album");
        var file = WriteWave(nested, "Breathe.wav");
        var catalog = NewCatalog();
        Assert.Equal(1, (await catalog.AddFolderAsync(folder)).Added);
        var id = Assert.Single(catalog.Document.Items).Id;
        await catalog.AddFolderAsync(nested);
        await catalog.AddFilesAsync([file, file.ToUpperInvariant()]);
        var item = Assert.Single(catalog.Document.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal(1, item.DurationSeconds, 2);
        Assert.Equal(2, catalog.Document.Folders.Count);
        Assert.Equal("0:01", item.DurationText);
    }

    [Fact]
    public async Task PlaylistOrderAndIdsSurviveRestartAndLibraryRemovalNeverDeletesFiles()
    {
        var first = WriteWave(Path.Combine(_root, "music"), "One.wav");
        var second = WriteWave(Path.Combine(_root, "music"), "Two.wav");
        var catalog = NewCatalog();
        await catalog.AddFilesAsync([first, second]);
        var one = catalog.Document.Items[0].Id;
        var two = catalog.Document.Items[1].Id;
        var playlist = catalog.CreatePlaylist("  Evening  ");
        var jazz = catalog.Document.Playlists.Single(p => p.Name == "Jazz");
        catalog.AddToPlaylist(playlist.Id, [two, one, two, "unknown"]);
        catalog.AddToPlaylist(jazz.Id, [one]);
        Assert.Equal([two, one], playlist.ItemIds);

        var reopened = NewCatalog();
        Assert.Equal([two, one], reopened.Document.Playlists.Single(p => p.Id == playlist.Id).ItemIds);
        reopened.RemoveItems([one]);
        Assert.Equal([two], reopened.Document.Playlists.Single(p => p.Id == playlist.Id).ItemIds);
        Assert.Empty(reopened.Document.Playlists.Single(p => p.Id == jazz.Id).ItemIds);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(two, Assert.Single(NewCatalog().Document.Items).Id);
    }

    [Fact]
    public void PlaylistNamesAreTrimmedAndValidatedAndDeletionPersists()
    {
        var catalog = NewCatalog();
        Assert.Equal(["Dinner", "Jazz", "Rock", "80's"], catalog.Document.Playlists.Select(p => p.Name));
        var playlist = catalog.CreatePlaylist("  Evening  ");
        Assert.Equal("Evening", playlist.Name);
        Assert.Empty(playlist.ItemIds);
        Assert.Throws<ArgumentException>(() => catalog.CreatePlaylist("evening"));
        Assert.Throws<ArgumentException>(() => catalog.CreatePlaylist("  "));
        Assert.Throws<ArgumentException>(() => catalog.RenamePlaylist(playlist.Id, "jazz"));
        catalog.RenamePlaylist(playlist.Id, "EVENING");
        var dinnerId = catalog.Document.Playlists.Single(p => p.Name == "Dinner").Id;
        catalog.DeletePlaylist(dinnerId);
        Assert.DoesNotContain(NewCatalog().Document.Playlists, p => p.Name == "Dinner");
    }

    [Fact]
    public async Task MissingFolderRescanPreservesItemsAndPlaylistThenReconnectRecovers()
    {
        var folder = Path.Combine(_root, "music");
        WriteWave(folder, "One.wav");
        var catalog = NewCatalog();
        await catalog.AddFolderAsync(folder);
        var id = Assert.Single(catalog.Document.Items).Id;
        var playlistId = catalog.Document.Playlists[0].Id;
        catalog.AddToPlaylist(playlistId, [id]);
        var disconnected = Path.Combine(_root, "disconnected");
        Directory.Move(folder, disconnected);
        var report = await catalog.RescanAsync();
        Assert.False(Assert.Single(catalog.Document.Items).IsAvailable);
        Assert.Equal([id], catalog.Document.Playlists[0].ItemIds);
        Assert.NotEmpty(report.Warnings);
        Assert.False(Assert.Single(NewCatalog().Document.Items).IsAvailable);
        Directory.Move(disconnected, folder);
        await catalog.RescanAsync();
        Assert.True(Assert.Single(catalog.Document.Items).IsAvailable);
        Assert.Equal(id, catalog.Document.Items[0].Id);
    }

    [Fact]
    public async Task RescanUpdatesTagsTwiceWithoutChangingIdOrDuplicating()
    {
        var file = WriteWave(Path.Combine(_root, "music"), "untagged.wav");
        var catalog = NewCatalog();
        await catalog.AddFilesAsync([file]);
        var id = Assert.Single(catalog.Document.Items).Id;
        using (var tags = TagLib.File.Create(file))
        {
            tags.Tag.Title = "Breathe";
            tags.Tag.Performers = ["Pink Floyd"];
            tags.Tag.Album = "The Dark Side of the Moon";
            tags.Tag.Track = 2;
            tags.Save();
        }
        await catalog.RescanAsync();
        await catalog.RescanAsync();
        var item = Assert.Single(catalog.Document.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal("Breathe", item.Title);
        Assert.Equal("Pink Floyd", item.Artist);
        Assert.Equal("The Dark Side of the Moon", item.Album);
        Assert.Equal(2u, item.TrackNumber);
    }

    [Fact]
    public async Task MetadataReadFailurePreservesPreviousTags()
    {
        var file = WriteWave(Path.Combine(_root, "music"), "One.wav");
        using (var tags = TagLib.File.Create(file)) { tags.Tag.Title = "Known title"; tags.Save(); }
        var catalog = NewCatalog();
        await catalog.AddFilesAsync([file]);
        File.WriteAllText(file, "damaged wave");
        var report = await catalog.RescanAsync();
        Assert.Equal("Known title", Assert.Single(catalog.Document.Items).Title);
        Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public async Task ImportAndRescanLeaveMediaBytesUnchanged()
    {
        var file = WriteWave(Path.Combine(_root, "music"), "One.wav");
        var original = SHA256.HashData(File.ReadAllBytes(file));
        var catalog = NewCatalog();
        await catalog.AddFolderAsync(Path.GetDirectoryName(file)!);
        await catalog.RescanAsync();
        catalog.RemoveItems(catalog.Document.Items.Select(i => i.Id).ToArray());
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(file)));
    }

    [Fact]
    public async Task RemovingFolderOnlyStopsDiscoveryAndKeepsImportedItems()
    {
        var file = WriteWave(Path.Combine(_root, "music"), "One.wav");
        var catalog = NewCatalog();
        await catalog.AddFolderAsync(Path.GetDirectoryName(file)!);
        catalog.RemoveFolder(catalog.Document.Folders[0].Id);
        Assert.Empty(catalog.Document.Folders);
        Assert.Single(catalog.Document.Items);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task CanceledImportDoesNotChangeCatalogOrCreatePersistenceFile()
    {
        var file = WriteWave(Path.Combine(_root, "music"), "One.wav");
        var catalog = NewCatalog();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.AddFilesAsync([file], ct: cancellation.Token));
        Assert.Empty(catalog.Document.Items);
        Assert.False(File.Exists(catalog.LibraryPath));
    }

    [Fact]
    public void DamagedLibraryIsPreservedAndPreviousBackupCanRecover()
    {
        var catalog = NewCatalog();
        var original = catalog.CreatePlaylist("Saved playlist");
        catalog.Save(); // Both the main file and previous generation contain the playlist.
        File.WriteAllText(catalog.LibraryPath, "{not valid JSON");
        var recovered = NewCatalog();
        Assert.NotNull(recovered.LoadWarning);
        Assert.Contains(recovered.Document.Playlists, p => p.Id == original.Id);
        var preserved = Assert.Single(Directory.GetFiles(Data, "library.corrupt.*.json"));
        Assert.Equal("{not valid JSON", File.ReadAllText(preserved));
        recovered.CreatePlaylist("After recovery");
        Assert.Contains(NewCatalog().Document.Playlists, p => p.Name == "After recovery");
    }

    [Fact]
    public void DamagedLibraryWithoutBackupIsPreservedBeforeEmptyRecovery()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "library.json"), "[]");
        var catalog = NewCatalog();
        Assert.NotNull(catalog.LoadWarning);
        Assert.Empty(catalog.Document.Items);
        Assert.Single(Directory.GetFiles(Data, "library.corrupt.*.json"));
        catalog.Save();
        Assert.Empty(NewCatalog().Document.Items);
    }

    [Fact]
    public void FutureLibraryVersionIsNotOverwritten()
    {
        Directory.CreateDirectory(Data);
        var json = JsonSerializer.Serialize(new LibraryDocument { SchemaVersion = 99 });
        var path = Path.Combine(Data, "library.json");
        File.WriteAllText(path, json);
        var catalog = new LibraryCatalog(Data);
        Assert.ThrowsAny<IOException>(catalog.Load);
        Assert.Throws<InvalidOperationException>(catalog.Save);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public async Task UnsupportedAndMalformedMediaDoNotPreventOtherImports()
    {
        var folder = Path.Combine(_root, "music");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not media");
        File.WriteAllText(Path.Combine(folder, "damaged.mp3"), "invalid MP3");
        WriteWave(folder, "good.wav");
        var catalog = NewCatalog();
        var result = await catalog.AddFolderAsync(folder);
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(catalog.Document.Items, i => i.Title == "damaged");
    }

    private LibraryCatalog NewCatalog()
    {
        var catalog = new LibraryCatalog(Data);
        catalog.Load();
        return catalog;
    }

    private static string WriteWave(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        using var writer = new BinaryWriter(File.Create(path));
        const int bytes = 8000 * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
