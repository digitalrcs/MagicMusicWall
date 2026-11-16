using System.Net;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class AiPlaylistTests
{
    private static AiSong Song(string id) => new(id, "Song", "Artist", "Album", 1985, "Rock", [], [], null, null);
    private static readonly AiProviderOptions Local = new("LM Studio", "http://localhost:1234/v1", "test-model", "");
    [Fact]
    public void InventoryExcludesPathsVideoAndUnavailableAndIncludesEvidence()
    {
        var songs = AiPlaylistService.Inventory([
            new() { Id = "a", Title = "Song", Path = "C:/private/song.mp3", MusicBrainz = new() { Instruments = ["guitar"] } },
            new() { Id = "video", IsVideo = true }, new() { Id = "missing", IsAvailable = false }
        ], new Dictionary<string, AudioFeatures> { ["a"] = new([], [], 82, .7, .2, 100) });
        var item = Assert.Single(songs);
        Assert.Equal(82d, item.EstimatedBpm);
        Assert.Contains("guitar", item.Instruments);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(songs));
        Assert.DoesNotContain("Path", JsonSerializer.Serialize(songs));
    }
    [Fact]
    public void UnknownIdsDuplicatesInvalidScoresAndLimitAreHandled()
    {
        var result = AiPlaylistService.ParseChoices("""
            {"songs":[{"id":"unknown","reason":"bad","score":1},
            {"id":"a","reason":"first","score":0.7},{"id":"a","reason":"best","score":0.9},
            {"id":"b","reason":"second","score":0.8},{"id":"c","reason":"invalid","score":2}]}
            """, [Song("a"), Song("b"), Song("c")], 1);
        Assert.Equal("best", Assert.Single(result).Reason);
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"songs\":")]
    public void MalformedResponseFails(string input) => Assert.ThrowsAny<JsonException>(() => AiPlaylistService.ParseChoices(input, [Song("a")], 20));
    [Fact]
    public void EmptyResultsAreValid() => Assert.Empty(AiPlaylistService.ParseChoices("{\"songs\":[]}", [Song("a")], 20));
    [Fact]
    public void CloudRequiresTlsAndEndpointRejectsEmbeddedCredentials()
    {
        Assert.Throws<InvalidOperationException>(() => AiPlaylistService.Endpoint(Local with { Provider = "OpenAI" }, "models"));
        Assert.Throws<InvalidOperationException>(() => AiPlaylistService.Endpoint(Local with { BaseUrl = "http://user:pass@localhost/v1" }, "models"));
        Assert.Equal("http://localhost:1234/v1/models", AiPlaylistService.Endpoint(Local, "models").AbsoluteUri);
    }
    [Fact]
    public async Task EveryBatchIsReviewedAndGlobalLimitApplied()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            var body = await request.Content!.ReadAsStringAsync(token);
            using var json = JsonDocument.Parse(body);
            using var user = JsonDocument.Parse(json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var id = user.RootElement.GetProperty("inventory")[0].GetProperty("id").GetString();
            var answer = JsonSerializer.Serialize(new { songs = new[] { new { id, reason = "Tagged year 1985", score = .8 } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = answer } } } }), Encoding.UTF8, "application/json") };
        }));
        var result = await new AiPlaylistService(http).RecommendAsync(Local, "80s songs", Enumerable.Range(0, 121).Select(x => Song(x.ToString())).ToArray(), 2, null, default);
        Assert.Equal(3, calls); Assert.Equal(2, result.Count);
    }
    [Fact]
    public async Task CancellationDoesNotReturnPartialResults()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) => { cts.Cancel(); await Task.Delay(10, token); return new(HttpStatusCode.OK); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AiPlaylistService(http).RecommendAsync(Local, "Dinner", [Song("a")], 20, null, cts.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact]
    public void NotesPersistSeparatelyAndCannotUseSongIdsAsPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mmw-ai-note-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var note = new AiSongNote("../../outside", "Demo", "Artist", "LM Studio", "model", DateTimeOffset.UtcNow, "Unverified interpretation");
            AiSongNotes.Save(directory, note);
            Assert.Equal(note, AiSongNotes.Load(directory, note.SongId));
            Assert.Null(AiSongNotes.Load(directory, "different"));
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "ai-song-notes")));
            Assert.False(File.Exists(Path.Combine(directory, "library.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
