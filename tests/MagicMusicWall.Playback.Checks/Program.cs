using System.Diagnostics;
using System.Text.Json;
using LibVLCSharp.Shared;
using MagicMusicWall.App;

if (!OperatingSystem.IsWindows() || args.Length is < 2 or > 3 || args[0] != "--native-dir"
    || (args.Length == 3 && args[2] != "--legacy"))
{
    Console.Error.WriteLine("Windows x64 only. Usage: --native-dir <folder containing libvlc.dll> [--legacy]");
    return 2;
}

var nativeDirectory = Path.GetFullPath(args[1]);
if (!File.Exists(Path.Combine(nativeDirectory, "libvlc.dll")))
{
    Console.Error.WriteLine("The explicitly supplied native directory does not contain libvlc.dll.");
    return 2;
}

var legacy = args.Length == 3;
var results = new List<Observation>();
var fixtureDirectory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-Playback-" + Guid.NewGuid().ToString("N"));
var fixture = Path.Combine(fixtureDirectory, "silence.wav");
string? error = null;
try
{
    Directory.CreateDirectory(fixtureDirectory);
    WriteSilentWave(fixture);
    Core.Initialize(nativeDirectory);
    var explicitDevice = await CheckOutput("Windows default", "");
    if (string.IsNullOrWhiteSpace(explicitDevice))
        throw new InvalidOperationException("No explicit endpoint is available for the second check.");
    await CheckOutput("Explicit available endpoint", explicitDevice);
}
catch (Exception ex)
{
    error = ex.Message;
}
finally
{
    // Delete only the single fixture and unique empty directory created above.
    if (File.Exists(fixture)) File.Delete(fixture);
    if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory);
}

Console.WriteLine(JsonSerializer.Serialize(new { Passed = error is null, Legacy = legacy, Error = error, Checks = results },
    new JsonSerializerOptions { WriteIndented = true }));
return error is null ? 0 : 1;

async Task<string?> CheckOutput(string outputLabel, string device)
{
    using var vlc = PlaybackEngine.Create(21, headless: true);
    using var player = new MediaPlayer(vlc);
    PlaybackEngine.ConfigureOutput(player, device, moveActive: false, recreate: true);
    player.Volume = 21;
    player.Mute = false;
    // Windows session callbacks are asynchronous, including between the two runs.
    await WaitFor(() => player.Volume == 21 && !player.Mute, "initial volume and mute did not settle");

    try
    {
        await Next("initial", 21, false);
        var actualDevice = player.OutputDevice;
        await Slider("slider-73", 73);
        await Next("next-73", 73, false);
        await Next("next-again-73", 73, false);
        await Slider("slider-29", 29);
        await Next("next-29", 29, false);
        await Slider("slider-zero", 0);
        await Next("next-zero", 0, false);
        await Slider("slider-29-before-mute", 29);
        player.Mute = true;
        await WaitFor(() => player.Mute && player.Volume == 29, "intentional mute did not settle");
        Observe("intentional-mute", 29, true);
        await Next("next-muted", 29, true);
        await Slider("slider-unmute", 29);
        await Next("next-unmuted", 29, false);
        if (device.Length > 0 && !string.Equals(device, player.OutputDevice, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The explicitly selected audio endpoint changed during the check.");
        // VLC can report an empty ID for the default route. In that case use
        // one available endpoint from its inventory, without changing Windows.
        return !string.IsNullOrWhiteSpace(actualDevice) ? actualDevice
            : vlc.AudioOutputDevices("mmdevice").Select(d => d.DeviceIdentifier).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
    }
    finally { player.Stop(); }

    async Task Slider(string stage, int volume)
    {
        player.Volume = volume;
        player.Mute = false;
        await WaitFor(() => player.Volume == volume && !player.Mute, stage + " did not settle");
        Observe(stage, volume, false);
    }

    async Task Next(string stage, int volume, bool expectedMute)
    {
        var preservedMute = player.Mute;
        player.Stop();
        if (!player.SetRenderer(null)) throw new InvalidOperationException("Could not select local playback.");
        PlaybackEngine.ConfigureOutput(player, device, moveActive: false, recreate: !legacy);
        player.Volume = volume;
        player.Mute = preservedMute;
        using var media = new Media(vlc, new Uri(fixture));
        if (!player.Play(media)) throw new InvalidOperationException(stage + ": Play was rejected.");
        await WaitFor(() => player.IsPlaying && player.Time > 0, stage + ": playback did not start");
        // Observe the actual output after creation/Windows session notification,
        // without correcting it or waiting until it happens to match the assertion.
        await Task.Delay(250);
        Observe(stage, volume, expectedMute);
    }

    void Observe(string stage, int expectedVolume, bool expectedMute)
    {
        var observation = new Observation(outputLabel, stage, expectedVolume, player.Volume, expectedMute, player.Mute, player.State.ToString());
        results.Add(observation);
        if (observation.ActualVolume != expectedVolume || observation.ActualMute != expectedMute)
            throw new InvalidOperationException($"{outputLabel}/{stage}: expected volume {expectedVolume}, mute {expectedMute}; got volume {observation.ActualVolume}, mute {observation.ActualMute}.");
    }
}

static async Task WaitFor(Func<bool> predicate, string failure)
{
    var elapsed = Stopwatch.StartNew();
    while (!predicate())
    {
        if (elapsed.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(failure);
        await Task.Delay(40);
    }
}

static void WriteSilentWave(string path)
{
    const int bytes = 8000 * 2 * 30;
    using var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew));
    writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
    writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes);
    writer.Write(new byte[bytes]);
}

record Observation(string Output, string Stage, int ExpectedVolume, int ActualVolume, bool ExpectedMute, bool ActualMute, string State);
