using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public class FileActivationTests
{
    private static readonly string Root = Path.GetFullPath("activation-tests");

    [Fact]
    public void BareAndPlayPathsPreserveSpacesUnicodeAndOrder()
    {
        Assert.Equal(new[] { Path.Combine(Root, "Björk one.mp3"), Path.Combine(Root, "two.flac") },
            FileActivation.ParsePaths(["Björk one.mp3", "--play", "two.flac"], Root));
    }

    [Fact]
    public void OptionsAndTheirValuesAreNotPlayed()
    {
        Assert.Equal(new[] { Path.Combine(Root, "song.wav") }, FileActivation.ParsePaths(
            ["--data-dir", "library", "--import", "import.mp3", "--video-test", "test.mp4",
             "--portable", "--smoke-test", "--unknown", "song.wav"], Root));
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--import")]
    [InlineData("--play")]
    public void MissingValuesProduceNoPaths(string option) => Assert.Empty(FileActivation.ParsePaths([option], Root));

    [Fact]
    public void DuplicatePathsKeepFirstSpelling()
    {
        Assert.Equal(new[] { Path.Combine(Root, "Song.MP3") },
            FileActivation.ParsePaths(["Song.MP3", "song.mp3", Path.Combine(Root, "Song.MP3")], Root));
    }

    [Theory]
    [InlineData("song.MP3", true)]
    [InlineData("song.ape", true)]
    [InlineData("song.wave", true)]
    [InlineData("song.exe", false)]
    public void SupportedMediaValidation(string path, bool expected) => Assert.Equal(expected, LibraryCatalog.IsSupported(path));
}
