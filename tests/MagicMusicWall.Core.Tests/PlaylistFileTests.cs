using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class PlaylistFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-m3u-" + Guid.NewGuid().ToString("N"));
    public PlaylistFileTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public void RoundTripPreservesUnicodePathsOrderAndDuplicates()
    {
        var one = new MediaItem { Path = Path.Combine(_directory, "Café one.mp3"), Title = "One\nInjected title" };
        var two = new MediaItem { Path = Path.Combine(_directory, "#two.flac") };
        var path = Path.Combine(_directory, "mix.m3u8");
        PlaylistFile.Write(path, [one, two, one]);
        Assert.Equal([one.Path, two.Path, one.Path], PlaylistFile.ReadPaths(path));
    }
    [Fact]
    public void ReadsCommentsRelativePathsAndFileUrisButRejectsOnlineStreams()
    {
        var path = Path.Combine(_directory, "mix.m3u");
        var media = Path.Combine(_directory, "song.mp3");
        File.WriteAllText(path, $"#EXTM3U\n\n#EXTINF:30,Song\nsong.mp3\n{new Uri(media).AbsoluteUri}\n");
        Assert.Equal([media, media], PlaylistFile.ReadPaths(path));
        File.AppendAllText(path, "https://example.com/stream.mp3");
        Assert.Throws<InvalidDataException>(() => PlaylistFile.ReadPaths(path));
    }
    [Fact]
    public void FailedExportDoesNotReplaceExistingPlaylist()
    {
        var path = Path.Combine(_directory, "mix.m3u8"); File.WriteAllText(path, "original");
        Assert.ThrowsAny<Exception>(() => PlaylistFile.Write(path, [new MediaItem { Path = "invalid\0.mp3" }]));
        Assert.Equal("original", File.ReadAllText(path));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
