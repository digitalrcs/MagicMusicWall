using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class AppPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-Preferences-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void MissingSettingsUseDefaultsAndSaveCreatesDirectory()
    {
        var preferences = AppPreferences.Load(_directory);
        Assert.True(preferences.QueueExternalFiles);
        Assert.Equal(65, preferences.Volume);
        Assert.Equal(8787, preferences.RemotePort);
        Assert.False(preferences.RemoteEnabled);
        Assert.Null(preferences.LoadWarning);
        preferences.Save(_directory);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void LegacySettingsLoadWithoutLosingAudioPreferences()
    {
        WriteSettings("""{"volume":23,"audioDeviceId":"stereo-output"}""");
        var preferences = AppPreferences.Load(_directory);
        Assert.True(preferences.QueueExternalFiles);
        Assert.Equal(23, preferences.Volume);
        Assert.Equal("stereo-output", preferences.AudioDeviceId);
        Assert.Equal(8787, preferences.RemotePort);
        Assert.False(preferences.RemoteEnabled);
        Assert.Null(preferences.LoadWarning);
    }

    [Fact]
    public void NewPreferencesRoundTripAndCopyIsIndependent()
    {
        var preferences = new AppPreferences
        {
            Volume = 27, AudioDeviceId = "stereo", AudioDeviceName = "Living room",
            Shuffle = true, RepeatQueue = true, RescanOnStartup = true,
            SharingAddress = "192.0.2.10", SharingPort = 9000,
            RemoteEnabled = true, RemoteAddress = "192.0.2.11", RemotePort = 8788,
            RemoteAccessToken = "synthetic-test-value"
        };
        var copy = preferences.Copy();
        copy.Volume = 11;
        Assert.Equal(27, preferences.Volume);
        preferences.Save(_directory);
        var loaded = AppPreferences.Load(_directory);
        Assert.Equal(JsonSerializer.Serialize(preferences), JsonSerializer.Serialize(loaded));
        Assert.DoesNotContain("LoadWarning", File.ReadAllText(SettingsPath));
        loaded.Volume = 42;
        loaded.Save(_directory);
        Assert.Equal(42, AppPreferences.Load(_directory).Volume);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExternalFileQueuePreferenceRoundTrips(bool enqueue)
    {
        var preferences = new AppPreferences { QueueExternalFiles = enqueue };
        preferences.Save(_directory);
        Assert.Equal(enqueue, AppPreferences.Load(_directory).QueueExternalFiles);
        var copy = preferences.Copy();
        copy.QueueExternalFiles = !enqueue;
        Assert.Equal(enqueue, preferences.QueueExternalFiles);
    }

    [Theory]
    [InlineData(-1, -1, 0, 0, 0, 8787)]
    [InlineData(101, 65536, 65536, 100, 0, 8787)]
    [InlineData(0, 0, 1, 0, 0, 1)]
    [InlineData(100, 65535, 65535, 100, 65535, 65535)]
    public void PortsAndVolumeNormalizeOnLoadAndSave(int volume, int sharingPort, int remotePort, int expectedVolume, int expectedSharingPort, int expectedRemotePort)
    {
        WriteSettings(JsonSerializer.Serialize(new { Volume = volume, SharingPort = sharingPort, RemotePort = remotePort }));
        var loaded = AppPreferences.Load(_directory);
        Assert.Equal(expectedVolume, loaded.Volume);
        Assert.Equal(expectedSharingPort, loaded.SharingPort);
        Assert.Equal(expectedRemotePort, loaded.RemotePort);
        var direct = new AppPreferences { Volume = volume, SharingPort = sharingPort, RemotePort = remotePort };
        direct.Save(_directory);
        var reloaded = AppPreferences.Load(_directory);
        Assert.Equal(expectedVolume, reloaded.Volume);
        Assert.Equal(expectedSharingPort, reloaded.SharingPort);
        Assert.Equal(expectedRemotePort, reloaded.RemotePort);
    }

    [Fact]
    public void NullStringsBecomeEmpty()
    {
        WriteSettings("""{"AudioDeviceId":null,"AudioDeviceName":null,"SharingAddress":null,"RemoteAddress":null,"RemoteAccessToken":null}""");
        var preferences = AppPreferences.Load(_directory);
        Assert.Equal("", preferences.AudioDeviceId);
        Assert.Equal("", preferences.AudioDeviceName);
        Assert.Equal("", preferences.SharingAddress);
        Assert.Equal("", preferences.RemoteAddress);
        Assert.Equal("", preferences.RemoteAccessToken);
    }

    [Theory]
    [InlineData("{ malformed, preserve this exactly")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Volume\":\"loud\"}")]
    public void MalformedSettingsArePreservedBeforeReplacement(string original)
    {
        WriteSettings(original);
        var preferences = AppPreferences.Load(_directory);
        Assert.NotNull(preferences.LoadWarning);
        Assert.Equal(65, preferences.Volume);
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        preferences.Save(_directory);
        Assert.Null(preferences.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_directory, "settings.corrupt.*.json"));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(65, AppPreferences.Load(_directory).Volume);
        preferences.Save(_directory);
        Assert.Single(Directory.GetFiles(_directory, "settings.corrupt.*.json"));
    }

    [Fact]
    public void LockedExistingSettingsCannotBeOverwrittenByDefaults()
    {
        // File sharing is enforced by Windows, the app's supported runtime.
        if (!OperatingSystem.IsWindows()) return;
        WriteSettings("""{"Volume":18}""");
        using (var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var preferences = AppPreferences.Load(_directory);
            Assert.NotNull(preferences.LoadWarning);
            Assert.Throws<IOException>(() => preferences.Save(_directory));
        }
        Assert.Equal(18, AppPreferences.Load(_directory).Volume);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private void WriteSettings(string text)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
