using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteFoldersTests : IAsyncLifetime
{
    private const string Token = "synthetic-remote-folders-access-key-at-least32";
    private static readonly string FolderId = new('A', 64);
    private readonly RemoteControlServer _server = new();
    private readonly FolderControl _control = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    public Task InitializeAsync() => _server.StartAsync("127.0.0.1", 0, Token, _control);
    public async Task DisposeAsync() { _client.Dispose(); await _server.DisposeAsync(); }

    private async Task<HttpResponseMessage> Send(string path = "/api/folders", RemoteCommand? command = null, string? token = Token, string? origin = null, string? address = null)
    {
        using var request = new HttpRequestMessage(command is null ? HttpMethod.Get : HttpMethod.Post, (address ?? _server.BaseUrl) + path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (command is not null) request.Content = JsonContent.Create(command);
        return await _client.SendAsync(request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingAndPlayingFoldersRequireAuthentication(bool play)
    {
        foreach (var key in new string?[] { null, "wrong-key" })
        {
            using var response = await Send(play ? "/api/command" : "/api/folders", play ? new("play-folder", FolderId: FolderId) : null, key);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Empty(_control.FolderReads); Assert.Empty(_control.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossOriginFolderRequestsCannotReachDesktop(bool play)
    {
        using var response = await Send(play ? "/api/command" : "/api/folders", play ? new("play-folder", FolderId: FolderId) : null, origin: "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_control.FolderReads); Assert.Empty(_control.Commands);
    }

    [Fact]
    public async Task HomeContractHasFoldersAndCollectionCountsWithoutDirectRows()
    {
        using var response = await Send();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<RemoteFolderPage>();
        Assert.Equal("Your folders", page!.Title); Assert.Null(page.Id); Assert.Null(page.ParentId);
        Assert.Empty(page.Items); Assert.Equal(0, page.TotalItems); Assert.Equal(512, page.DescendantCount);
        Assert.Equal(FolderId, Assert.Single(page.Folders).Id);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(new string?[] { null }, _control.FolderReads);
    }

    [Fact]
    public async Task SelectedFolderCapsVisibleRowsWithoutChangingCounts()
    {
        using var response = await Send("/api/folders?folder=" + FolderId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<RemoteFolderPage>();
        Assert.Equal(FolderId, page!.Id);
        Assert.Equal(500, page.Items.Count); Assert.Equal(510, page.TotalItems); Assert.Equal(512, page.DescendantCount);
        Assert.Contains("\"descendantCount\":512", await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { FolderId }, _control.FolderReads);
    }

    [Fact]
    public async Task InvalidDuplicateAndUnrecognizedFolderArgumentsAreRejected()
    {
        foreach (var query in new[] { "folder=", "folder=C%3A%5CMusic", "folder=" + new string('A', 63), "folder=" + new string('A', 65),
            "folder=" + new string('G', 64), "folder=" + FolderId + "&folder=" + FolderId, "path=C%3A%5CMusic", "url=https%3A%2F%2Funrelated.example" })
        {
            using var response = await Send("/api/folders?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(_control.FolderReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownFolderCannotBeReadOrPlayed(bool play)
    {
        var unknown = new string('B', 64);
        using var response = await Send(play ? "/api/command" : "/api/folders?folder=" + unknown, play ? new("play-folder", FolderId: unknown) : null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(new[] { unknown }, _control.FolderReads);
        Assert.Empty(_control.Commands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task PlayFolderPassesTheKnownFolderOrCollectionHomeToDesktop(string? id)
    {
        using var response = await Send("/api/command", new("play-folder", FolderId: id));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.Single(_control.Commands);
        Assert.Equal("play-folder", command.Action); Assert.Equal(id, command.FolderId);
        Assert.Equal(new[] { id }, _control.FolderReads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Music")]
    [InlineData("not-an-id")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public async Task InvalidFolderCommandValuesAreNeverDispatched(string id)
    {
        foreach (var action in new[] { "play-folder", "next" })
        {
            using var response = await Send("/api/command", new(action, FolderId: id));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(_control.FolderReads); Assert.Empty(_control.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedDesktopReturnsNotFound(bool play)
    {
        await using var older = new RemoteControlServer();
        await older.StartAsync("127.0.0.1", 0, Token, new PlaybackControl());
        using var response = await Send(play ? "/api/command" : "/api/folders", play ? new("play-folder") : null, address: older.BaseUrl);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovalBetweenFolderReadAndPlaybackReturnsNotFound()
    {
        _control.RemovedBeforePlay = true;
        using var response = await Send("/api/command", new("play-folder", FolderId: FolderId));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("private-files", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StopCancelsAnInFlightFolderRead()
    {
        _control.BlockRead = true;
        var pending = Send();
        await _control.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var response = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private class PlaybackControl : IRemoteControl
    {
        public List<RemoteCommand> Commands { get; } = [];
        public bool RemovedBeforePlay { get; set; }
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            if (RemovedBeforePlay) throw new KeyNotFoundException("C:\\private-files\\removed");
            Commands.Add(command); return Task.CompletedTask;
        }
    }

    private sealed class FolderControl : PlaybackControl, IRemoteFolders
    {
        public List<string?> FolderReads { get; } = [];
        public bool BlockRead { get; set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RemoteFolderPage?> GetFoldersAsync(string? folderId, CancellationToken cancellationToken)
        {
            FolderReads.Add(folderId);
            ReadStarted.TrySetResult();
            if (BlockRead) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (folderId is null) return new("Your folders", null, null, [new(FolderId, "Sample music", 512)], [], 0, 512);
            if (!string.Equals(folderId, FolderId, StringComparison.OrdinalIgnoreCase)) return null;
            return new("Sample music", FolderId, null, [new(new string('C', 64), "Album", 2)],
                Enumerable.Range(0, 510).Select(i => new RemoteItem("track-" + i, "Track " + i, "Artist", "Album", "3:00", false)).ToArray(), 510, 512);
        }
    }
}
