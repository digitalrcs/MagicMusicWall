using System.Security.Cryptography;
using System.Text;

namespace MagicMusicWall.Core;

public enum MusicMapKind { Artist, Album, Song }

public sealed record MusicMapEntity(string Id, MusicMapKind Kind, string Name, string Artist,
    string Album, IReadOnlyList<string> TrackIds, IReadOnlyList<string> Genres, IReadOnlyList<int> Decades);
public sealed record MusicMapMatch(MusicMapEntity Entity, int Score, string Reason);

/// <summary>A local library snapshot with metadata relationships and optional measured audio descriptors.</summary>
public sealed class MusicMapIndex
{
    private readonly Dictionary<string, MusicMapEntity> _byId;
    private readonly Dictionary<string, Identity> _identities;
    private readonly Dictionary<string, AnalyzedEntity> _audio;
    public IReadOnlyList<MusicMapEntity> Entities { get; }
    public IReadOnlyList<string> Genres { get; }
    public int TrackCount { get; }
    /// <summary>Number of unique music tracks with at least one usable genre tag.</summary>
    public int TaggedTrackCount { get; }
    public int AnalyzedTrackCount { get; }

    private MusicMapIndex(List<MusicMapEntity> entities, Dictionary<string, Identity> identities, Track[] tracks,
        IReadOnlyDictionary<string, AudioFeatures>? audio)
    {
        Entities = Array.AsReadOnly(entities.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray());
        _byId = Entities.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _identities = identities;
        Genres = Labels(tracks.SelectMany(x => x.Genres));
        TrackCount = tracks.Length;
        TaggedTrackCount = tracks.Count(x => x.Genres.Count > 0);
        var measured = new Dictionary<string, AudioFeatures>(StringComparer.Ordinal);
        if (audio is not null)
            foreach (var track in tracks)
                if (audio.TryGetValue(track.Id, out var feature) && ValidAudio(feature))
                    measured.Add(track.Id, feature with { Mfcc = (double[])feature.Mfcc.Clone(), Chroma = (double[])feature.Chroma.Clone() });
        AnalyzedTrackCount = measured.Count;
        _audio = BuildAudio(measured);
    }

    public static MusicMapIndex Build(IEnumerable<MediaItem> items, IReadOnlyDictionary<string, AudioFeatures>? audio = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        // Copy every value before grouping. Later catalog edits cannot change this map.
        var tracks = items.Where(x => !x.IsVideo).Select(Snapshot)
            .OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Title, StringComparer.Ordinal)
            .DistinctBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var entities = new List<MusicMapEntity>(tracks.Length * 2);
        var identities = new Dictionary<string, Identity>(StringComparer.Ordinal);

        void Add(string id, MusicMapKind kind, IEnumerable<Track> members)
        {
            var group = members.OrderBy(x => x.Album, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Disc).ThenBy(x => x.Number).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            var first = group[0];
            var artist = Display(group.Select(x => x.Artist));
            var album = kind == MusicMapKind.Artist ? "" : Display(group.Select(x => x.Album));
            var name = kind switch { MusicMapKind.Artist => artist, MusicMapKind.Album => album, _ => first.Title };
            entities.Add(new(id, kind, name, artist, album,
                Array.AsReadOnly(group.Select(x => x.Id).ToArray()), Labels(group.SelectMany(x => x.Genres)),
                Array.AsReadOnly(group.Where(x => x.Decade.HasValue).Select(x => x.Decade!.Value).Distinct().Order().ToArray())));
            identities.Add(id, new(first.KnownArtist ? first.ArtistKey : null,
                kind != MusicMapKind.Artist && first.KnownAlbum ? first.AlbumKey : null));
        }

