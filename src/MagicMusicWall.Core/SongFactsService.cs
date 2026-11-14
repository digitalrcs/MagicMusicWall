using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MagicMusicWall.Core;

public sealed record FactArticle(string Id, string Title, string Description, string Url);
public sealed record SongFactsArticle(string Title, string Text, string Url, string Attribution);

/// <summary>On-demand English Wikipedia article lookup. Sends only the supplied search text or selected page ID.</summary>
public sealed class SongFactsService
{
    private const string ApiUrl = "https://en.wikipedia.org/w/api.php";
    private const int MaximumResponseBytes = 256 * 1024;
    private const string UserAgent = "MagicMusicWall/1.0 (+https://digitalrcs.com)";
    public const string LicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/";
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });
    private readonly HttpClient _client;
    private readonly Dictionary<string, (DateTimeOffset Expires, string Json)> _cache = new(StringComparer.Ordinal);
    private DateTimeOffset _retryAfter;

    public SongFactsService(HttpClient? client = null) => _client = client ?? DefaultClient;

    public async Task<IReadOnlyList<FactArticle>> SearchAsync(string artist, string title, CancellationToken cancellationToken = default)
    {
        artist = SearchField(artist, nameof(artist));
        title = SearchField(title, nameof(title));
        if (artist.Length == 0 && title.Length == 0)
            throw new ArgumentException("Enter an artist or song title to search Wikipedia.");
        // Quote the title so a multi-word song remains a phrase. Artist text helps distinguish similarly named songs.
        var search = string.Join(" ", new[] { title.Length > 0 ? "\"" + title.Replace('"', ' ') + "\"" : "", artist }.Where(s => s.Length > 0));
        using var document = await QueryAsync(new()
        {
            ["generator"] = "search", ["gsrsearch"] = search, ["gsrnamespace"] = "0", ["gsrlimit"] = "6",
            ["prop"] = "extracts|pageprops", ["ppprop"] = "disambiguation", ["exintro"] = "1",
            ["explaintext"] = "1", ["exchars"] = "280", ["exlimit"] = "6"
        }, cancellationToken).ConfigureAwait(false);
        var pages = Pages(document.RootElement);
        return pages.Where(UsablePage).OrderBy(p => Integer(p, "index", int.MaxValue)).Take(6).Select(page =>
        {
            var id = Integer(page, "pageid").ToString(CultureInfo.InvariantCulture);
            var description = IsDisambiguation(page) ? "Disambiguation page — choose a specific song or artist article." : Excerpt(String(page, "extract"), 50, 1);
            if (description.Length == 0) description = "Open the source article to read more.";
            return new FactArticle(id, String(page, "title"), description, ArticleUrl(id));
        }).ToArray();
    }

    public async Task<SongFactsArticle> GetArticleAsync(string pageId, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(pageId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0)
            throw new ArgumentException("Choose an article from the Wikipedia results first.", nameof(pageId));
        pageId = parsedId.ToString(CultureInfo.InvariantCulture);
        using var document = await QueryAsync(new()
        {
            ["pageids"] = pageId, ["redirects"] = "1", ["prop"] = "extracts|pageprops", ["ppprop"] = "disambiguation",
            ["exintro"] = "1", ["explaintext"] = "1", ["exchars"] = "1200", ["exlimit"] = "1"
        }, cancellationToken).ConfigureAwait(false);
        var page = Pages(document.RootElement).FirstOrDefault(UsablePage);
        if (page.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("That Wikipedia article is no longer available. Search again and choose another result.");
        if (IsDisambiguation(page))
            throw new InvalidOperationException("This result is a disambiguation page. Search with the artist name and choose a specific song or album article.");
        var extract = Excerpt(String(page, "extract"), 300, 2);
        if (extract.Length == 0)
            throw new InvalidOperationException("Wikipedia has no introductory text for this article. Open its source link or choose another result.");
        var url = ArticleUrl(Integer(page, "pageid").ToString(CultureInfo.InvariantCulture));
        return new SongFactsArticle(String(page, "title"), extract, url,
            $"Excerpt from Wikipedia contributors, CC BY-SA 4.0. Text shortened and whitespace normalized. Source: {url} · License: {LicenseUrl}");
    }

    private async Task<JsonDocument> QueryAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        parameters["action"] = "query";
        parameters["format"] = "json";
        parameters["formatversion"] = "2";
        parameters["maxlag"] = "5";
        var query = string.Join("&", parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(query, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
                return JsonDocument.Parse(cached.Json);
            if (_retryAfter > DateTimeOffset.UtcNow)
                throw new InvalidOperationException($"Wikipedia is busy. Try again in {Math.Max(1, (int)Math.Ceiling((_retryAfter - DateTimeOffset.UtcNow).TotalSeconds))} seconds.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl + "?" + query);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                _retryAfter = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("Wikipedia is busy or limiting requests. Please wait a moment and try again.");
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Wikipedia could not return the article (HTTP {(int)response.StatusCode}). Try again later.");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidOperationException("Wikipedia returned an unexpectedly large response. Try a more specific search.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + read > MaximumResponseBytes)
                    throw new InvalidOperationException("Wikipedia returned an unexpectedly large response. Try a more specific search.");
                bytes.Write(buffer, 0, read);
            }
            var json = Encoding.UTF8.GetString(bytes.ToArray());
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new InvalidOperationException("Wikipedia returned an unexpected response. Please try again later.");
            }
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = String(error, "code");
                document.Dispose();
                if (code is "maxlag" or "ratelimited")
                {
                    _retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                    throw new InvalidOperationException("Wikipedia is busy or limiting requests. Please wait a moment and try again.");
                }
                throw new InvalidOperationException("Wikipedia could not complete this search. Try a different artist or song title.");
            }
            foreach (var key in _cache.Where(p => p.Value.Expires <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _cache.Remove(key);
            if (_cache.Count >= 32) _cache.Remove(_cache.MinBy(pair => pair.Value.Expires).Key);
            _cache[query] = (DateTimeOffset.UtcNow.AddMinutes(10), json);
            return document;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Wikipedia took too long to respond. Check your internet connection and try again.");
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException("Could not reach Wikipedia. Check your internet connection and try again.", error);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("The connection to Wikipedia was interrupted. Please try again.", error);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("Wikipedia returned an unreadable response. Please try again later.", error);
        }
        finally { RequestGate.Release(); }
    }

    private static string SearchField(string? value, string name)
    {
        value ??= "";
        if (value.Length > 512) throw new ArgumentException("Keep each artist or song title under 256 characters.", name);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        if (value.Length > 256) throw new ArgumentException("Keep each artist or song title under 256 characters.", name);
        return value;
    }

    private static JsonElement[] Pages(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query)) return [];
        if (query.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Wikipedia returned an unexpected result list. Please try again later.");
        if (!query.TryGetProperty("pages", out var pages)) return [];
        if (pages.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Wikipedia returned an unexpected result list. Please try again later.");
        return pages.EnumerateArray().ToArray();
    }
    private static string String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static long Integer(JsonElement value, string name, long fallback = 0) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var number) ? number : fallback;
    private static bool UsablePage(JsonElement page) => page.ValueKind == JsonValueKind.Object && Integer(page, "pageid") > 0 && Integer(page, "ns") == 0 && !page.TryGetProperty("missing", out _) && String(page, "title").Length > 0;
    private static bool IsDisambiguation(JsonElement page) => page.TryGetProperty("pageprops", out var props) && props.ValueKind == JsonValueKind.Object && props.TryGetProperty("disambiguation", out _);
    private static string ArticleUrl(string pageId) => "https://en.wikipedia.org/?curid=" + pageId;

    private static string Excerpt(string text, int maximumWords, int maximumParagraphs)
    {
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        var paragraphs = Regex.Split(text, @"\r?\n\s*\r?\n").Select(p => Regex.Replace(p, @"\s+", " ").Trim()).Where(p => p.Length > 0).ToArray();
        var result = new List<string>();
        var remaining = maximumWords;
        var truncated = paragraphs.Length > maximumParagraphs;
        foreach (var paragraph in paragraphs.Take(maximumParagraphs))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > remaining) truncated = true;
            if (remaining > 0) result.Add(string.Join(" ", words.Take(remaining)));
            remaining -= Math.Min(remaining, words.Length);
        }
        var excerpt = string.Join("\n\n", result);
        return truncated && excerpt.Length > 0 ? excerpt.TrimEnd('…') + "…" : excerpt;
    }
}
