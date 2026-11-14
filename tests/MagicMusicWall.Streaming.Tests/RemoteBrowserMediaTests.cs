using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteBrowserMediaTests : IAsyncLifetime
{
    private const string AccessKey = "synthetic-browser-playback-access-key-at-least32";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("MagicMusicWallBrowserMediaTests-");
    private readonly RemoteControlServer _server = new();
    private readonly MediaControl _control = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly byte[] _bytes = Encoding.ASCII.GetBytes("0123456789abcdefghijklmnopqrstuvwxyz");
    private string MediaPath => Path.Combine(_directory.FullName, "private library name.mp3");

    public async Task InitializeAsync()
    {
        await File.WriteAllBytesAsync(MediaPath, _bytes);
        _control.MediaPath = MediaPath;
        await _server.StartAsync("127.0.0.1", 0, AccessKey, _control);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    private async Task<HttpResponseMessage> Mint(string json = "{\"itemId\":\"track-1\"}", string? key = AccessKey, string? origin = null, string? address = null, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, (address ?? _server.BaseUrl) + "/api/stream");
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (origin is not null) request.Headers.Add("Origin", origin);
        request.Content = new StringContent(json, Encoding.UTF8, contentType);
        return await _client.SendAsync(request);
    }

    private async Task<RemoteMediaTicket> Ticket()
    {
        using var response = await Mint();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RemoteMediaTicket>())!;
    }

    [Fact]
    public async Task OnlyAuthenticatedSameOriginRequestsCanCreateTickets()
    {
        foreach (var key in new string?[] { null, "wrong-key" })
        {
            using var response = await Mint(key: key);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var crossOrigin = await Mint(origin: "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        Assert.Empty(_control.RequestedIds);
    }

    [Fact]
    public async Task TicketScopesOneFileWithoutExposingTheAccessKeyOrPath()
    {
        var before = DateTimeOffset.UtcNow;
        var ticket = await Ticket();
        var after = DateTimeOffset.UtcNow;
        Assert.Matches("^/stream/[a-f0-9]{64}$", ticket.Url);
        Assert.InRange(ticket.ExpiresAt, before.AddHours(8), after.AddHours(8));
        Assert.Equal("audio/mpeg", ticket.ContentType);
        Assert.DoesNotContain(AccessKey, ticket.Url);
        Assert.DoesNotContain("private", ticket.Url);

        // The scoped capability is sufficient for a browser media element; no Bearer header.
        using var response = await _client.GetAsync(_server.BaseUrl + ticket.Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        foreach (var path in new[] { "/stream/" + new string('a', 64), "/stream/" + AccessKey, ticket.Url + "/private.txt" })
        {
            using var unknown = await _client.GetAsync(_server.BaseUrl + path);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        using var post = await _client.PostAsync(_server.BaseUrl + ticket.Url, null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Empty(_control.Commands);
    }

    [Theory]
    [InlineData("GET", "bytes=5-9", "56789", "bytes 5-9/36", 5)]
    [InlineData("GET", "bytes=-3", "xyz", "bytes 33-35/36", 3)]
    [InlineData("HEAD", "bytes=18-22", "", "bytes 18-22/36", 5)]
    public async Task BrowserSeekingAndHeadRequestsReturnCorrectByteRanges(string method, string range, string body, string contentRange, long length)
    {
        var ticket = await Ticket();
        using var request = new HttpRequestMessage(new HttpMethod(method), _server.BaseUrl + ticket.Url);
        request.Headers.Range = RangeHeaderValue.Parse(range);
        request.Headers.Add("Origin", _server.BaseUrl);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(contentRange, response.Content.Headers.ContentRange?.ToString());
        Assert.Equal(length, response.Content.Headers.ContentLength);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CapabilityRequestsStillEnforceHostAndOrigin()
    {
        var ticket = await Ticket();
        using var originRequest = new HttpRequestMessage(HttpMethod.Get, _server.BaseUrl + ticket.Url);
        originRequest.Headers.Add("Origin", "https://unrelated.example");
        using var crossOrigin = await _client.SendAsync(originRequest);
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        using var hostRequest = new HttpRequestMessage(HttpMethod.Get, _server.BaseUrl + ticket.Url);
        hostRequest.Headers.Host = "unrelated.example:" + new Uri(_server.BaseUrl!).Port;
        using var wrongHost = await _client.SendAsync(hostRequest);
        Assert.Equal(HttpStatusCode.BadRequest, wrongHost.StatusCode);
    }

    [Fact]
    public async Task MalformedMissingDuplicateAndExcessiveItemIdsNeverReachTheCatalog()
    {
        foreach (var json in new[] { "{", "null", "{}", "[]", "{\"itemId\":null}", "{\"itemId\":123}", "{\"itemId\":\" \"}",
            "{\"itemId\":\"track-1\",\"itemId\":\"track-2\"}", "{\"itemId\":\"track-1\",\"path\":\"anything\"}",
            "{\"itemId\":\"" + new string('x', 129) + "\"}", "{\"itemId\":\"line\\nfeed\"}" })
        {
            using var response = await Mint(json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var wrongType = await Mint("itemId=track-1", contentType: "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        using var oversized = await Mint("{\"itemId\":\"" + new string('x', 17000) + "\"}");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Empty(_control.RequestedIds);
    }

    [Fact]
    public async Task UnknownDisabledUnsupportedAndMissingMediaDoNotIssueCapabilities()
    {
        using var unknown = await Mint("{\"itemId\":\"unknown\"}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        _control.Enabled = false;
        using var disabled = await Mint();
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        _control.Enabled = true;
        File.Delete(MediaPath);
        using var missing = await Mint();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.DoesNotContain("private", await missing.Content.ReadAsStringAsync());
        await using var older = new RemoteControlServer();
        await older.StartAsync("127.0.0.1", 0, AccessKey, new PlaybackControl());
        using var unsupported = await Mint(address: older.BaseUrl);
        Assert.Equal(HttpStatusCode.NotFound, unsupported.StatusCode);
    }

    [Fact]
    public async Task RestartRevokesOldTicketsAndCapacityEvictsTheOldest()
    {
        var oldest = await Ticket();
        for (var index = 0; index < 128; index++) await Ticket();
        using var evicted = await _client.GetAsync(_server.BaseUrl + oldest.Url);
        Assert.Equal(HttpStatusCode.NotFound, evicted.StatusCode);
        var last = await Ticket();
        using var valid = await _client.GetAsync(_server.BaseUrl + last.Url);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        await _server.StopAsync();
        await _server.StartAsync("127.0.0.1", 0, AccessKey, _control);
        using var revoked = await _client.GetAsync(_server.BaseUrl + last.Url);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.NotEqual(last.Url, (await Ticket()).Url);
    }

    [Fact]
    public async Task StopAbortsAnActiveTransfer()
    {
        var large = Path.Combine(_directory.FullName, "large.mp4");
        await using (var file = File.Create(large)) file.SetLength(64 * 1024 * 1024);
        _control.MediaPath = large;
        var ticket = await Ticket();
        Assert.Equal("video/mp4", ticket.ContentType);
        using var response = await _client.GetAsync(_server.BaseUrl + ticket.Url, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        Assert.True(await stream.ReadAsync(new byte[1024]) > 0);
        await _server.StopAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = await Record.ExceptionAsync(async () => await stream.CopyToAsync(Stream.Null, timeout.Token));
        Assert.NotNull(failure);
        Assert.False(timeout.IsCancellationRequested, "Shutdown should abort the transfer without waiting for a client timeout.");
    }

    private class PlaybackControl : IRemoteControl
    {
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { Commands.Add(command); return Task.CompletedTask; }
    }

    private sealed class MediaControl : PlaybackControl, IRemoteMedia
    {
        public string MediaPath { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public List<string> RequestedIds { get; } = [];
        public Task<RemoteMediaFile?> GetMediaFileAsync(string id, CancellationToken cancellationToken)
        {
            RequestedIds.Add(id);
            return Task.FromResult(Enabled && id == "track-1" ? new RemoteMediaFile(MediaPath) : null);
        }
    }
}
