using System.IO;
using System.Text.Json;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal sealed record MusicMapScanProgress(int Completed, int Total, int Analyzed, int Cached, int Failed, string CurrentTitle);
internal sealed record MusicMapScanResult(int Analyzed, int Cached, int Failed, bool Cancelled);

/// <summary>Incremental local analysis. Original media is opened only by the decoder, for reading.</summary>
internal static class MusicMapAudioScanner
{
    private const string CacheName = "music-audio-features.json";
    private sealed record Entry(string Path, long Length, long ModifiedUtcTicks, AudioFeatures Features);
    private sealed record Cache(int Version, Dictionary<string, Entry> Tracks);

    public static IReadOnlyDictionary<string, AudioFeatures> LoadFeatures(string directory, IEnumerable<MediaItem> items)
    {
        var cache = Load(directory);
        var result = new Dictionary<string, AudioFeatures>(StringComparer.Ordinal);
        foreach (var item in items.Where(x => !x.IsVideo))
            if (cache.TryGetValue(item.Id, out var entry) && Matches(entry, item.Path)) result[item.Id] = entry.Features;
        return result;
    }

    public static async Task<MusicMapScanResult> ScanAsync(string directory, MediaItem[] tracks,
        IProgress<MusicMapScanProgress> progress, CancellationToken cancellationToken)
    {
        var entries = Load(directory);
        var analyzed = 0; var cached = 0; var failed = 0; var completed = 0; var cancelled = false;
        var errors = new List<string>();
        var pendingResults = 0;
        // Removing library entries must also remove their cached private paths on the next scan.
        var ids = tracks.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in entries.Keys.Where(id => !ids.Contains(id)).ToArray()) entries.Remove(id);
        using var decoder = new AudioSampleDecoder();
        try
        {
            foreach (var track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new(completed, tracks.Length, analyzed, cached, failed, track.Title));
                try
                {
                    if (entries.TryGetValue(track.Id, out var existing) && Matches(existing, track.Path)) cached++;
                    else
                    {
                        entries.Remove(track.Id);
                        var before = Stamp(track.Path);
                        var samples = await decoder.DecodeAsync(track.Path, cancellationToken).ConfigureAwait(false);
                        var features = AudioFeatureExtractor.Extract(samples, cancellationToken);
                        if (before != Stamp(track.Path)) throw new IOException("The file changed during analysis; retry after editing finishes.");
                        entries[track.Id] = new(track.Path, before.Length, before.Ticks, features);
                        analyzed++;
                        pendingResults++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed++;
                    if (errors.Count < 100) errors.Add($"{track.Title}: {ex.Message}");
                }
                completed++;
                if (pendingResults >= 50) { Save(directory, entries); pendingResults = 0; }
                progress.Report(new(completed, tracks.Length, analyzed, cached, failed, track.Title));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; }
        finally
        {
            // Keep completed work even when cancelled. Only a complete JSON file replaces the cache.
            Save(directory, entries);
            File.WriteAllText(Path.Combine(directory, "music-audio-scan.txt"),
                $"{DateTimeOffset.Now:u}\nAnalyzed: {analyzed}; unchanged: {cached}; failed: {failed}; cancelled: {cancelled}.\n"
                + string.Join(Environment.NewLine, errors));
        }
        return new(analyzed, cached, failed, cancelled);
    }

    private static Dictionary<string, Entry> Load(string directory)
    {
        try
        {
            var file = Path.Combine(directory, CacheName);
            if (!File.Exists(file)) return new(StringComparer.Ordinal);
            using var stream = File.OpenRead(file);
            var data = JsonSerializer.Deserialize<Cache>(stream);
            if (data?.Version != AudioFeatureExtractor.AlgorithmVersion || data.Tracks is null) return new(StringComparer.Ordinal);
            return data.Tracks.Where(x => x.Value is not null && Valid(x.Value.Features))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new(StringComparer.Ordinal); }
    }

    private static bool Valid(AudioFeatures? features) => features is not null && features.Mfcc is { Length: 26 }
        && features.Chroma is { Length: 12 } && features.Mfcc.All(x => double.IsFinite(x) && Math.Abs(x) < 1_000_000)
        && features.Chroma.All(x => double.IsFinite(x) && x is >= 0 and <= 1)
        && double.IsFinite(features.Tempo) && (features.Tempo == 0 || features.Tempo is >= 20 and <= 400)
        && double.IsFinite(features.BeatStrength) && features.BeatStrength is >= 0 and <= 1
        && double.IsFinite(features.Rms) && features.Rms is > 0 and <= 4
        && double.IsFinite(features.Centroid) && features.Centroid is >= 0 and <= AudioFeatureExtractor.SampleRate / 2.0;

    private static bool Matches(Entry entry, string path)
    {
        if (!string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)) return false;
        try { var stamp = Stamp(path); return stamp.Length == entry.Length && stamp.Ticks == entry.ModifiedUtcTicks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private static (long Length, long Ticks) Stamp(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File is unavailable. Reconnect its drive or share.");
        return (info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static void Save(string directory, Dictionary<string, Entry> entries)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, CacheName);
        var temporary = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Cache(AudioFeatureExtractor.AlgorithmVersion, entries));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
