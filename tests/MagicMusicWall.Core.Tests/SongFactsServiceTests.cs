using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class SongFactsServiceTests
{
    [Fact]
    public async Task SearchReturnsRankedCandidatesWithPlainDescriptionsAndNamedUserAgent()
    {
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("en.wikipedia.org", request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("gsrsearch=\"Nights & Lights\" Example Artist", query);
            Assert.Contains("explaintext=1", query);
            Assert.Contains("gsrlimit=6", query);
            Assert.Contains("MagicMusicWall/1.0", request.Headers.UserAgent.ToString());
            Assert.Contains("https://digitalrcs.com", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json(new { query = new { pages = new object[]
            {
                new { pageid = 20, ns = 0, index = 2, title = "Other article", extract = "Second result." },
                new { pageid = 10, ns = 0, index = 1, title = "Nights & Lights (song)", extract = "A   synthetic song.\n\nAnother paragraph." },
                new { pageid = -1, ns = 0, missing = true, title = "Missing" }
            } } }));
        });
        using var client = new HttpClient(handler);
        var results = await new SongFactsService(client).SearchAsync("Example Artist", "Nights & Lights");
        Assert.Equal(["10", "20"], results.Select(result => result.Id));
        Assert.Equal("A synthetic song.…", results[0].Description);
        Assert.Equal("https://en.wikipedia.org/?curid=10", results[0].Url);
    }

    [Fact]
    public async Task ArticleUsesSelectedPageIdAndIncludesSourceAndLicense()
    {
        using var handler = new StubHandler((request, _) =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("pageids=42", query);
            Assert.Contains("exintro=1", query);
            Assert.Contains("explaintext=1", query);
            return Task.FromResult(Article("First synthetic fact.\n\nSecond synthetic fact.", 42));
        });
        using var client = new HttpClient(handler);
        var article = await new SongFactsService(client).GetArticleAsync("42");
        Assert.Equal("Synthetic article", article.Title);
        Assert.Equal("First synthetic fact.\n\nSecond synthetic fact.", article.Text);
        Assert.Equal("https://en.wikipedia.org/?curid=42", article.Url);
        Assert.Contains("Wikipedia contributors", article.Attribution);
        Assert.Contains("CC BY-SA 4.0", article.Attribution);
        Assert.Contains(SongFactsService.LicenseUrl, article.Attribution);
        Assert.Contains(article.Url, article.Attribution);
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, false)]
    [InlineData(301, true)]
    public async Task ExtractRetainsAtMost300SourceWordsAndMarksTruncation(int wordCount, bool truncated)
    {
        var source = string.Join(" ", Enumerable.Range(1, wordCount).Select(i => "word" + i));
        using var handler = new StubHandler((_, _) => Task.FromResult(Article(source)));
        using var client = new HttpClient(handler);
        var article = await new SongFactsService(client).GetArticleAsync("42");
        Assert.Equal(Math.Min(wordCount, 300), article.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(truncated, article.Text.EndsWith('…'));
        Assert.DoesNotContain("word301", article.Text);
        Assert.StartsWith("word1 word2 ", article.Text);
    }

    [Fact]
    public async Task ExtractKeepsFirstTwoNonemptyParagraphsAndNormalizesWhitespace()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Article("\r\n \t\r\nOne  two.\r\n\r\n\tThree four.\n\nFive six.")));
        using var client = new HttpClient(handler);
        var article = await new SongFactsService(client).GetArticleAsync("42");
        Assert.Equal("One two.\n\nThree four.…", article.Text);
    }

    [Theory]
    [InlineData("{\"query\":{\"pages\":[{\"pageid\":42,\"ns\":0,\"title\":\"Empty\",\"extract\":\" \\t\\n \\u0001\"}]} }", "no introductory text")]
    [InlineData("{\"query\":{\"pages\":[{\"pageid\":42,\"ns\":0,\"title\":\"Ambiguous\",\"extract\":\"Many meanings\",\"pageprops\":{\"disambiguation\":\"\"}}]}}", "disambiguation")]
    [InlineData("{\"query\":{\"pages\":[{\"pageid\":42,\"missing\":true,\"title\":\"Missing\"}]}}", "no longer available")]
    public async Task UnavailableOrAmbiguousArticlesDoNotInventFacts(string json, string message)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(RawJson(json)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SongFactsService(client).GetArticleAsync("42"));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task BlankSearchInvalidPageIdAndPrecancelledRequestsNeverReachNetwork()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected network request"));
        using var client = new HttpClient(handler);
        var service = new SongFactsService(client);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(" ", "\t"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetArticleAsync("https://example.invalid/"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetArticleAsync("-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(new string('x', 257), "song"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("Artist", "Title", new CancellationToken(true)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task IdenticalQueriesUseBoundedInMemoryCache()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Article("A synthetic fact.")));
        using var client = new HttpClient(handler);
        var service = new SongFactsService(client);
        var first = await service.GetArticleAsync("42");
        var second = await service.GetArticleAsync("42");
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RateLimitedResponseHonorsRetryAfterWithoutImmediateRepeatRequest()
    {
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var service = new SongFactsService(client);
        var first = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetArticleAsync("42"));
        var second = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetArticleAsync("42"));
        Assert.Contains("limiting requests", first.Message);
        Assert.Contains("Try again in", second.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not-json", "unreadable response")]
    [InlineData("[]", "unexpected response")]
    [InlineData("{\"error\":{\"code\":\"maxlag\"}}", "busy")]
    [InlineData("{\"query\":{\"pages\":{}}}", "unexpected result list")]
    public async Task BadResponsesProduceReadableRetryMessages(string json, string message)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(RawJson(json)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SongFactsService(client).GetArticleAsync("42"));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(RawJson(new string('x', 270_000))));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SongFactsService(client).GetArticleAsync("42"));
        Assert.Contains("large response", error.Message);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellationWhileNetworkFailureIsReadable()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler(async (_, token) => { cancellation.Cancel(); await Task.Delay(5000, token); return Article("unused"); });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SongFactsService(client).GetArticleAsync("42", cancellation.Token));
        using var offlineHandler = new StubHandler((_, _) => throw new HttpRequestException("Synthetic failure"));
        using var offlineClient = new HttpClient(offlineHandler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SongFactsService(offlineClient).GetArticleAsync("42"));
        Assert.Contains("internet connection", error.Message);
    }

    private static HttpResponseMessage Article(string extract, int id = 42) => Json(new { query = new { pages = new[] { new { pageid = id, ns = 0, title = "Synthetic article", extract } } } });
    private static HttpResponseMessage Json(object body) => RawJson(JsonSerializer.Serialize(body));
    private static HttpResponseMessage RawJson(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return action(request, cancellationToken);
        }
    }
}
