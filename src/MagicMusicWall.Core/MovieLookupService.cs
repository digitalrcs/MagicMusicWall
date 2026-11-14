using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed record MovieMatch
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string Overview { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public IReadOnlyList<string> Genres { get; init; } = [];
}

public sealed record MovieSearchResult
{
    public IReadOnlyList<MovieMatch> Items { get; init; } = [];
    public string? Message { get; init; }
}

/// <summary>User-initiated movie searches only. Does not store credentials or edit media files.</summary>
public sealed class MovieLookupService : IDisposable
{
    public const string Attribution = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
    public const string Website = "https://www.themoviedb.org";
    private const string ApiRoot = "https://api.themoviedb.org/3/";
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private Dictionary<int, string>? _genres;
    private DateTimeOffset _genresExpire, _cooldown;

    public MovieLookupService() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan }) => _ownsClient = true;

    /// <summary>The caller owns this client and must disable automatic redirects on its handler.</summary>
    public MovieLookupService(HttpClient client, TimeSpan? requestTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(25);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public async Task<MovieSearchResult> SearchAsync(string title, string readAccessToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(title, readAccessToken);
        if (validation is not null) return new() { Message = validation };
        title = title.Trim();
        readAccessToken = readAccessToken.Trim();
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cooldown > DateTimeOffset.UtcNow)
                return new() { Message = $"TMDB is busy. Try again in {WaitSeconds(_cooldown - DateTimeOffset.UtcNow)} seconds." };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            using var search = await Request("search/movie?include_adult=false&language=en-US&page=1&query=" + Uri.EscapeDataString(title), readAccessToken, timeout.Token).ConfigureAwait(false);
            if (search.RootElement.ValueKind != JsonValueKind.Object || !search.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new LookupFailure("TMDB returned an unexpected movie response. Please try again later.");

            var candidates = results.EnumerateArray().Take(100).Select(ParseMovie).OfType<MovieCandidate>()
                .DistinctBy(m => m.Movie.Id).Take(20).ToList();
            if (candidates.Count == 0) return new() { Message = "No matching movies found. Try the movie title without file extensions or release-group text." };

            string? genreWarning = null;
            if (_genres is null || _genresExpire <= DateTimeOffset.UtcNow)
            {
                try
                {
                    using var response = await Request("genre/movie/list?language=en-US", readAccessToken, timeout.Token).ConfigureAwait(false);
                    _genres = ParseGenres(response.RootElement);
                    _genresExpire = DateTimeOffset.UtcNow.AddDays(1);
                }
                catch (Exception ex) when (FailureMessage(ex, cancellationToken) is not null)
                {
                    genreWarning = " Movie genres could not be refreshed. " + FailureMessage(ex, cancellationToken);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var items = candidates.Select(candidate => candidate.Movie with
            {
                Genres = candidate.GenreIds.Where(id => _genres?.ContainsKey(id) == true)
                    .Select(id => _genres![id]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            }).ToArray();
            return new() { Items = items, Message = "Choose the movie and release year that match your file; online matches are suggestions." + genreWarning };
        }
        catch (Exception ex) when (FailureMessage(ex, cancellationToken) is not null)
        {
            return new() { Message = FailureMessage(ex, cancellationToken) };
        }
        finally { _serial.Release(); }
    }

    private static string? Validate(string? title, string? token)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Enter a movie title before looking online.";
        if (title.Length > 256 || title.Any(char.IsControl)) return "Use a movie title of 256 characters or fewer without control characters.";
        if (string.IsNullOrWhiteSpace(token)) return "Add your TMDB API Read Access Token in Setup to look up movie metadata.";
        if (token.Length > 8192 || token.Any(char.IsControl) || token.Trim().Any(c => !char.IsAsciiLetterOrDigit(c) && !"-._~+/=".Contains(c)))
            return "The TMDB API Read Access Token has an invalid format. Check the token in Setup.";
        return null;
    }

    private async Task<JsonDocument> Request(string relativeUri, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("MagicMusicWall/0.4 (https://digitalrcs.com)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new LookupFailure("TMDB did not accept the API Read Access Token. Check it in Setup.");
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow ?? TimeSpan.FromSeconds(10);
            delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 86400));
            _cooldown = DateTimeOffset.UtcNow.Add(delay);
            throw new LookupFailure($"TMDB is busy or rate limited. Try again in {WaitSeconds(delay)} seconds.");
        }
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new LookupFailure("TMDB redirected the request. The lookup was stopped; please try again later.");
        if (!response.IsSuccessStatusCode)
            throw new LookupFailure($"TMDB could not complete the lookup (HTTP {(int)response.StatusCode}). Please try again later.");
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is not null && contentType != "application/json" && !contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            throw new LookupFailure("TMDB returned an unexpected response format. Please try again later.");
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new LookupFailure("TMDB returned too much data. Try a more specific movie title.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumResponseBytes)
                throw new LookupFailure("TMDB returned too much data. Try a more specific movie title.");
            buffer.Write(chunk, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static MovieCandidate? ParseMovie(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var movieId) || movieId <= 0
            || item.TryGetProperty("adult", out var adult) && adult.ValueKind == JsonValueKind.True) return null;
        var title = Text(item, "title", 512);
        if (title.Length == 0) return null;
        var date = Text(item, "release_date", 10);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = "";
        var genres = new List<int>();
        if (item.TryGetProperty("genre_ids", out var values) && values.ValueKind == JsonValueKind.Array)
            foreach (var value in values.EnumerateArray().Take(50))
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var genre) && genre > 0) genres.Add(genre);
        return new(new()
        {
            Id = movieId, Title = title, OriginalTitle = Text(item, "original_title", 512), ReleaseDate = date,
            Overview = Text(item, "overview", 16000, allowNewlines: true), SourceUrl = Website + "/movie/" + movieId.ToString(CultureInfo.InvariantCulture)
        }, genres);
    }

    private static Dictionary<int, string> ParseGenres(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
            throw new LookupFailure("TMDB returned an unexpected genre response.");
        var result = new Dictionary<int, string>();
        foreach (var genre in genres.EnumerateArray().Take(200))
        {
            if (genre.ValueKind != JsonValueKind.Object || !genre.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var genreId) || genreId <= 0) continue;
            var name = Text(genre, "name", 100);
            if (name.Length > 0) result.TryAdd(genreId, name);
        }
        return result;
    }

    private static string Text(JsonElement item, string name, int maximumLength, bool allowNewlines = false)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        if (text.Length > maximumLength) return "";
        return new string(text.Where(c => !char.IsControl(c) || allowNewlines && c is '\r' or '\n' or '\t').ToArray()).Trim();
    }

    private static string? FailureMessage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException => "TMDB took too long to respond. Please try again.",
        LookupFailure failure => failure.Message,
        JsonException => "TMDB returned unreadable data. Please try again later.",
        HttpRequestException or IOException => "Could not reach TMDB. Check your internet connection and try again.",
        _ => null
    };

    private static string WaitSeconds(TimeSpan delay) => Math.Max(1, Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    private sealed record MovieCandidate(MovieMatch Movie, IReadOnlyList<int> GenreIds);
    private sealed class LookupFailure(string message) : Exception(message);
}
