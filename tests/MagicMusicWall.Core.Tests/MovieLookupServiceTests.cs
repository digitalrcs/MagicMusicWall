using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class MovieLookupServiceTests
{
    private const string SyntheticToken = "synthetic.header-only.token";
    private const string OneMovie = """{"results":[{"id":42,"title":"Synthetic Film","release_date":"2001-02-03","genre_ids":[18]}]}""";
    private const string Genres = """{"genres":[{"id":18,"name":"Drama"},{"id":35,"name":"Comedy"}]}""";

    [Fact]
    public async Task SearchUsesOfficialEndpointHeaderAuthenticationAndCachedGenres()
    {
        var genreCalls = 0;
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.themoviedb.org", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(SyntheticToken, request.Headers.Authorization?.Parameter);
            Assert.DoesNotContain(SyntheticToken, request.RequestUri.AbsoluteUri);
            if (request.RequestUri.AbsolutePath.EndsWith("/genre/movie/list")) { genreCalls++; return Task.FromResult(Json(Genres)); }
            Assert.Equal("/3/search/movie", request.RequestUri.AbsolutePath);
            Assert.Contains("include_adult=false", request.RequestUri.Query);
            Assert.Contains("page=1", request.RequestUri.Query);
            Assert.Contains("query=A%20%26%20B%3F", request.RequestUri.AbsoluteUri);
            return Task.FromResult(Json(OneMovie));
        });
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("A & B?", SyntheticToken);
        Assert.Equal("Synthetic Film", Assert.Single(result.Items).Title);
        Assert.Equal(["Drama"], result.Items[0].Genres);
        Assert.Equal("https://www.themoviedb.org/movie/42", result.Items[0].SourceUrl);
        await service.SearchAsync("A & B?", SyntheticToken);
        Assert.Equal(1, genreCalls);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("", SyntheticToken)]
    [InlineData("Film", "")]
    [InlineData("Film", "bad\r\ntoken")]
    [InlineData("Film\n", SyntheticToken)]
    public async Task InvalidInputDoesNotRequest(string title, string token)
    {
        using var handler = new FakeHandler((_, _) => throw new Xunit.Sdk.XunitException("No request expected"));
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync(title, token);
        Assert.Empty(result.Items);
        Assert.NotEmpty(result.Message!);
    }

    [Fact]
    public async Task MalformedSiblingItemsDoNotDiscardValidMovieAndUnknownGenresStayEmpty()
    {
        var data = """
        {"results":[null,3,{"id":"wrong","title":"Bad"},{"id":0,"title":"Zero"},{"id":4,"title":"Adult","adult":true},
        {"id":42,"title":"Synthetic Film","original_title":"Original","release_date":"2024-02-30","overview":"First\nSecond","genre_ids":[18,18,35,999,"18",null]},
        {"id":42,"title":"Duplicate"},{"id":43,"title":"Other","release_date":"2024-02-29","genre_ids":[999]}]}
        """;
        using var handler = new FakeHandler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/genre/movie/list") ? Genres : data)));
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Equal([42L, 43L], result.Items.Select(x => x.Id));
        Assert.Equal("Original", result.Items[0].OriginalTitle);
        Assert.Equal("", result.Items[0].ReleaseDate);
        Assert.Equal("First\nSecond", result.Items[0].Overview);
        Assert.Equal(["Drama", "Comedy"], result.Items[0].Genres);
        Assert.Equal("2024-02-29", result.Items[1].ReleaseDate);
        Assert.Empty(result.Items[1].Genres);
    }

    [Fact]
    public async Task ResultLimitRetainsProviderOrder()
    {
        var data = JsonSerializer.Serialize(new { results = Enumerable.Range(1, 50).Select(i => new { id = i, title = "Movie " + i }) });
        using var handler = new FakeHandler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/genre/movie/list") ? Genres : data)));
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), result.Items.Select(x => x.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Token")]
    [InlineData(HttpStatusCode.Forbidden, "Token")]
    [InlineData(HttpStatusCode.Redirect, "redirected")]
    public async Task AuthenticationAndRedirectFailuresAreSafeAndClear(HttpStatusCode status, string expected)
    {
        var calls = 0;
        using var handler = new FakeHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(SyntheticToken) }); });
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Empty(result.Items);
        Assert.Contains(expected, result.Message);
        Assert.DoesNotContain(SyntheticToken, result.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GenreRateLimitPreservesMoviesAndPreventsImmediateRetry()
    {
        var calls = 0;
        using var handler = new FakeHandler((request, _) =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/search/movie")) return Task.FromResult(Json(OneMovie));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var first = await service.SearchAsync("Film", SyntheticToken);
        Assert.Single(first.Items);
        Assert.Empty(first.Items[0].Genres);
        Assert.Contains("genres could not", first.Message);
        var second = await service.SearchAsync("Film", SyntheticToken);
        Assert.Empty(second.Items);
        Assert.Contains("Try again", second.Message);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"results\":{}}")]
    public async Task MalformedResponsesReportFailure(string json)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Json(json)));
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Empty(result.Items);
        Assert.NotEmpty(result.Message!);
        Assert.DoesNotContain("No matching", result.Message);
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var handler = new FakeHandler((_, _) =>
        {
            var response = Json("{}");
            response.Content.Headers.ContentLength = 3 * 1024 * 1024;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client);
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Empty(result.Items);
        Assert.Contains("too much data", result.Message);
    }

    [Fact]
    public async Task CancellationPropagatesAndTimeoutBecomesMessage()
    {
        using var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
        using var client = new HttpClient(handler);
        using var service = new MovieLookupService(client, TimeSpan.FromMilliseconds(30));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("Film", SyntheticToken, cancellation.Token));
        var result = await service.SearchAsync("Film", SyntheticToken);
        Assert.Empty(result.Items);
        Assert.Contains("too long", result.Message);
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
