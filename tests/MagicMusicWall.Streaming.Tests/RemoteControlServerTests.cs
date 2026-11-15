using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteControlServerTests : IAsyncLifetime
{
    private const string Token = "synthetic-test-access-key-at-least-32-characters";
    private readonly RemoteControlServer _server = new();
    private readonly FakeControl _control = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };

    public Task InitializeAsync() => _server.StartAsync("127.0.0.1", 0, Token, _control);
    public async Task DisposeAsync() { _client.Dispose(); await _server.DisposeAsync(); }

    private HttpRequestMessage Request(HttpMethod method, string path, bool authenticated = true, string? json = null)
    {
        var request = new HttpRequestMessage(method, _server.BaseUrl + path);
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task PublicPageIsSelfContainedAndDoesNotExposeLibraryOrAccessKey()
    {
        using var response = await _client.GetAsync(_server.BaseUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, body);
        Assert.DoesNotContain("Unique private track", body);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
        using var logo = await _client.GetAsync(_server.BaseUrl + "/brand-logo.png");
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, (await logo.Content.ReadAsByteArrayAsync())[..8]);
    }

    [Theory]
    [InlineData("GET", "/api/state")]
    [InlineData("GET", "/api/library")]
    [InlineData("POST", "/api/command")]
    public async Task MissingOrWrongKeyCannotReadOrControl(string method, string path)
    {
        using var request = Request(new HttpMethod(method), path, false, method == "POST" ? "{\"action\":\"next\"}" : null);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Unique private track", await response.Content.ReadAsStringAsync());
        using var wrong = Request(new HttpMethod(method), path, false, method == "POST" ? "{\"action\":\"next\"}" : null);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
        using var wrongResponse = await _client.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        Assert.Equal(0, _control.ReadCount);
        Assert.Empty(_control.Commands);
    }

    [Fact]
    public async Task AuthorizedStateUsesExpectedCamelCaseContract()
    {
        using var request = Request(HttpMethod.Get, "/api/state");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"currentId\":\"track-1\"", body);
        Assert.Contains("\"isPlaying\":true", body);
        Assert.Contains("Unique private track", body);
        Assert.DoesNotContain("path", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task LibraryPreservesSearchAndPlaylistValuesAndCapsVisibleResults()
    {
        _control.ResultCount = 510;
        using var request = Request(HttpMethod.Get, "/api/library?q=Dark%20Side%20%26%20Moon&playlist=playlist-1");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var library = await response.Content.ReadFromJsonAsync<RemoteLibrary>();
        Assert.Equal("Dark Side & Moon", _control.Query);
        Assert.Equal("playlist-1", _control.Playlist);
        Assert.Equal(500, library!.Items.Count);
        Assert.Equal(510, library.Total);
    }

    [Fact]
    public async Task ExcessiveOrDuplicateSearchArgumentsAreRejected()
    {
        foreach (var query in new[] { "q=" + new string('a', 257), "playlist=" + new string('p', 129), "q=one&q=two", "playlist=a&playlist=b" })
        {
            using var request = Request(HttpMethod.Get, "/api/library?" + query);
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, _control.ReadCount);
    }

    [Theory]
    [InlineData("{\"action\":\"play\"}")]
    [InlineData("{\"action\":\"pause\"}")]
    [InlineData("{\"action\":\"toggle\"}")]
    [InlineData("{\"action\":\"next\"}")]
    [InlineData("{\"action\":\"previous\"}")]
    [InlineData("{\"action\":\"play-item\",\"itemId\":\"track-1\"}")]
    [InlineData("{\"action\":\"queue-item\",\"itemId\":\"track-1\"}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":499}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":499}")]
    [InlineData("{\"action\":\"play-playlist\",\"playlistId\":\"playlist-1\"}")]
    [InlineData("{\"action\":\"play-results\",\"query\":\"Pink Floyd\"}")]
    [InlineData("{\"action\":\"volume\",\"volume\":0}")]
    [InlineData("{\"action\":\"volume\",\"volume\":100}")]
    [InlineData("{\"action\":\"seek\",\"positionMs\":0}")]
    public async Task SupportedCommandsAreDispatchedExactlyOnce(string json)
    {
        using var request = Request(HttpMethod.Post, "/api/command", json: json);
        request.Headers.Add("Origin", _server.BaseUrl);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(_control.Commands);
    }

    [Theory]
    [InlineData("{bad json}")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"action\":\"delete-file\"}")]
    [InlineData("{\"action\":\"play-item\"}")]
    [InlineData("{\"action\":\"queue-item\",\"itemId\":\"  \"}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"track-1\"}")]
    [InlineData("{\"action\":\"play-queue-item\",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"  \",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":-1}")]
    [InlineData("{\"action\":\"play-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":500}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"track-1\"}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"  \",\"queueIndex\":0}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":-1}")]
    [InlineData("{\"action\":\"remove-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":500}")]
    [InlineData("{\"action\":\"next\",\"queueIndex\":500}")]
    [InlineData("{\"action\":\"play-playlist\"}")]
    [InlineData("{\"action\":\"volume\"}")]
    [InlineData("{\"action\":\"volume\",\"volume\":101}")]
    [InlineData("{\"action\":\"volume\",\"volume\":-1}")]
    [InlineData("{\"action\":\"seek\",\"positionMs\":-1}")]
    public async Task InvalidCommandsNeverReachDesktop(string json)
    {
        using var request = Request(HttpMethod.Post, "/api/command", json: json);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_control.Commands);
    }

    [Fact]
    public async Task QueuePlaybackPassesBothIdentityAndPositionToDesktop()
    {
        using var request = Request(HttpMethod.Post, "/api/command", json: "{\"action\":\"play-queue-item\",\"itemId\":\"track-1\",\"queueIndex\":3}");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.Single(_control.Commands);
        Assert.Equal("play-queue-item", command.Action);
        Assert.Equal("track-1", command.ItemId);
        Assert.Equal(3, command.QueueIndex);
    }

    [Fact]
    public async Task FormsOversizedBodiesAndCrossOriginCommandsAreRejected()
    {
        using var form = Request(HttpMethod.Post, "/api/command");
        form.Content = new StringContent("action=next", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var formResponse = await _client.SendAsync(form);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, formResponse.StatusCode);
        using var oversized = Request(HttpMethod.Post, "/api/command", json: "{\"action\":\"play-results\",\"query\":\"" + new string('x', 17000) + "\"}");
        using var oversizedResponse = await _client.SendAsync(oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        using var crossOrigin = Request(HttpMethod.Post, "/api/command", json: "{\"action\":\"next\"}");
        crossOrigin.Headers.Add("Origin", "https://unrelated.example");
        using var originResponse = await _client.SendAsync(crossOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, originResponse.StatusCode);
        Assert.Empty(_control.Commands);
    }

    [Fact]
    public async Task UnrelatedHostCannotServeThePage()
    {
        using var request = Request(HttpMethod.Get, "/");
        request.Headers.Host = "unrelated.example:" + new Uri(_server.BaseUrl!).Port;
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CallbackFailureDoesNotRevealFilePathsOrExceptionDetails()
    {
        _control.Fail = true;
        using var request = Request(HttpMethod.Post, "/api/command", json: "{\"action\":\"next\"}");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private-music", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StopRestartAndDisposeRetireListenerAndOldCredentials()
    {
        var oldAddress = _server.BaseUrl!;
        await _server.StopAsync();
        Assert.False(_server.IsRunning);
        Assert.Null(_server.BaseUrl);
        await Assert.ThrowsAsync<HttpRequestException>(async () => await _client.GetAsync(oldAddress));
        await _server.StartAsync("127.0.0.1", 0, Token + "changed", _control);
        using var oldKey = Request(HttpMethod.Get, "/api/state");
        using var response = await _client.SendAsync(oldKey);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _server.DisposeAsync();
        await _server.DisposeAsync();
        await _server.StopAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _server.StartAsync("127.0.0.1", 0, Token, _control));
    }

    [Fact]
    public async Task StopCancelsAnInFlightDesktopRead()
    {
        _control.BlockRead = true;
        using var request = Request(HttpMethod.Get, "/api/state");
        var pending = _client.SendAsync(request);
        await _control.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var response = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(_server.IsRunning);
    }

    [Fact]
    public async Task StartupRejectsInvalidSettingsAndOccupiedPorts()
    {
        await using var other = new RemoteControlServer();
        foreach (var address in new[] { "0.0.0.0", "::1", "localhost", "255.255.255.255", "192.0.2.10" })
            await Assert.ThrowsAsync<ArgumentException>(() => other.StartAsync(address, 0, Token, _control));
        await Assert.ThrowsAsync<ArgumentException>(() => other.StartAsync("127.0.0.1", 0, "short", _control));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => other.StartAsync("127.0.0.1", 65536, Token, _control));
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.StartAsync("127.0.0.1", new Uri(_server.BaseUrl!).Port, Token, _control));
        Assert.False(other.IsRunning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _server.StartAsync("127.0.0.1", 0, Token, _control));
    }

    private sealed class FakeControl : IRemoteControl
    {
        private static readonly RemoteItem Item = new("track-1", "Unique private track", "Artist", "Album", "3:02", false);
        public int ReadCount { get; private set; }
        public int ResultCount { get; set; } = 1;
        public string? Query { get; private set; }
        public string? Playlist { get; private set; }
        public bool Fail { get; set; }
        public bool BlockRead { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<RemoteCommand> Commands { get; } = [];
        public async Task<RemoteState> GetStateAsync(CancellationToken cancellationToken)
        {
            ++ReadCount;
            ReadStarted.TrySetResult();
            if (BlockRead) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new RemoteState(Item.Id, Item.Title, Item.Artist, true, 1000, 182000, 65,
                "Stereo", [Item], [new("playlist-1", "Dinner", 1)]);
        }
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken)
        {
            ++ReadCount; Query = query; Playlist = playlistId;
            return Task.FromResult(new RemoteLibrary(Enumerable.Range(0, ResultCount).Select(_ => Item).ToArray(), ResultCount));
        }
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("Could not open C:\\private-music\\track.mp3");
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
