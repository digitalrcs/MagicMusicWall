using System.Text.Json;
using MagicMusicWall.App;
using MagicMusicWall.Core;

if (args.Length != 1) throw new ArgumentException("Pass the bundled libvlc/win-x64 directory.");
LibVLCSharp.Shared.Core.Initialize(Path.GetFullPath(args[0]));
var directory = Path.Combine(Path.GetTempPath(), "MagicMusicWall-MapChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "pulse.wav");
    const int rate = 22050, samples = rate * 5;
    using (var stream = new BinaryWriter(File.Create(path)))
    {
        stream.Write("RIFF"u8); stream.Write(36 + samples * 2); stream.Write("WAVEfmt "u8);
        stream.Write(16); stream.Write((short)1); stream.Write((short)1); stream.Write(rate);
        stream.Write(rate * 2); stream.Write((short)2); stream.Write((short)16);
        stream.Write("data"u8); stream.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / rate;
            stream.Write((short)(10000 * Math.Sin(2 * Math.PI * 440 * t) * (.2 + .8 * Math.Exp(-15 * (t * 2 % 1)))));
        }
    }
    var tracks = new[] { new MediaItem { Id = "test-pulse", Title = "Synthetic pulse", Path = path } };
    var progress = new InlineProgress(_ => { });
    void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
    var first = await MusicMapAudioScanner.ScanAsync(directory, tracks, progress, CancellationToken.None);
    Check(first is { Analyzed: 1, Cached: 0, Failed: 0, Cancelled: false }, "first scan analyzes the generated WAV");
    Check(MusicMapAudioScanner.LoadFeatures(directory, tracks).Count == 1, "saved feature cache reloads");
    var second = await MusicMapAudioScanner.ScanAsync(directory, tracks, progress, CancellationToken.None);
    Check(second is { Analyzed: 0, Cached: 1, Failed: 0 }, "unchanged file skips decoding");
    File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(-5));
    Check(MusicMapAudioScanner.LoadFeatures(directory, tracks).Count == 0, "changed file invalidates the previous fingerprint");
    var third = await MusicMapAudioScanner.ScanAsync(directory, tracks, progress, CancellationToken.None);
    Check(third is { Analyzed: 1, Cached: 0, Failed: 0 }, "changed file is analyzed again");
    using var cancellation = new CancellationTokenSource();
    var secondTrack = new MediaItem { Id = "second", Path = path, Title = "Second synthetic track" };
    var cancelled = await MusicMapAudioScanner.ScanAsync(directory, [tracks[0], secondTrack],
        new InlineProgress(p => { if (p.Completed == 1) cancellation.Cancel(); }), cancellation.Token);
    Check(cancelled.Cancelled && MusicMapAudioScanner.LoadFeatures(directory, tracks).Count == 1, "cancellation retains completed cached work");
    var missing = new MediaItem { Id = "missing", Title = "Disconnected file", Path = Path.Combine(directory, "missing.wav") };
    var failure = await MusicMapAudioScanner.ScanAsync(directory, [tracks[0], missing], progress, CancellationToken.None);
    Check(failure is { Cached: 1, Failed: 1 }, "unavailable file is reported without losing successful cache entries");
    File.WriteAllText(Path.Combine(directory, "music-audio-features.json"), "{bad json");
    Check(MusicMapAudioScanner.LoadFeatures(directory, tracks).Count == 0, "corrupt cache safely falls back to metadata");
}
finally
{
    // A unique test-owned directory; never receives any user library paths.
    Directory.Delete(directory, recursive: true);
}

sealed class InlineProgress(Action<MusicMapScanProgress> report) : IProgress<MusicMapScanProgress>
{
    public void Report(MusicMapScanProgress value) => report(value);
}
