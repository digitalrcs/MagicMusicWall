using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MagicMusicWall.Core;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteSongInformationTests : IAsyncLifetime
{
    private const string Token = "synthetic-song-information-access-key-32-characters";
    private readonly RemoteControlServer _server = new();
    private readonly SongControl _control = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };

    public Task InitializeAsync() => _server.StartAsync("127.0.0.1", 0, Token, _control);
    public async Task DisposeAsync() { _client.Dispose(); await _server.DisposeAsync(); }

    private async Task<HttpResponseMessage> Get(string suffix, string? key = Token, string? origin = null, string? baseUrl = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, (baseUrl ?? _server.BaseUrl) + "/api/song/" + suffix);
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await _client.SendAsync(request);
    }

    [Theory]
    [InlineData("track-1")]
    [InlineData("track-1/lookup")]
    [InlineData("track-1/lyrics")]
    [InlineData("track-1/facts")]
    [InlineData("track-1/facts/42")]
    public async Task EverySongRouteRequiresTheRemoteKeyBeforeReadingTheCatalog(string suffix)
    {
        foreach (var key in new string?[] { null, "wrong-key" })
        {
            using var response = await Get(suffix, key);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("Blue Sky", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Theory]
    [InlineData("track-1")]
    [InlineData("track-1/lookup")]
    [InlineData("track-1/lyrics")]
    [InlineData("track-1/facts")]
    [InlineData("track-1/facts/42")]
    public async Task CrossOriginSongRequestsCannotReachCallbacks(string suffix)
    {
        using var response = await Get(suffix, origin: "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Fact]
    public async Task DetailsReturnMetadataAndCachedLyricsWithoutStartingAnOnlineLookup()
    {
        using var response = await Get("track-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<RemoteSongDetails>();
        Assert.Equal("track-1", details!.Song.Id);
        Assert.Equal("Blue Sky", details.Song.Title);
        Assert.Equal("The Sample Band", details.Song.Artist);
        Assert.Equal(182.5, details.Song.DurationSeconds);
        Assert.Equal("Synthetic cached lyrics", details.Lyrics!.PlainText);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"durationSeconds\":182.5", json);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(1, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("missing/lookup")]
    [InlineData("missing/lyrics")]
    [InlineData("missing/facts")]
    [InlineData("missing/facts/42")]
    public async Task UnknownLibrarySongDoesNotStartAnyOnlineLookup(string suffix)
    {
        using var response = await Get(suffix);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Theory]
    [InlineData("track-1")]
    [InlineData("track-1/lookup")]
    [InlineData("track-1/lyrics")]
    [InlineData("track-1/facts")]
    [InlineData("track-1/facts/42")]
    public async Task OlderDesktopWithoutSongInformationReturnsNotFound(string suffix)
    {
        await using var olderServer = new RemoteControlServer();
        await olderServer.StartAsync("127.0.0.1", 0, Token, new PlaybackOnlyControl());
        using var response = await Get(suffix, baseUrl: olderServer.BaseUrl);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("lookup", "recordings")]
    [InlineData("lyrics", "lyrics")]
    [InlineData("facts", "facts")]
    public async Task OmittedSearchValuesUseTheSelectedSongsMetadata(string route, string operation)
    {
        using var response = await Get("track-1/" + route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lookup = Assert.Single(_control.Lookups);
        Assert.Equal(new Lookup(operation, "track-1", "The Sample Band", "Blue Sky", null), lookup);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(route == "lyrics" ? "Synthetic online lyrics" : "Blue Sky", json);
    }

    [Theory]
    [InlineData("lookup?artist=%20%20", "recordings", "", "Blue Sky")]
    [InlineData("facts?title=%20%20", "facts", "The Sample Band", "")]
    [InlineData("lyrics?artist=%20Bj%C3%B6rk%20%26%20Friends%20&title=%20Why%3F%20", "lyrics", "Björk & Friends", "Why?")]
    public async Task ExplicitCorrectionsAreTrimmedAndDoNotFallBackWhenEmpty(string query, string operation, string artist, string title)
    {
        using var response = await Get("track-1/" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Lookup(operation, "track-1", artist, title, null), Assert.Single(_control.Lookups));
    }

    [Theory]
    [InlineData("lookup?artist=&title=%20%20")]
    [InlineData("lyrics?artist=&title=Song")]
    [InlineData("lyrics?title=")]
    [InlineData("facts?artist=&title=%20%20")]
    public async Task RequiredSearchValuesCannotBeExplicitlyCleared(string query)
    {
        using var response = await Get("track-1/" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_control.Lookups);
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("lyrics")]
    [InlineData("facts")]
    public async Task DuplicateExcessiveControlAndUnexpectedQueryValuesAreRejected(string route)
    {
        foreach (var query in new[] { "artist=a&artist=b", "title=a&title=b", "artist=" + new string('a', 257),
            "title=" + new string('t', 257), "artist=a%0Ab", "title=a%00b", "url=https%3A%2F%2Funrelated.example", "path=C%3A%5Cmusic" })
        {
            using var response = await Get("track-1/" + route + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Fact]
    public async Task MaximumLengthSearchFieldsRemainUsable()
    {
        var artist = new string('a', 256);
        var title = new string('t', 256);
        using var response = await Get("track-1/lookup?artist=" + artist + "&title=" + title);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Lookup("recordings", "track-1", artist, title, null), Assert.Single(_control.Lookups));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.5")]
    [InlineData("not-a-number")]
    [InlineData("9223372036854775808")]
    public async Task InvalidArticleIdentifiersAreRejectedBeforeCallbacks(string pageId)
    {
        using var response = await Get("track-1/facts/" + pageId);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _control.LocalReads);
        Assert.Empty(_control.Lookups);
    }

    [Theory]
    [InlineData("00042", "42")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    public async Task ArticleIdentifierIsNormalizedAndReturnsAttributedFacts(string input, string normalized)
    {
        using var response = await Get("track-1/facts/" + input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Lookup("article", "track-1", null, null, normalized), Assert.Single(_control.Lookups));
        var article = await response.Content.ReadFromJsonAsync<SongFactsArticle>();
        Assert.Equal("Synthetic facts", article!.Text);
        Assert.Equal("Wikipedia contributors, CC BY-SA 4.0", article.Attribution);
        Assert.Equal("https://en.wikipedia.org/?curid=" + normalized, article.Url);
    }

    [Theory]
    [InlineData("facts")]
    [InlineData("facts/42")]
    public async Task ProviderFailureIsFriendlyWithoutPublishingExceptionDetails(string route)
    {
        _control.FailFacts = true;
        using var response = await Get("track-1/" + route);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Wikipedia could not complete", json);
        Assert.DoesNotContain("private-music", json);
    }

    [Fact]
    public async Task SongRemovedBetweenReadAndLookupReturnsNotFound()
    {
        _control.RemoveBeforeLookup = true;
        using var response = await Get("track-1/lookup");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("private-music", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StoppingServerCancelsAnInFlightSongLookup()
    {
        _control.BlockLookup = true;
        var pending = Get("track-1/lookup");
        await _control.LookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var response = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(_control.LookupCancelled);
    }

    private sealed record Lookup(string Operation, string Id, string? Artist, string? Title, string? PageId);

    private class PlaybackOnlyControl : IRemoteControl
    {
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class SongControl : PlaybackOnlyControl, IRemoteSongInformation
    {
        public int LocalReads { get; private set; }
        public List<Lookup> Lookups { get; } = [];
        public bool FailFacts { get; set; }
        public bool RemoveBeforeLookup { get; set; }
        public bool BlockLookup { get; set; }
        public bool LookupCancelled { get; private set; }
        public TaskCompletionSource LookupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RemoteSongDetails?> GetSongAsync(string id, CancellationToken cancellationToken)
        {
            ++LocalReads;
            return Task.FromResult(id == "track-1" ? new RemoteSongDetails(new(id, "Blue Sky", "The Sample Band", "Sample Album", 182.5),
                new() { Found = true, PlainText = "Synthetic cached lyrics" }) : null);
        }

        public async Task<MusicSearchResult> SearchRecordingsAsync(string id, string artist, string title, CancellationToken cancellationToken)
        {
            if (RemoveBeforeLookup) throw new KeyNotFoundException("C:\\private-music\\removed.mp3");
            Lookups.Add(new("recordings", id, artist, title, null));
            LookupStarted.TrySetResult();
            if (BlockLookup)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { LookupCancelled = true; throw; }
            }
            return new() { Items = [new() { Id = "recording-1", Title = title, Artist = artist, Album = "Sample Album", Score = 95 }] };
        }

        public Task<LyricsResult> GetLyricsAsync(string id, string artist, string title, CancellationToken cancellationToken)
        {
            Lookups.Add(new("lyrics", id, artist, title, null));
            return Task.FromResult(new LyricsResult { Found = true, PlainText = "Synthetic online lyrics", MatchedArtist = artist, MatchedTitle = title });
        }

        public Task<RemoteFactSearch> SearchFactsAsync(string id, string artist, string title, CancellationToken cancellationToken)
        {
            Lookups.Add(new("facts", id, artist, title, null));
            if (FailFacts) throw new InvalidOperationException("Could not open C:\\private-music\\track.mp3");
            return Task.FromResult(new RemoteFactSearch([new("42", "Blue Sky", "Synthetic description", "https://en.wikipedia.org/?curid=42")], "Choose an article."));
        }

        public Task<SongFactsArticle> GetFactAsync(string id, string pageId, CancellationToken cancellationToken)
        {
            Lookups.Add(new("article", id, null, null, pageId));
            if (FailFacts) throw new InvalidOperationException("Could not open C:\\private-music\\track.mp3");
            return Task.FromResult(new SongFactsArticle("Blue Sky", "Synthetic facts", "https://en.wikipedia.org/?curid=" + pageId, "Wikipedia contributors, CC BY-SA 4.0"));
        }
    }
}
