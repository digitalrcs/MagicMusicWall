using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed partial class MusicLookupService
{
    /// <summary>Lookup only the chosen recording and, when supplied, its verified release context.</summary>
    public async Task<MusicBrainzInfoResult> GetMusicBrainzInfoAsync(RecordingMatch match, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!ValidMbid(match.Id, out var recordingId))
                throw new LookupFailure("Choose a recording with a valid MusicBrainz identifier.");
            Guid releaseId = default;
            if (!string.IsNullOrWhiteSpace(match.ReleaseId) && !ValidMbid(match.ReleaseId, out releaseId))
                throw new LookupFailure("Choose an album with a valid MusicBrainz release identifier.");

            // Fetch identity/credits separately from classification. The provider can
            // return HTTP 503 when these valid include groups are combined.
            using var recordingDocument = await Request($"https://musicbrainz.org/ws/2/recording/{recordingId:D}?fmt=json&inc=artist-credits+artist-rels",
                "MusicBrainz", MusicBrainzGate, cancellationToken).ConfigureAwait(false);
            var recording = recordingDocument.RootElement;
            RequireEntity(recording, recordingId, "recording");
            var title = EvidenceText(Text(recording, "title"));
            var artist = EvidenceText(ArtistCredit(recording));
            if (title.Length == 0 || artist.Length == 0)
                throw new LookupFailure("MusicBrainz returned a recording without a usable title or artist.");

            var (instruments, credits) = RecordingCredits(recording);
            using var classificationDocument = await Request($"https://musicbrainz.org/ws/2/recording/{recordingId:D}?fmt=json&inc=genres+tags",
                "MusicBrainz", MusicBrainzGate, cancellationToken).ConfigureAwait(false);
            var classification = classificationDocument.RootElement;
            RequireEntity(classification, recordingId, "recording");
            var genres = VotedNames(classification, "genres").ToList();
            var tags = VotedNames(classification, "tags").ToList();
            var sources = new List<string> { $"https://musicbrainz.org/recording/{recordingId:D}" };
            var album = "";
            var releaseContext = false;
            if (releaseId != Guid.Empty)
            {
                using var releaseDocument = await Request($"https://musicbrainz.org/ws/2/release/{releaseId:D}?fmt=json&inc=recordings+release-groups+genres+tags",
                    "MusicBrainz", MusicBrainzGate, cancellationToken).ConfigureAwait(false);
                var release = releaseDocument.RootElement;
                RequireEntity(release, releaseId, "release");
                if (!ReleaseContainsRecording(release, recordingId))
                    throw new LookupFailure("The selected release could not be verified as containing this recording. Choose another album or a recording-only match.");
                album = EvidenceText(Text(release, "title"));
                if (album.Length == 0) throw new LookupFailure("MusicBrainz returned a release without a usable title.");
                sources.Add($"https://musicbrainz.org/release/{releaseId:D}");
                // Recording genres are the strongest evidence. Album genres fill an absence,
                // never artist-level instrumentation or a guessed classification for this file.
                if (genres.Count == 0)
                {
                    genres.AddRange(VotedNames(release, "genres"));
                    tags.AddRange(VotedNames(release, "tags"));
                    releaseContext = genres.Count > 0 || tags.Count > VotedNames(classification, "tags").Count;
                    if (genres.Count == 0 && release.TryGetProperty("release-group", out var group)
                        && ValidMbid(Text(group, "id"), out var groupId))
                    {
                        using var groupDocument = await Request($"https://musicbrainz.org/ws/2/release-group/{groupId:D}?fmt=json&inc=genres+tags",
                            "MusicBrainz", MusicBrainzGate, cancellationToken).ConfigureAwait(false);
                        var groupRoot = groupDocument.RootElement;
                        RequireEntity(groupRoot, groupId, "release group");
                        sources.Add($"https://musicbrainz.org/release-group/{groupId:D}");
                        var groupGenres = VotedNames(groupRoot, "genres"); var groupTags = VotedNames(groupRoot, "tags");
                        genres.AddRange(groupGenres); tags.AddRange(groupTags);
                        releaseContext |= groupGenres.Count > 0 || groupTags.Count > 0;
                    }
                }
            }
            var info = new MusicBrainzInfo
            {
                RecordingId = recordingId.ToString("D"), Title = title, Artist = artist, Album = album,
                ReleaseId = releaseId == Guid.Empty ? "" : releaseId.ToString("D"),
                FirstReleaseDate = EvidenceDate(Text(recording, "first-release-date")),
                Genres = EvidenceNames(genres), Tags = EvidenceNames(tags), Instruments = instruments, Credits = credits,
                Sources = sources.ToArray(), RetrievedUtc = DateTimeOffset.UtcNow
            };
            return new() { Info = info, Message = releaseContext
                ? "MusicBrainz information includes recording credits and album-level genre/tag context. Instrument labels come only from this recording's performance credits."
                : "MusicBrainz information retrieved. Missing genres or instrument credits mean no data was supplied, not that an instrument is absent." };
        }
        catch (Exception ex) when (FailureMessage(ex, "MusicBrainz", cancellationToken) is not null)
        {
            return new() { Message = FailureMessage(ex, "MusicBrainz", cancellationToken) };
        }
    }

    /// <summary>Conservative automatic selection; provider search scores are never treated as confidence.</summary>
    public static RecordingMatch? ChooseConfidentMatch(MediaItem item, IEnumerable<RecordingMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(item); ArgumentNullException.ThrowIfNull(matches);
        var artist = Normalize(item.Artist ?? ""); var title = Normalize(item.Title ?? "");
        var album = Normalize(item.Album ?? "");
        if (item.IsVideo || artist.Length == 0 || title.Length == 0 || !double.IsFinite(item.DurationSeconds) || item.DurationSeconds <= 0) return null;
        var supplied = matches.Take(101).ToArray();
        if (supplied.Length > 100) return null; // Never claim uniqueness after truncating a candidate set.
        var candidates = supplied.Where(m => m is not null && ValidMbid(m.Id, out _)
            && (string.IsNullOrWhiteSpace(m.ReleaseId) || ValidMbid(m.ReleaseId, out _))
            && Normalize(m.Artist ?? "") == artist && Normalize(m.Title ?? "") == title
            && (album.Length == 0 || (Normalize(m.Album ?? "") == album && ValidMbid(m.ReleaseId, out _)))
            && double.IsFinite(m.DurationSeconds) && m.DurationSeconds > 0 && Math.Abs(m.DurationSeconds - item.DurationSeconds) <= 3
            && CompatibleVersion(item.Title + " " + item.Album, m.Title + " " + m.Album + " " + m.Disambiguation))
            .GroupBy(m => Guid.Parse(m.Id)).Take(2).ToArray();
        if (candidates.Length != 1) return null;
        // Without an album clue, do not attach an arbitrary reissue or compilation.
        var chosen = candidates[0].OrderBy(m => Math.Abs(m.DurationSeconds - item.DurationSeconds))
            .ThenBy(m => m.ReleaseId, StringComparer.Ordinal).First();
        return album.Length == 0 ? chosen with { Album = "", ReleaseId = "", ReleaseDate = "" } : chosen;
    }

    private static bool CompatibleVersion(string local, string candidate)
    {
        // A version cue in either direction needs the same cue in the other. Matching
        // artist/title/duration alone cannot tell a live mix from a studio recording.
        string[] patterns = [@"\blive\b", @"\bremix(?:ed)?\b", @"\bremaster(?:ed)?\b", @"\bdemo\b", @"\bacoustic\b", @"\binstrumental\b", @"\bmono\b", @"\bstereo\b", @"\bradio\s+edit\b"];
        return patterns.All(pattern => Regex.IsMatch(local, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            == Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static bool ValidMbid(string? value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;

    private static void RequireEntity(JsonElement entity, Guid expectedId, string name)
    {
        if (entity.ValueKind != JsonValueKind.Object || !ValidMbid(Text(entity, "id"), out var actual) || actual != expectedId)
            throw new LookupFailure($"MusicBrainz returned an unexpected {name}. No information was saved.");
    }

    private static bool ReleaseContainsRecording(JsonElement release, Guid recordingId)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array) return false;
        foreach (var medium in media.EnumerateArray().Take(100))
        {
            if (medium.ValueKind != JsonValueKind.Object || !medium.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array) continue;
            foreach (var track in tracks.EnumerateArray().Take(1000))
                if (track.ValueKind == JsonValueKind.Object && track.TryGetProperty("recording", out var recording)
                    && ValidMbid(Text(recording, "id"), out var id) && id == recordingId) return true;
        }
        return false;
    }

    private static IReadOnlyList<string> VotedNames(JsonElement entity, string property)
    {
        if (!entity.TryGetProperty(property, out var names) || names.ValueKind != JsonValueKind.Array) return [];
        return EvidenceNames(names.EnumerateArray().Take(500).Where(n => Number(n, "count") > 0).Select(n => Text(n, "name")));
    }

    private static (string[] Instruments, string[] Credits) RecordingCredits(JsonElement recording)
    {
        var instruments = new List<string>(); var credits = new List<string>();
        if (!recording.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array) return ([], []);
        var modifiers = new HashSet<string>(["additional", "guest", "solo", "original", "minor", "uncredited"], StringComparer.OrdinalIgnoreCase);
        foreach (var relation in relations.EnumerateArray().Take(500))
        {
            if (relation.ValueKind != JsonValueKind.Object || Text(relation, "target-type") != "artist"
                || !relation.TryGetProperty("artist", out var artist)) continue;
            var name = EvidenceText(Text(artist, "name")); var type = EvidenceText(Text(relation, "type"), 100);
            if (name.Length == 0 || type.Length == 0) continue;
            var attributes = relation.TryGetProperty("attributes", out var array) && array.ValueKind == JsonValueKind.Array
                ? EvidenceNames(array.EnumerateArray().Take(100).Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString() ?? "")) : [];
            credits.Add(name + " — " + type + (attributes.Length == 0 ? "" : " (" + string.Join(", ", attributes) + ")"));
            if (type.Equals("instrument", StringComparison.OrdinalIgnoreCase))
                instruments.AddRange(attributes.Where(a => !modifiers.Contains(a)));
        }
        return (EvidenceNames(instruments), EvidenceNames(credits, maximumLength: 512));
    }

    private static string EvidenceText(string? value, int limit = 256)
    {
        var clean = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        clean = new string(clean.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length <= limit ? clean : clean[..limit];
    }

    private static string[] EvidenceNames(IEnumerable<string> values, int maximumLength = 128) => values
        .Select(value => EvidenceText(value, maximumLength)).Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();

    private static string EvidenceDate(string value)
    {
        if (value.Length == 4 && int.TryParse(value, out var year) && year is >= 1000 and <= 9999) return value;
        return DateTime.TryParseExact(value, ["yyyy-MM", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value : "";
    }
}
