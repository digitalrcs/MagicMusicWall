using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class MusicLookupServiceTests
{
    [Fact]
    public async Task RecordingSearchEscapesQueryAndIdentifiesApplication()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("musicbrainz.org", request.RequestUri!.Host);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("recording:\"A \\" + "\"quoted\\\" song\\?\" AND artist:\"AC\\/DC\\+\"", query);
            Assert.Contains("limit=12", query);
            Assert.Contains("MagicMusicWall/", request.Headers.UserAgent.ToString());
            Assert.Contains("https://digitalrcs.com", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json("""{"recordings":[]}"""));
        });
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        var result = await service.SearchRecordingsAsync("AC/DC+", "A \"quoted\" song?");
        Assert.Empty(result.Items);
        Assert.Contains("No matching", result.Message);
        Assert.StartsWith("https://musicbrainz.org/search?", result.SourceUrl);
    }

    [Fact]
    public async Task RecordingsKeepDifferentAlbumsAndArtistCreditAndDistinctDates()
    {
        var json = """
        {"recordings":[{"id":"11111111-1111-1111-1111-111111111111","title":"Synthetic Song","score":"98","length":123456,"first-release-date":"1970","disambiguation":"studio version","artist-credit":[{"name":"Alpha","joinphrase":" & "},{"artist":{"name":"Beta"}}],"releases":[{"id":"22222222-2222-2222-2222-222222222222","title":"First Album","date":"1971-01-02"},{"title":"first album","date":"1995"},{"title":"Compilation","date":"2002"}]}]}
        """;
        using var service = Service(json);
        var result = await service.SearchRecordingsAsync("Alpha", "Synthetic Song");
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Alpha & Beta", result.Items[0].Artist);
        Assert.Equal("1970", result.Items[0].FirstReleaseDate);
        Assert.Equal("1971-01-02", result.Items[0].ReleaseDate);
        Assert.Equal(123.456, result.Items[0].DurationSeconds);
        Assert.Equal(98, result.Items[0].Score);
        Assert.Equal("Compilation", result.Items[1].Album);
        Assert.EndsWith("11111111-1111-1111-1111-111111111111", result.Items[0].SourceUrl);
    }

    [Fact]
    public async Task RecordingResultsAreBoundedAndDoNotStarveAlternateRecordings()
    {
        var recordings = Enumerable.Range(1, 14).Select(index => new Dictionary<string, object>
        {
            ["id"] = Guid.NewGuid().ToString(), ["title"] = "Song " + index, ["score"] = 100 - index,
            ["artist-credit"] = new[] { new { name = "Artist" } },
            ["releases"] = Enumerable.Range(1, 15).Select(album => new { title = "Album " + album }).ToArray()
        });
        using var service = Service(JsonSerializer.Serialize(new { recordings }));
        var result = await service.SearchRecordingsAsync("Artist", "Song");
        Assert.Equal(12, result.Items.Count);
        Assert.Equal(12, result.Items.Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"recordings\":{}}")]
    [InlineData("{\"recordings\":[null,3,{\"id\":\"bad\"}]}")]
    public async Task InvalidRecordingResponsesReportFailure(string response)
    {
        using var service = Service(response);
        var result = await service.SearchRecordingsAsync("Artist", "Song");
        Assert.Empty(result.Items);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("No matching", result.Message);
    }

    [Fact]
    public async Task LyricsAcceptNormalizedIdentityAndInclusiveDurationBoundary()
    {
        using var service = Service(LyricsJson(Lyric(1, "aeol", "song blue live", "Set", 185, "Synthetic line A")));
        var result = await service.GetLyricsAsync("Aéol", "Song (Blue) Live", "Set", 180);
        Assert.True(result.Found);
        Assert.Equal("Synthetic line A", result.PlainText);
        Assert.Equal("https://lrclib.net/api/get/1", result.SourceUrl);
        Assert.Equal("song blue live", result.MatchedTitle);
    }

    [Theory]
    [InlineData("Artist", "Song live", 180)]
    [InlineData("Different artist", "Song", 180)]
    [InlineData("Artist", "Song", 186)]
    [InlineData("Artist", "Song", 0)]
    public async Task LyricsRejectWrongArtistVersionOrDuration(string artist, string title, double duration)
    {
        using var service = Service(LyricsJson(Lyric(1, artist, title, "Album", duration, "Synthetic line")));
        var result = await service.GetLyricsAsync("Artist", "Song", "Album", 180);
        Assert.False(result.Found);
        Assert.Empty(result.PlainText);
        Assert.Contains("No confident", result.Message);
    }

    [Fact]
    public async Task LyricsPreferExactAlbumAndRejectConflictingVersionsOtherwise()
    {
        var response = LyricsJson(Lyric(1, "Artist", "Song", "Deluxe", 196, "Synthetic deluxe line"), Lyric(2, "Artist", "Song", "Standard", 204, "Synthetic standard line"));
        using var service = Service(response);
        var result = await service.GetLyricsAsync("Artist", "Song", "deluxe", 200);
        Assert.True(result.Found);
        Assert.Equal("Synthetic deluxe line", result.PlainText);
        var ambiguous = await service.GetLyricsAsync("Artist", "Song", "", 200);
        Assert.False(ambiguous.Found);
        Assert.Equal("Several versions match. Choose the recording to view its lyrics.", ambiguous.Message);
        Assert.Empty(ambiguous.PlainText);
        Assert.Equal(2, ambiguous.Choices.Count);
        Assert.Equal(["1", "2"], ambiguous.Choices.Select(choice => choice.Id));
        Assert.Equal([196d, 204d], ambiguous.Choices.Select(choice => choice.DurationSeconds));
        Assert.All(ambiguous.Choices, choice =>
        {
            Assert.True(choice.Lyrics.Found);
            Assert.Empty(choice.Lyrics.Choices);
            Assert.Equal("Artist", choice.Lyrics.MatchedArtist);
            Assert.Equal("Song", choice.Lyrics.MatchedTitle);
            Assert.EndsWith("/" + choice.Id, choice.Lyrics.SourceUrl);
            Assert.Contains("explicitly selected", choice.Lyrics.Message);
        });
    }

    [Fact]
    public async Task AmbiguousChoicesAreFilteredDeduplicatedSortedAndBounded()
    {
        var records = Enumerable.Range(1, 10).Reverse().Select(id => Lyric(id, "Artist", "Song", "Album", 180 + id * .2, "Synthetic version " + id)).ToList();
        records.Add(Lyric(50, "Wrong artist", "Song", "Album", 180, "Synthetic wrong artist"));
        records.Add(Lyric(51, "Artist", "Song live", "Album", 180, "Synthetic wrong version"));
        records.Add(Lyric(52, "Artist", "Song", "Album", 186, "Synthetic wrong duration"));
        records.Add(Lyric(53, "Artist", "Song", "Album", 184, "Synthetic version 1"));
        records.Add(Lyric(54, "Artist", "Song", "Other album", 180, "Synthetic wrong album"));
        using var service = Service(LyricsJson(records.ToArray()));
        var result = await service.GetLyricsAsync("Artist", "Song", "Album", 180);
        Assert.False(result.Found);
        Assert.Empty(result.PlainText);
        Assert.Empty(result.SyncedText);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i.ToString()), result.Choices.Select(choice => choice.Id));
        Assert.All(result.Choices, choice =>
        {
            Assert.Equal("Artist", choice.Lyrics.MatchedArtist);
            Assert.Equal("Song", choice.Lyrics.MatchedTitle);
            Assert.Equal("Album", choice.Lyrics.MatchedAlbum);
            Assert.InRange(choice.DurationSeconds, 175, 185);
            Assert.Equal("Synthetic version " + choice.Id, choice.Lyrics.PlainText);
            Assert.Empty(choice.Lyrics.Choices);
        });
        Assert.Equal(8, result.Choices.Select(choice => choice.Id).Distinct().Count());
    }

    [Fact]
    public async Task IdenticalDuplicatesAreAcceptedWithUnknownDurationNotice()
    {
        using var service = Service(LyricsJson(Lyric(1, "Artist", "Song", "Album", 0, "Synthetic same line"), Lyric(2, "Artist", "Song", "Album", 0, "Synthetic same line")));
        var result = await service.GetLyricsAsync("Artist", "Song", "Album", 0);
        Assert.True(result.Found);
        Assert.Contains("Duration was unavailable", result.Message);
        Assert.Empty(result.Choices);
    }

    [Fact]
    public async Task InstrumentalResultsDoNotExposeContradictoryLyricsText()
    {
        using var service = Service(LyricsJson(new { id = 1, artistName = "Artist", trackName = "Song", albumName = "Album", duration = 180, instrumental = true, plainLyrics = "Synthetic conflicting line" }));
        var result = await service.GetLyricsAsync("Artist", "Song", "Album", 180);
        Assert.True(result.Found);
        Assert.True(result.Instrumental);
        Assert.Empty(result.PlainText);
        Assert.Contains("instrumental", result.Message);
    }

    [Theory]
    [InlineData("broken JSON")]
    [InlineData("{}")]
    [InlineData("[null,3,{\"id\":\"invalid\"}]")]
    public async Task InvalidLyricsResponseReportsProviderFailure(string response)
    {
        using var service = Service(response);
        var result = await service.GetLyricsAsync("Artist", "Song", "", 0);
        Assert.False(result.Found);
        Assert.Contains("LRCLIB returned", result.Message);
    }

    [Fact]
    public async Task QueryEncodingPreventsExtraParametersAndSendsNoLocalPath()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("lrclib.net", request.RequestUri!.Host);
            Assert.Equal("?artist_name=A%26B&track_name=Song%20%2B%20%231", request.RequestUri.Query);
            Assert.Contains("MagicMusicWall", request.Headers.UserAgent.ToString());
            return Task.FromResult(Json("[]"));
        });
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        await service.GetLyricsAsync("A&B", "Song + #1", "Album", 180);
    }

    [Fact]
    public async Task InvalidInputsDoNotSendRequests()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Should not send"));
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        Assert.Contains("Enter a title", (await service.SearchRecordingsAsync("Artist", " ")).Message);
        Assert.Contains("256", (await service.SearchRecordingsAsync("Artist", new string('x', 257))).Message);
        Assert.Contains("Enter an artist", (await service.GetLyricsAsync("", "Song", "", 0)).Message);
        Assert.Contains("finite", (await service.GetLyricsAsync("Artist", "Song", "", double.NaN)).Message);
        Assert.Contains("control", (await service.GetLyricsAsync("Artist\nOther", "Song", "", 0)).Message);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task HttpFailureAndNonJsonResponseAreNotMistakenForNoMatches()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        Assert.Contains("HTTP 502", (await service.GetLyricsAsync("Artist", "Song", "", 0)).Message);
        handler.Callback = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>edge failure</html>", Encoding.UTF8, "text/html") });
        Assert.Contains("format", (await service.GetLyricsAsync("Artist", "Song", "", 0)).Message);
    }

    [Fact]
    public async Task TimeoutsAreReportedButCallerCancellationPropagates()
    {
        using var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("[]"); });
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client, TimeSpan.FromMilliseconds(40));
        Assert.Contains("too long", (await service.GetLyricsAsync("Artist", "Song", "", 0)).Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetLyricsAsync("Artist", "Song", "", 0, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchRecordingsAsync("Artist", "Song", cancellation.Token));
    }

    [Fact]
    public async Task RetryAfterIsHonoredWithoutAutomaticOrEarlyManualRetry()
    {
        using var handler = new FakeHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        try
        {
            var limited = await service.GetLyricsAsync("Artist", "Song", "", 0);
            Assert.Contains("rate limited", limited.Message);
            Assert.Contains("1 second before", limited.Message);
            Assert.DoesNotContain("1 seconds", limited.Message);
            Assert.Contains("asked us to wait", (await service.GetLyricsAsync("Artist", "Song", "", 0)).Message);
            Assert.Equal(1, handler.Count);
        }
        finally { await Task.Delay(1400); }
    }

    [Fact]
    public async Task CancellationWhileRequestIsActivePropagatesAndReleasesProviderGate()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Json("[]"); });
        using var client = new HttpClient(handler);
        using var service = new MusicLookupService(client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetLyricsAsync("Artist", "Song", "", 0, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        handler.Callback = (_, _) => Task.FromResult(Json("[]"));
        var next = await service.GetLyricsAsync("Artist", "Song", "", 0);
        Assert.Contains("No confident", next.Message);
    }

    [Fact]
    public async Task MusicBrainzRequestsAreSpacedAcrossServiceInstances()
    {
        var starts = new List<long>();
        using var handler = new FakeHandler((_, _) => { starts.Add(Stopwatch.GetTimestamp()); return Task.FromResult(Json("""{"recordings":[]}""")); });
        using var client = new HttpClient(handler);
        using var first = new MusicLookupService(client);
        using var second = new MusicLookupService(client);
        await Task.WhenAll(first.SearchRecordingsAsync("Artist", "One"), second.SearchRecordingsAsync("Artist", "Two"));
        Assert.Equal(2, starts.Count);
        Assert.True(Stopwatch.GetElapsedTime(starts[0], starts[1]) >= TimeSpan.FromMilliseconds(990));
    }

    private static MusicLookupService Service(string response) => new(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Json(response)))));
    private static HttpResponseMessage Json(string response) => new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    private static string LyricsJson(params object[] records) => JsonSerializer.Serialize(records);
    private static object Lyric(int id, string artist, string title, string album, double duration, string plain) => new { id, artistName = artist, trackName = title, albumName = album, duration, instrumental = false, plainLyrics = plain, syncedLyrics = (string?)null };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Callback { get; set; } = callback;
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Count++; return Callback(request, cancellationToken); }
    }
}
