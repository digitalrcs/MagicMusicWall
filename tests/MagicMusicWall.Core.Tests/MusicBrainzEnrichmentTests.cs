using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

// The service intentionally has one process-wide rate limiter. Isolate 429 tests
// from other fake HTTP clients so their synthetic cooldown cannot affect them.
[CollectionDefinition("MusicBrainz enrichment serial", DisableParallelization = true)]
public sealed class MusicBrainzEnrichmentCollection { }

[Collection("MusicBrainz enrichment serial")]
public sealed class MusicBrainzEnrichmentTests
{
    private const string Recording = "11111111-1111-1111-1111-111111111111";
    private const string Release = "22222222-2222-2222-2222-222222222222";
    private const string Group = "33333333-3333-3333-3333-333333333333";
    private const string Other = "44444444-4444-4444-4444-444444444444";
    private const string BasicRecording = """
        {"id":"11111111-1111-1111-1111-111111111111","title":"Song","artist-credit":[{"name":"Artist"}],"first-release-date":"1990-02-03"}
        """;

    [Fact]
    public async Task OptionalAlbumNarrowsRecordingSearchWithEscapedReleaseField()
    {
        using var handler = new Handler((request, _) =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("AND release:\"Blue \\\"Room\\\"\\/Live\\+\"", query);
            Assert.Contains("recording:\"Song\" AND artist:\"Artist\"", query);
            return Task.FromResult(Json("{\"count\":0,\"recordings\":[]}"));
        });
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.SearchRecordingsAsync("Artist", "Song", CancellationToken.None, "Blue \"Room\"/Live+");
        Assert.True(result.IsComplete); Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    public async Task SearchCompletenessRequiresKnownUntruncatedProviderCount(int count, bool expected)
    {
        var root = new Dictionary<string, object> { ["recordings"] = new[] { JsonSerializer.Deserialize<JsonElement>(BasicRecording) } };
        if (count >= 0) root["count"] = count;
        using var handler = new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(root))));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.SearchRecordingsAsync("Artist", "Song");
        Assert.Single(result.Items); Assert.Equal(expected, result.IsComplete);
    }

    [Fact]
    public async Task SearchWithTruncatedReleaseChoicesIsNotComplete()
    {
        var payload = JsonSerializer.Serialize(new
        {
            count = 1,
            recordings = new[] { new Dictionary<string, object>
            {
                ["id"] = Recording, ["title"] = "Song", ["artist-credit"] = new[] { new { name = "Artist" } },
                ["releases"] = Enumerable.Range(1, 13).Select(i => new { id = Guid.NewGuid(), title = "Album " + i }).ToArray()
            } }
        });
        using var handler = new Handler((_, _) => Task.FromResult(Json(payload)));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.SearchRecordingsAsync("Artist", "Song");
        Assert.Equal(12, result.Items.Count); Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task RecordingEvidenceUsesPositiveVotesAndOnlyRecordingInstrumentCredits()
    {
        var payload = """
            {"id":"11111111-1111-1111-1111-111111111111","title":"Song","artist-credit":[{"name":"Artist"}],
             "first-release-date":"1990-02-03",
             "relations":[
               {"target-type":"artist","type":"instrument","artist":{"name":"Violin Player"},"attributes":["violin","guest"]},
               {"target-type":"artist","type":"instrument","artist":{"name":"Guitar Player"},"attributes":["acoustic guitar"]},
               {"target-type":"artist","type":"producer","artist":{"name":"Producer"},"attributes":["piano"]},
               {"target-type":"work","type":"performance","work":{"title":"Work"},"attributes":["cello"]},null,3]}
            """;
        var classification = """
            {"id":"11111111-1111-1111-1111-111111111111",
             "genres":[{"name":"folk","count":2},{"name":"FOLK","count":1},{"name":"wrong","count":-1},{"name":"unknown"}],
             "tags":[{"name":"mellow","count":1},{"name":"disputed","count":0}]}
            """;
        var requests = new List<string>();
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("musicbrainz.org", request.RequestUri!.Host);
            Assert.Contains("MagicMusicWall/0.8", request.Headers.UserAgent.ToString());
            requests.Add(request.RequestUri.Query);
            return Task.FromResult(Json(request.RequestUri.Query switch
            {
                "?fmt=json&inc=artist-credits+artist-rels" => payload,
                "?fmt=json&inc=genres+tags" => classification,
                _ => throw new InvalidOperationException("Unexpected include combination")
            }));
        });
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.GetMusicBrainzInfoAsync(Match() with { Album = "Unverified", ReleaseId = "" });
        var info = Assert.IsType<MusicBrainzInfo>(result.Info);
        Assert.Equal(["folk"], info.Genres); Assert.Equal(["mellow"], info.Tags);
        Assert.Equal(["violin", "acoustic guitar"], info.Instruments);
        Assert.Equal(3, info.Credits.Length);
        Assert.Contains("Producer — producer (piano)", info.Credits);
        Assert.Empty(info.Album); Assert.Empty(info.ReleaseId);
        Assert.Equal("1990-02-03", info.FirstReleaseDate);
        Assert.Equal(["https://musicbrainz.org/recording/" + Recording], info.Sources);
        Assert.Equal(info.Sources[0], info.SourceUrl);
        Assert.InRange(info.RetrievedUtc, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.Equal(["?fmt=json&inc=artist-credits+artist-rels", "?fmt=json&inc=genres+tags"], requests);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task VerifiedReleaseGroupSuppliesAlbumContextInAtMostFourRequests()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
        {
            "/ws/2/recording/" + Recording => BasicRecording,
            "/ws/2/release/" + Release => """
                {"id":"22222222-2222-2222-2222-222222222222","title":"Verified Album","release-group":{"id":"33333333-3333-3333-3333-333333333333"},"media":[{"tracks":[{"recording":{"id":"11111111-1111-1111-1111-111111111111"}}]}]}
                """,
            "/ws/2/release-group/" + Group => """
                {"id":"33333333-3333-3333-3333-333333333333","genres":[{"name":"ambient","count":2}],"tags":[{"name":"relaxing","count":1}]}
                """,
            _ => throw new InvalidOperationException("Unexpected endpoint")
        })));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.GetMusicBrainzInfoAsync(Match());
        var info = Assert.IsType<MusicBrainzInfo>(result.Info);
        Assert.Equal("Verified Album", info.Album); Assert.Equal(Release, info.ReleaseId);
        Assert.Equal(["ambient"], info.Genres); Assert.Equal(["relaxing"], info.Tags);
        Assert.Empty(info.Instruments); Assert.Equal(3, info.Sources.Length); Assert.Equal(4, handler.Count);
        Assert.Contains("album-level", result.Message);
    }

    [Fact]
    public async Task UnrelatedReleaseDoesNotAttachAlbumEvidence()
    {
        var request = 0;
        using var handler = new Handler((_, _) => Task.FromResult(Json(handlerResponse())));
        string handlerResponse() => ++request <= 2 ? BasicRecording : """
            {"id":"22222222-2222-2222-2222-222222222222","title":"Wrong Album","media":[{"tracks":[{"recording":{"id":"44444444-4444-4444-4444-444444444444"}}]}],"genres":[{"name":"wrong","count":4}]}
            """;
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.GetMusicBrainzInfoAsync(Match());
        Assert.Null(result.Info); Assert.Contains("could not be verified", result.Message); Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"44444444-4444-4444-4444-444444444444\",\"title\":\"Wrong\",\"artist-credit\":[{\"name\":\"Artist\"}]}")]
    [InlineData("{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"No artist\"}")]
    public async Task MalformedOrMismatchedIdentityNeverReturnsEvidence(string response)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(response)));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.GetMusicBrainzInfoAsync(Match() with { ReleaseId = "" });
        Assert.Null(result.Info); Assert.NotNull(result.Message); Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"44444444-4444-4444-4444-444444444444\",\"genres\":[{\"name\":\"wrong\",\"count\":1}]}")]
    public async Task MalformedOrMismatchedClassificationNeverReturnsPartialEvidence(string response)
    {
        var request = 0;
        using var handler = new Handler((_, _) => Task.FromResult(Json(++request == 1 ? BasicRecording : response)));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        var result = await service.GetMusicBrainzInfoAsync(Match());
        Assert.Null(result.Info); Assert.NotNull(result.Message); Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task InvalidIdentifiersAndPrecancelledCallsDoNotSendRequests()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP expected"));
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        Assert.Null((await service.GetMusicBrainzInfoAsync(Match() with { Id = "https://example.com" })).Info);
        Assert.Null((await service.GetMusicBrainzInfoAsync(Match() with { ReleaseId = "bad" })).Info);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetMusicBrainzInfoAsync(Match(), cancellation.Token));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ActiveCancellationPropagatesAndSharedGateStillWorks()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Json(BasicRecording); });
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetMusicBrainzInfoAsync(Match() with { ReleaseId = "" }, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        handler.Callback = (_, _) => Task.FromResult(Json(BasicRecording));
        Assert.NotNull((await service.GetMusicBrainzInfoAsync(Match() with { ReleaseId = "" })).Info);
    }

    [Fact]
    public async Task RateLimitIsReportedAndStopsEarlyManualRetry()
    {
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler); using var service = new MusicLookupService(client);
        try
        {
            var result = await service.GetMusicBrainzInfoAsync(Match());
            Assert.Null(result.Info); Assert.Contains("rate limited", result.Message);
            Assert.Contains("asked us to wait", (await service.SearchRecordingsAsync("Artist", "Song")).Message);
            Assert.Equal(1, handler.Count);
        }
        finally { await Task.Delay(400); }
    }

    [Fact]
    public async Task LookupAndSearchShareTheSameRateGateAcrossInstances()
    {
        var starts = new List<long>();
        using var handler = new Handler((request, _) =>
        {
            starts.Add(Stopwatch.GetTimestamp());
            return Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith(Recording) ? BasicRecording : "{\"recordings\":[]}"));
        });
        using var client = new HttpClient(handler); using var first = new MusicLookupService(client); using var second = new MusicLookupService(client);
        await Task.WhenAll(first.GetMusicBrainzInfoAsync(Match() with { ReleaseId = "" }), second.SearchRecordingsAsync("Artist", "Song"));
        Assert.Equal(3, starts.Count);
        Assert.True(Stopwatch.GetElapsedTime(starts[0], starts[1]).TotalMilliseconds >= 990);
        Assert.True(Stopwatch.GetElapsedTime(starts[1], starts[2]).TotalMilliseconds >= 990);
    }

    [Fact]
    public void ConfidentMatchIgnoresProviderScoreAndAcceptsDurationBoundary()
    {
        var candidate = Match() with { Artist = "Ártist", Title = "SONG!", DurationSeconds = 183, Score = 1 };
        Assert.Same(candidate, MusicLookupService.ChooseConfidentMatch(Item(), [candidate]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [candidate with { DurationSeconds = 183.001 }]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [candidate with { Artist = "Other", Score = 100 }]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [candidate with { Album = "Compilation" }]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [candidate with { Album = "" }]));
    }

    [Fact]
    public void ConfidentMatchRejectsAmbiguousIdsButDeduplicatesSameRecording()
    {
        var candidate = Match();
        Assert.NotNull(MusicLookupService.ChooseConfidentMatch(Item(), [candidate, candidate with { ReleaseId = Group }]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [candidate, candidate with { Id = Other }]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), Enumerable.Repeat(candidate, 101)));
    }

    [Theory]
    [InlineData("live at Test Hall")]
    [InlineData("2011 remaster")]
    [InlineData("club remix")]
    [InlineData("demo")]
    public void ConfidentMatchDoesNotAssumeVersionFromDuration(string disambiguation)
        => Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [Match() with { Disambiguation = disambiguation }]));

    [Fact]
    public void MissingAlbumSelectsRecordingOnlyAndMissingIdentityNeedsReview()
    {
        var item = Item(); item.Album = "";
        var result = Assert.IsType<RecordingMatch>(MusicLookupService.ChooseConfidentMatch(item, [Match()]));
        Assert.Equal(Recording, result.Id); Assert.Empty(result.Album); Assert.Empty(result.ReleaseId);
        item.Artist = ""; Assert.Null(MusicLookupService.ChooseConfidentMatch(item, [Match()]));
        item = Item(); item.DurationSeconds = double.NaN; Assert.Null(MusicLookupService.ChooseConfidentMatch(item, [Match()]));
        Assert.Null(MusicLookupService.ChooseConfidentMatch(Item(), [Match() with { Id = "bad" }]));
    }

    private static MediaItem Item() => new() { Artist = "Artist", Title = "Song", Album = "Album", DurationSeconds = 180 };
    private static RecordingMatch Match() => new() { Id = Recording, ReleaseId = Release, Artist = "Artist", Title = "Song", Album = "Album", DurationSeconds = 180 };
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Callback { get; set; } = callback;
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return Callback(request, cancellationToken); }
    }
}
