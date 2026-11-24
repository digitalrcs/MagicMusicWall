using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using MagicMusicWall.App;

if (args.Length != 4 || args[0] != "--native-dir" || args[2] != "--ffmpeg")
{ Console.Error.WriteLine("Usage: --native-dir <bundled libvlc/win-x64> --ffmpeg <ffmpeg.exe>"); return 2; }
if (!File.Exists(Path.Combine(args[1], "libvlc.dll")) || !File.Exists(args[3])) return 2;
Core.Initialize(Path.GetFullPath(args[1]));
var directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-ReplayGainChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var flat = await Fixture("flat", null, null);
    var quiet = await Fixture("quiet", "-6.0 dB", "0.95");
    var loud = await Fixture("loud", "+12.0 dB", "0.95");
    var baseline = await Peak(quiet, false);
    var attenuated = await Peak(quiet, true);
    var protectedPeak = await Peak(loud, true);
    var unchanged = await Peak(flat, true);
    var untaggedBase = await Peak(flat, false);
    Check(Math.Abs(attenuated / baseline - Math.Pow(10, -.3)) < .015, $"-6 dB tag scales decoded output: ratio={attenuated / baseline:F4}");
    Check(Math.Abs(protectedPeak / baseline - 1 / .95) < .02, $"peak protection caps positive gain: ratio={protectedPeak / baseline:F4}");
    Check(Math.Abs(unchanged / untaggedBase - 1) < .005, $"untagged output unchanged: ratio={unchanged / untaggedBase:F4}");
    Console.WriteLine("All checks used audio-memory callbacks; no audio device or source library was used.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
    Directory.Delete(directory);
}

async Task<string> Fixture(string name, string? gain, string? peak)
{
    var path = Path.Combine(directory, name + ".flac");
    var start = new ProcessStartInfo(Path.GetFullPath(args[3])) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2:sample_rate=22050", "-c:a", "flac" }) start.ArgumentList.Add(arg);
    if (gain is not null) { start.ArgumentList.Add("-metadata"); start.ArgumentList.Add("REPLAYGAIN_TRACK_GAIN=" + gain); }
    if (peak is not null) { start.ArgumentList.Add("-metadata"); start.ArgumentList.Add("REPLAYGAIN_TRACK_PEAK=" + peak); }
    start.ArgumentList.Add(path);
    using var process = Process.Start(start)!;
    var errors = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception("Synthetic FLAC creation failed: " + await errors);
    return path;
}

async Task<double> Peak(string path, bool enabled)
{
    using var engine = PlaybackEngine.Create(100, headless: true, replayGain: enabled);
    using var player = new MediaPlayer(engine);
    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var peak = 0d;
    long sampleCount = 0;
    player.SetAudioCallbacks((_, samples, count, _) =>
    {
        var data = new short[count];
        Marshal.Copy(samples, data, 0, (int)count);
        foreach (var sample in data) peak = Math.Max(peak, Math.Abs((double)sample) / 32768);
        sampleCount += count;
    }, null, null, null, null);
    player.SetAudioFormat("S16N", 22050, 1);
    player.EndReached += (_, _) => completion.TrySetResult(true);
    player.EncounteredError += (_, _) => completion.TrySetResult(false);
    using var media = new Media(engine, new Uri(path));
    await media.Parse(MediaParseOptions.ParseLocal);
    if (!player.Play(media)) throw new Exception("Synthetic playback could not start.");
    try { if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new Exception("Synthetic playback failed."); }
    finally { player.Stop(); }
    if (sampleCount < 22050 || peak <= 0 || !double.IsFinite(peak)) throw new Exception($"Insufficient decoded samples: count={sampleCount}, peak={peak}, file={Path.GetFileName(path)}, enabled={enabled}.");
    return peak;
}

void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