        foreach (var group in tracks.GroupBy(x => x.ArtistKey, StringComparer.Ordinal))
            Add(StableId("artist", group.Key), MusicMapKind.Artist, group);
        foreach (var group in tracks.GroupBy(x => x.AlbumKey, StringComparer.Ordinal))
            Add(StableId("album", group.Key), MusicMapKind.Album, group);
        foreach (var track in tracks) Add("song:" + track.Id, MusicMapKind.Song, [track]);
        return new(entities, identities, tracks, audio);
    }

    public AudioFeatureSummary? AudioSummary(string entityId) => entityId is not null && _audio.TryGetValue(entityId, out var entity) ? entity.Summary : null;

    public IReadOnlyList<MusicMapMatch> Related(string entityId, int limit = 12, MusicMapSimilarity mode = MusicMapSimilarity.Metadata)
    {
        if (limit <= 0 || entityId is null || !_byId.TryGetValue(entityId, out var source)) return [];
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var sourceIdentity = _identities[source.Id];
        _audio.TryGetValue(source.Id, out var sourceAudio);
        if (mode == MusicMapSimilarity.Sound && sourceAudio is null) return [];
        var matches = new List<(MusicMapMatch Match, double Rank)>();
        foreach (var candidate in Entities)
        {
            if (candidate.Kind != source.Kind || candidate.Id == source.Id) continue;
            var score = 0;
            var reasons = new List<string>();
            var commonGenres = source.Genres.Intersect(candidate.Genres, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
            if (commonGenres.Length > 0)
            {
                score += 100 + 10 * (commonGenres.Length - 1);
                reasons.Add("Shared genre: " + string.Join(", ", commonGenres));
            }
            var candidateIdentity = _identities[candidate.Id];
            if (source.Kind != MusicMapKind.Artist && sourceIdentity.ArtistKey is not null && sourceIdentity.ArtistKey == candidateIdentity.ArtistKey)
            { score += 35; reasons.Add("Same artist"); }
            if (source.Kind == MusicMapKind.Song && sourceIdentity.AlbumKey is not null && sourceIdentity.AlbumKey == candidateIdentity.AlbumKey)
            { score += 45; reasons.Add("Same album"); }
            var commonDecades = source.Decades.Intersect(candidate.Decades).Take(2).ToArray();
            if (commonDecades.Length > 0)
            {
                score += 10;
                reasons.Add("Shared era: " + string.Join(", ", commonDecades.Select(x => x + "s")));
            }
            double rank = score;
            if (mode != MusicMapSimilarity.Metadata)
            {
                _audio.TryGetValue(candidate.Id, out var candidateAudio);
                if (sourceAudio is not null && candidateAudio is not null)
                {
                    var resemblance = 1 / (1 + AudioDistance(sourceAudio, candidateAudio));
                    rank = mode == MusicMapSimilarity.Sound ? 1000 * resemblance : 1000 * (.7 * resemblance + .3 * Math.Min(1, score / 210.0));
                    if (mode == MusicMapSimilarity.Sound) reasons.Clear();
                    reasons.Insert(0, $"Estimated sound resemblance (analyzed {sourceAudio.Summary.AnalyzedTracks}/{sourceAudio.Summary.TotalTracks} and {candidateAudio.Summary.AnalyzedTracks}/{candidateAudio.Summary.TotalTracks} tracks)");
                }
                else if (mode == MusicMapSimilarity.Sound) continue;
                else
                {
                    rank = 1000 * .3 * Math.Min(1, score / 210.0);
                    reasons.Insert(0, "Metadata only: sound analysis unavailable for one or both selections");
                }
            }
            if (rank > 0) matches.Add((new(candidate, Math.Max(1, (int)Math.Round(rank)), string.Join(" · ", reasons)), rank));
        }
        return matches.OrderByDescending(x => x.Rank).ThenBy(x => x.Match.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Match.Entity.Id, StringComparer.Ordinal).Take(Math.Min(limit, 100)).Select(x => x.Match).ToArray();
    }

    private Dictionary<string, AnalyzedEntity> BuildAudio(Dictionary<string, AudioFeatures> measured)
    {
        var result = new Dictionary<string, AnalyzedEntity>(StringComparer.Ordinal);
        if (measured.Count == 0) return result;
        var vectors = measured.ToDictionary(x => x.Key, x => AudioVector(x.Value), StringComparer.Ordinal);
        const int dimensions = 42;
        var means = new double[dimensions]; var moments = new double[dimensions]; var counts = new int[dimensions];
        foreach (var pair in vectors)
            for (var i = 0; i < dimensions; i++)
            {
                if (i == 38 && measured[pair.Key].Tempo == 0) continue;
                var delta = pair.Value[i] - means[i];
                means[i] += delta / ++counts[i]; moments[i] += delta * (pair.Value[i] - means[i]);
            }
        var deviations = new double[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            var floor = i < 26 ? .05 : i < 38 ? .01 : i == 39 ? .03 : i == 40 ? .1 : .05;
            deviations[i] = Math.Max(floor, Math.Sqrt(Math.Max(0, moments[i] / Math.Max(1, counts[i]))));
        }
        foreach (var pair in vectors)
            for (var i = 0; i < dimensions; i++)
                pair.Value[i] = i == 38 && measured[pair.Key].Tempo == 0 ? 0 : Math.Clamp((pair.Value[i] - means[i]) / deviations[i], -4, 4);
        foreach (var entity in Entities)
        {
            var ids = entity.TrackIds.Where(measured.ContainsKey).ToArray();
            if (ids.Length == 0) continue;
            var average = new double[dimensions];
            foreach (var id in ids)
                for (var i = 0; i < dimensions; i++) average[i] += vectors[id][i] / ids.Length;
            var rhythmic = ids.Where(id => measured[id].Tempo > 0).ToArray();
            // Missing estimates do not pull an artist's tempo toward zero.
            average[38] = rhythmic.Length > 0 ? rhythmic.Average(id => vectors[id][38]) : 0;
            var summary = new AudioFeatureSummary(ids.Length, entity.TrackIds.Count,
                rhythmic.Length > 0 ? rhythmic.Average(id => measured[id].Tempo) : 0, ids.Average(id => measured[id].Rms));
            result.Add(entity.Id, new(average, summary));
        }
        return result;
    }

    private static double[] AudioVector(AudioFeatures features) => [.. features.Mfcc, .. features.Chroma,
        features.Tempo > 0 ? Math.Log2(features.Tempo) : 0, features.BeatStrength,
        Math.Log(Math.Max(1e-9, features.Rms)), Math.Log(1 + features.Centroid)];

    private static double AudioDistance(AnalyzedEntity a, AnalyzedEntity b)
    {
        double Group(int start, int length)
        {
            double sum = 0;
            for (var i = start; i < start + length; i++) { var delta = a.Vector[i] - b.Vector[i]; sum += delta * delta; }
            return sum / length;
        }
        var rhythm = a.Summary.Tempo > 0 && b.Summary.Tempo > 0 ? Group(38, 2) : Group(39, 1);
        // Each descriptor family contributes a bounded share, independent of its units
        // and dimensionality. Scores rank this library; they are not probability values.
        return Math.Sqrt(.5 * Group(0, 26) + .25 * Group(26, 12) + .15 * rhythm + .1 * Group(40, 2));
    }

    private static bool ValidAudio(AudioFeatures? features) => features is { Mfcc.Length: 26, Chroma.Length: 12 }
        && features.Mfcc.All(x => double.IsFinite(x) && Math.Abs(x) < 1_000_000)
        && features.Chroma.All(x => double.IsFinite(x) && x is >= 0 and <= 1)
        && double.IsFinite(features.Tempo) && (features.Tempo == 0 || features.Tempo is >= 20 and <= 400)
        && double.IsFinite(features.BeatStrength) && features.BeatStrength is >= 0 and <= 1
        && double.IsFinite(features.Rms) && features.Rms is > 0 and <= 4
        && double.IsFinite(features.Centroid) && features.Centroid is >= 0 and <= AudioFeatureExtractor.SampleRate / 2.0;

    private sealed record AnalyzedEntity(double[] Vector, AudioFeatureSummary Summary);

    private static Track Snapshot(MediaItem item)
    {
        var path = Clean(item.Path).Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var folder = slash >= 0 ? path[..slash] : "";
        var folderName = folder[(folder.LastIndexOf('/') + 1)..];
        var folderKey = folder.Length > 0 ? Key(folder) : "track:" + item.Id;
        var artist = Clean(item.Artist);
        var album = Clean(item.Album);
        var knownArtist = !Missing(artist, "unknown artist");
        var knownAlbum = !Missing(album, "unknown album");
        var artistKey = knownArtist ? "tag:" + Key(artist) : "folder:" + folderKey;
        var albumKey = StableId("release", artistKey, knownAlbum ? "tag:" + Key(album) : "folder:" + folderKey);
        if (!knownArtist) artist = folderName.Length > 0 ? "Unknown artist · " + folderName : "Unknown artist";
        if (!knownAlbum) album = folderName.Length > 0 ? "Unlabeled album · " + folderName : "Unlabeled album";
        var title = Clean(item.Title);
        if (title.Length == 0) title = path[(slash + 1)..];
        if (title.Length == 0) title = "Untitled song";
        var genres = Labels(Clean(item.Genre).Split([';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Clean).Where(x => !Missing(x, "unknown genre") && !x.Equals("unclassified", StringComparison.OrdinalIgnoreCase)));
        int? decade = item.Year is > 0 and <= 9999 ? (int)(item.Year / 10 * 10) : null;
        return new(item.Id, path, title, artist, album, artistKey, albumKey, knownArtist, knownAlbum,
            genres, decade, item.DiscNumber, item.TrackNumber);
    }

    private static bool Missing(string value, string label) => value.Length == 0 || value.Equals(label, StringComparison.OrdinalIgnoreCase)
        || value.Equals("unknown", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase)
        || value.Equals("(none)", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string? value) => value?.Trim() ?? "";
    private static string Key(string value) => value.Trim().ToUpperInvariant();
    private static string Display(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).First();
    private static IReadOnlyList<string> Labels(IEnumerable<string> values) => Array.AsReadOnly(values
        .Order(StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    private static string StableId(string prefix, params string[] parts) => prefix + ":" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Concat(parts.Select(x => x.Length + ":" + x)))));
    private sealed record Identity(string? ArtistKey, string? AlbumKey);
    private sealed record Track(string Id, string Path, string Title, string Artist, string Album, string ArtistKey, string AlbumKey,
        bool KnownArtist, bool KnownAlbum, IReadOnlyList<string> Genres, int? Decade, uint Disc, uint Number);
}
