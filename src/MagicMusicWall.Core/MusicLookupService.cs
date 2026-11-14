using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed record RecordingMatch
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string FirstReleaseDate { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string ReleaseId { get; init; } = "";
    public double DurationSeconds { get; init; }
    public string Disambiguation { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public int Score { get; init; }
}

public sealed record MusicSearchResult
{
    public IReadOnlyList<RecordingMatch> Items { get; init; } = [];
    public string SourceUrl { get; init; } = "https://musicbrainz.org/";
    public string? Message { get; init; }
}

public sealed record LyricsResult
{
    public bool Found { get; init; }
    public string PlainText { get; init; } = "";
    public string SyncedText { get; init; } = "";
    public bool Instrumental { get; init; }
    public string SourceName { get; init; } = "LRCLIB";
    public string SourceUrl { get; init; } = "https://lrclib.net/";
    public string MatchedTitle { get; init; } = "";
    public string MatchedArtist { get; init; } = "";
    public string MatchedAlbum { get; init; } = "";
    public string? Message { get; init; }
    public IReadOnlyList<LyricsChoice> Choices { get; init; } = [];
}

public sealed record LyricsChoice(LyricsResult Lyrics, double DurationSeconds, string Id);

/// <summary>
/// Explicit, user-initiated metadata/lyrics requests. No media paths, file writes,
/// background scans, automatic catalog edits, or generated song facts.
/// </summary>
public sealed class MusicLookupService : IDisposable
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const string UserAgent = "MagicMusicWall/0.3 (https://digitalrcs.com)";
    private static readonly ProviderGate MusicBrainzGate = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    private static readonly ProviderGate LyricsGate = new(TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsClient;

    public MusicLookupService() : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }) => _ownsClient = true;

    /// <summary>The caller retains ownership of an injected HttpClient.</summary>
    public MusicLookupService(HttpClient client, TimeSpan? requestTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public async Task<MusicSearchResult> SearchRecordingsAsync(string artist, string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = "https://musicbrainz.org/";
        try
        {
            artist = Input(artist, "artist", required: false);
            title = Input(title, "title", required: true);
            var query = $"recording:\"{EscapeLucene(title)}\"";
            if (artist.Length > 0) query += $" AND artist:\"{EscapeLucene(artist)}\"";
            source = "https://musicbrainz.org/search?type=recording&method=advanced&query=" + Uri.EscapeDataString(query);
            var uri = "https://musicbrainz.org/ws/2/recording?fmt=json&limit=12&query=" + Uri.EscapeDataString(query);
            using var document = await Request(uri, "MusicBrainz", MusicBrainzGate, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("recordings", out var recordings) || recordings.ValueKind != JsonValueKind.Array)
                throw new LookupFailure("MusicBrainz returned an unexpected response. Please try again later.");
            var groups = new List<List<RecordingMatch>>();
            foreach (var recording in recordings.EnumerateArray().Take(100))
            {
                var id = Text(recording, "id");
                var name = Text(recording, "title");
                var credit = ArtistCredit(recording);
                if (!Guid.TryParse(id, out var recordingId) || name.Length == 0 || credit.Length == 0) continue;
                var baseMatch = new RecordingMatch
                {
                    Id = recordingId.ToString(), Title = name, Artist = credit,
                    FirstReleaseDate = Text(recording, "first-release-date"),
                    DurationSeconds = Math.Max(0, Number(recording, "length") / 1000),
                    Disambiguation = Text(recording, "disambiguation"),
                    SourceUrl = "https://musicbrainz.org/recording/" + recordingId,
                    Score = (int)Math.Clamp(Number(recording, "score"), 0, 100)
                };
                var albums = new List<RecordingMatch>();
                if (recording.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
                {
                    foreach (var release in releases.EnumerateArray().Take(200))
                    {
                        var album = Text(release, "title");
                        if (album.Length == 0 || albums.Any(a => a.Album.Equals(album, StringComparison.OrdinalIgnoreCase))) continue;
                        var releaseId = Guid.TryParse(Text(release, "id"), out var parsed) ? parsed.ToString() : "";
                        albums.Add(baseMatch with { Album = album, ReleaseId = releaseId, ReleaseDate = Text(release, "date") });
                    }
                }
                groups.Add(albums.Count == 0 ? [baseMatch] : albums);
            }
            if (recordings.GetArrayLength() > 0 && groups.Count == 0)
                throw new LookupFailure("MusicBrainz returned recordings without usable identifying data. Please try again later.");
            // Include one result per recording before extra albums so prolific reissues
            // cannot displace every alternate recording. Keep distinct album choices.
            var candidates = groups.OrderByDescending(g => g[0].Score).ToList();
            var items = candidates.Select(g => g[0]).Concat(candidates.SelectMany(g => g.Skip(1)))
                .DistinctBy(m => (m.Id, m.Album.ToUpperInvariant())).Take(12).ToList();
            return new() { Items = items, SourceUrl = source, Message = items.Count == 0 ? "No matching recordings found. Try a different artist or title." : "Choose the recording and album that match your file; online matches are suggestions." };
        }
        catch (Exception ex) when (FailureMessage(ex, "MusicBrainz", cancellationToken) is not null)
        {
            return new() { SourceUrl = source, Message = FailureMessage(ex, "MusicBrainz", cancellationToken) };
        }
    }

    public async Task<LyricsResult> GetLyricsAsync(string artist, string title, string album, double duration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            artist = Input(artist, "artist", required: true);
            title = Input(title, "title", required: true);
            album = Input(album, "album", required: false);
            if (!double.IsFinite(duration) || duration < 0) throw new LookupFailure("The song duration must be a finite, nonnegative number.");
            // Stable v1 search lets us assess alternate versions ourselves rather than
            // accepting the provider's single best guess from /api/get.
            var uri = "https://lrclib.net/api/search?artist_name=" + Uri.EscapeDataString(artist) + "&track_name=" + Uri.EscapeDataString(title);
            using var document = await Request(uri, "LRCLIB", LyricsGate, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new LookupFailure("LRCLIB returned an unexpected response. Please try again later.");
            var records = document.RootElement.EnumerateArray().Take(100).Select(ParseLyrics).Where(r => r is not null).Cast<LyricCandidate>().ToList();
            if (document.RootElement.GetArrayLength() > 0 && records.Count == 0)
                throw new LookupFailure("LRCLIB returned records without usable identifying data. Please try again later.");
            var eligible = records.Where(r => Normalize(r.Title) == Normalize(title) && Normalize(r.Artist) == Normalize(artist))
                .Where(r => duration <= 0 || (r.Duration > 0 && Math.Abs(r.Duration - duration) <= 5)).ToList();
            if (eligible.Count == 0) return new() { Message = "No confident lyrics match found for this artist, title and duration. Check the song details and try again." };
            var albumMatches = eligible.Where(r => album.Length > 0 && Normalize(r.Album) == Normalize(album)).ToList();
            if (albumMatches.Count > 0) eligible = albumMatches;
            var withLyrics = eligible.Where(r => r.Instrumental || r.Plain.Length > 0 || r.Synced.Length > 0).ToList();
            if (withLyrics.Count == 0) return new() { Message = "LRCLIB has matching track information but no lyrics for it yet." };
            // Expose alternate timing/text versions for an explicit choice, never an
            // arbitrary automatic selection. Duplicate payloads retain the closest ID.
            var variants = withLyrics.OrderBy(r => duration > 0 ? Math.Abs(r.Duration - duration) : 0).ThenBy(r => r.Id)
                .DistinctBy(r => r.Id)
                .DistinctBy(r => (r.Instrumental, r.Instrumental ? "" : r.Plain.Trim(), r.Instrumental ? "" : r.Synced.Trim())).ToList();
            var albumDiffers = album.Length > 0 && albumMatches.Count == 0;
            if (variants.Count > 1) return new()
            {
                Message = "Several versions match. Choose the recording to view its lyrics.",
                Choices = variants.Take(8).Select(match => new LyricsChoice(
                    ToLyricsResult(match, duration, albumDiffers, explicitlySelected: true), match.Duration, match.Id.ToString(CultureInfo.InvariantCulture))).ToList()
            };
            return ToLyricsResult(variants[0], duration, albumDiffers, explicitlySelected: false);
        }
        catch (Exception ex) when (FailureMessage(ex, "LRCLIB", cancellationToken) is not null)
        {
            return new() { Message = FailureMessage(ex, "LRCLIB", cancellationToken) };
        }
    }

    private static LyricsResult ToLyricsResult(LyricCandidate match, double duration, bool albumDiffers, bool explicitlySelected)
    {
        var notes = new List<string>();
        if (explicitlySelected) notes.Add("This lyrics version was explicitly selected by you.");
        if (match.Instrumental) notes.Add("LRCLIB marks this recording as instrumental.");
        if (duration <= 0) notes.Add("Duration was unavailable, so timing could not be verified.");
        if (albumDiffers) notes.Add(duration > 0 ? "Artist, title and duration matched; the album differs. Verify this version." : "Artist and title matched; the album differs. Verify this version.");
        notes.Add("Lyrics are supplied by LRCLIB; verify the selected recording.");
        return new()
        {
            Found = true, PlainText = match.Instrumental ? "" : match.Plain, SyncedText = match.Instrumental ? "" : match.Synced,
            Instrumental = match.Instrumental, SourceUrl = "https://lrclib.net/api/get/" + match.Id.ToString(CultureInfo.InvariantCulture),
            MatchedTitle = match.Title, MatchedArtist = match.Artist, MatchedAlbum = match.Album, Message = string.Join(" ", notes)
        };
    }

    private async Task<JsonDocument> Request(string uri, string provider, ProviderGate gate, CancellationToken cancellationToken)
    {
        await gate.Enter(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(5);
                if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                gate.CoolDown(retry + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 350)));
                throw new LookupFailure($"{provider} is busy or rate limited. Please wait at least {WaitDuration(retry)} before trying again.");
            }
            if (!response.IsSuccessStatusCode) throw new LookupFailure($"{provider} could not complete the lookup (HTTP {(int)response.StatusCode}). Please try again later.");
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && type != "application/json" && !type.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                throw new LookupFailure($"{provider} returned an unexpected response format. Please try again later.");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new LookupFailure($"{provider} returned too much data. Try a more specific search.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (buffer.Length + count > MaximumResponseBytes) throw new LookupFailure($"{provider} returned too much data. Try a more specific search.");
                buffer.Write(chunk, 0, count);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        finally { gate.Exit(); }
    }

    private static string? FailureMessage(Exception exception, string provider, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException => $"{provider} took too long to respond. Please try again.",
        LookupFailure failure => failure.Message,
        JsonException => $"{provider} returned unreadable data. Please try again later.",
        HttpRequestException or IOException => $"Could not reach {provider}. Check your internet connection and try again.",
        _ => null
    };

    private static string WaitDuration(TimeSpan delay)
    {
        var seconds = Math.Max(1, Math.Ceiling(delay.TotalSeconds));
        return seconds.ToString(CultureInfo.InvariantCulture) + (seconds == 1 ? " second" : " seconds");
    }

    private static string Input(string? value, string label, bool required)
    {
        value = value?.Trim() ?? "";
        if (value.Length > 256) throw new LookupFailure($"The {label} must be 256 characters or fewer.");
        if (required && Normalize(value).Length == 0) throw new LookupFailure($"Enter {(label == "artist" ? "an" : "a")} {label} before looking online.");
        if (value.Any(c => char.IsControl(c))) throw new LookupFailure($"The {label} must not contain control characters.");
        return value;
    }

    private static string EscapeLucene(string value)
    {
        var escaped = new StringBuilder();
        foreach (var c in value) { if ("+-&|!(){}[]^\"~*?:\\/".Contains(c)) escaped.Append('\\'); escaped.Append(c); }
        return escaped.ToString();
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).ToArray());

    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static double Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return 0;
        double number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) ? number : 0;
    }

    private static string ArtistCredit(JsonElement recording)
    {
        if (recording.ValueKind != JsonValueKind.Object || !recording.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array) return "";
        var result = new StringBuilder();
        foreach (var credit in credits.EnumerateArray())
        {
            var name = Text(credit, "name");
            if (name.Length == 0 && credit.ValueKind == JsonValueKind.Object && credit.TryGetProperty("artist", out var artist)) name = Text(artist, "name");
            result.Append(name).Append(Text(credit, "joinphrase"));
        }
        return result.ToString().Trim();
    }

    private static LyricCandidate? ParseLyrics(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var parsedId) || parsedId < 0) return null;
        var title = Text(element, "trackName"); var artist = Text(element, "artistName");
        if (title.Length == 0 || artist.Length == 0) return null;
        var instrumental = element.TryGetProperty("instrumental", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new(parsedId, title, artist, Text(element, "albumName"), Math.Max(0, Number(element, "duration")), instrumental,
            Text(element, "plainLyrics"), Text(element, "syncedLyrics"));
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }

    private sealed record LyricCandidate(long Id, string Title, string Artist, string Album, double Duration, bool Instrumental, string Plain, string Synced);
    private sealed class LookupFailure(string message) : Exception(message);

    private sealed class ProviderGate(TimeSpan startSpacing, TimeSpan completionSpacing)
    {
        private readonly SemaphoreSlim _serial = new(1, 1);
        private long _nextStart;
        private DateTimeOffset _cooldown;

        public async Task Enter(CancellationToken cancellationToken)
        {
            await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cooldown > DateTimeOffset.UtcNow)
                    throw new LookupFailure($"The provider asked us to wait. Try again in {WaitDuration(_cooldown - DateTimeOffset.UtcNow)}.");
                while (true)
                {
                    var remaining = _nextStart - Stopwatch.GetTimestamp();
                    if (remaining <= 0) break;
                    await Task.Delay(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency), cancellationToken).ConfigureAwait(false);
                }
                _nextStart = Stopwatch.GetTimestamp() + (long)(startSpacing.TotalSeconds * Stopwatch.Frequency);
            }
            catch { _serial.Release(); throw; }
        }

        public void CoolDown(TimeSpan delay) => _cooldown = DateTimeOffset.UtcNow + delay;
        public void Exit()
        {
            _nextStart = Math.Max(_nextStart, Stopwatch.GetTimestamp() + (long)(completionSpacing.TotalSeconds * Stopwatch.Frequency));
            _serial.Release();
        }
    }
}
