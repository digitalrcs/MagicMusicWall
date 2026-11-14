using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class MediaTagServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MagicMusicWall-TagTests-" + Guid.NewGuid().ToString("N"));

    public MediaTagServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SavesChosenTagsAndPreservesUnselectedTagsArtworkAudioAndExactBackup()
    {
        var path = Wave();
        var picture = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
        using (var media = TagLib.File.Create(path))
        {
            media.Tag.Title = "Original title";
            media.Tag.Performers = ["Artist one", "Artist two"];
            media.Tag.Album = "Original album";
            media.Tag.AlbumArtists = ["Album artist"];
            media.Tag.Genres = ["Jazz", "Fusion"];
            media.Tag.Comment = "Keep this comment";
            media.Tag.Year = 1981;
            media.Tag.Track = 4; media.Tag.TrackCount = 9;
            media.Tag.Disc = 1; media.Tag.DiscCount = 2;
            media.Tag.Lyrics = "Keep these lyrics";
            media.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(picture)) { MimeType = "image/png", Type = TagLib.PictureType.FrontCover }];
            media.Save();
        }
        var original = await File.ReadAllBytesAsync(path);
        var audio = WaveData(path);
        var result = await MediaTagService.WriteAsync(path, new() { Title = "Updated title", Album = "Updated album", Year = 1982 });

        Assert.Equal(original, await File.ReadAllBytesAsync(result.BackupPath));
        Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(result.BackupPath));
        Assert.EndsWith(".bak", result.BackupPath);
        Assert.Equal("Updated title", result.Tags.Title);
        Assert.Equal("Updated album", result.Tags.Album);
        Assert.Equal(1982u, result.Tags.Year);
        Assert.Equal(audio, WaveData(path));
        using var saved = TagLib.File.Create(path);
        Assert.Equal(new[] { "Artist one", "Artist two" }, saved.Tag.Performers);
        Assert.Equal("Album artist", Assert.Single(saved.Tag.AlbumArtists));
        Assert.Equal(new[] { "Jazz", "Fusion" }, saved.Tag.Genres);
        Assert.Equal("Keep this comment", saved.Tag.Comment);
        Assert.Equal("Keep these lyrics", saved.Tag.Lyrics);
        Assert.Equal(4u, saved.Tag.Track); Assert.Equal(9u, saved.Tag.TrackCount);
        Assert.Equal(1u, saved.Tag.Disc); Assert.Equal(2u, saved.Tag.DiscCount);
        Assert.Equal(picture, Assert.Single(saved.Tag.Pictures).Data.Data);
        Assert.Empty(Directory.GetFiles(_root, ".mmw-edit-*"));
    }

    [Fact]
    public async Task ExplicitEmptyAndZeroClearOnlySelectedFieldsAndEverySaveHasDistinctBackup()
    {
        var path = Wave();
        var first = await MediaTagService.WriteAsync(path, new()
        {
            Title = "A title", Artist = "An artist", Album = "An album", AlbumArtist = "An album artist",
            Genre = "Rock", Comment = "A comment\nSecond line", Year = 2000, TrackNumber = 2, DiscNumber = 3
        });
        var beforeSecondSave = await File.ReadAllBytesAsync(path);
        var second = await MediaTagService.WriteAsync(path, new() { Album = "", Year = 0, TrackNumber = 0 });
        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.Equal(beforeSecondSave, await File.ReadAllBytesAsync(second.BackupPath));
        Assert.Equal("", second.Tags.Album); Assert.Equal(0u, second.Tags.Year); Assert.Equal(0u, second.Tags.TrackNumber);
        Assert.Equal("An artist", second.Tags.Artist); Assert.Equal("An album artist", second.Tags.AlbumArtist);
        Assert.Equal("A title", second.Tags.Title); Assert.Equal("Rock", second.Tags.Genre);
        Assert.Equal("A comment\nSecond line", second.Tags.Comment); Assert.Equal(3u, second.Tags.DiscNumber);
    }

    [Fact]
    public async Task ReaderThatDisallowsReplacementLeavesOriginalUnchangedAndRemovesStagedCopy()
    {
        var path = Wave();
        var original = await File.ReadAllBytesAsync(path);
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Assert.ThrowsAsync<IOException>(() => MediaTagService.WriteAsync(path, new() { Title = "Cannot save" }));
            Assert.Contains("atomically replace", error.Message);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
        Assert.Empty(Directory.GetFiles(_root, ".mmw-edit-*"));
    }

    [Fact]
    public async Task InvalidInputsReadOnlyAndPreCancellationDoNotAlterTheFile()
    {
        var path = Wave();
        var original = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<ArgumentException>(() => MediaTagService.WriteAsync(path, new()));
        await Assert.ThrowsAsync<ArgumentException>(() => MediaTagService.WriteAsync(path, new() { Title = "bad\0title" }));
        await Assert.ThrowsAsync<ArgumentException>(() => MediaTagService.WriteAsync(path, new() { Title = new string('x', 1025) }));
        await Assert.ThrowsAsync<ArgumentException>(() => MediaTagService.WriteAsync(path, new() { Year = 10000 }));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaTagService.WriteAsync(path, new() { Title = "Cancelled" }, cancel.Token));
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => MediaTagService.WriteAsync(path, new() { Title = "Read only" })); }
        finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task UnsupportedAndMalformedContainersAreNeverChanged()
    {
        var unsupported = Path.Combine(_root, "video.mkv");
        var corrupt = Path.Combine(_root, "damaged.mp3");
        await File.WriteAllTextAsync(unsupported, "original unsupported bytes");
        await File.WriteAllTextAsync(corrupt, "not an MP3");
        Assert.False(MediaTagService.SupportsWriting(unsupported));
        Assert.True(MediaTagService.SupportsWriting("UPPER.MP3"));
        await Assert.ThrowsAsync<NotSupportedException>(() => MediaTagService.WriteAsync(unsupported, new() { Title = "No" }));
        await Assert.ThrowsAsync<TagLib.CorruptFileException>(() => MediaTagService.WriteAsync(corrupt, new() { Title = "No" }));
        Assert.Equal("original unsupported bytes", await File.ReadAllTextAsync(unsupported));
        Assert.Equal("not an MP3", await File.ReadAllTextAsync(corrupt));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    private string Wave()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".wav");
        using var writer = new BinaryWriter(File.Create(path));
        const int bytes = 16000;
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(bytes); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(bytes);
        writer.Write(Enumerable.Range(0, bytes).Select(i => (byte)(i % 251)).ToArray());
        return path;
    }

    private static byte[] WaveData(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 12;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4)); var length = reader.ReadUInt32();
            if (id == "data") return reader.ReadBytes(checked((int)length));
            reader.BaseStream.Seek(length + (length & 1), SeekOrigin.Current);
        }
        throw new InvalidDataException("No audio data chunk found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
