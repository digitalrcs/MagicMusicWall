using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MagicMusicWall.Streaming.Tests;

public sealed class GuestAccessTests
{
    [Fact]
    public async Task GuestCanAppendButCannotControlStreamOrUseOwnerSession()
    {
        const string owner = "owner-synthetic-key-with-at-least-32-characters";
        const string guest = "guest-synthetic-key-with-at-least-32-characters";
        await using var server = new RemoteControlServer(); var control = new Control();
        await server.StartAsync("127.0.0.1", 0, owner, control, guestAccessToken: guest);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(server.BaseUrl!) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest);
        Assert.Contains("\"queueOnly\":true", await client.GetStringAsync("/api/access"));
        var state = await client.GetFromJsonAsync<RemoteState>("/api/state");
        Assert.False(state!.BrowserPlaybackEnabled); Assert.False(state.CanChangeAudioOutput);
        using var append = await client.PostAsJsonAsync("/api/command", new RemoteCommand("queue-item", ItemId: "song"));
        Assert.Equal(HttpStatusCode.NoContent, append.StatusCode); Assert.Equal(1, control.Executed);
        foreach (var action in new[] { "next", "pause", "clear-queue", "play-item", "queue-next", "remove-queue-item" })
        {
            using var response = await client.PostAsJsonAsync("/api/command", new RemoteCommand(action, ItemId: "song", QueueIndex: 0));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var stream = await client.PostAsJsonAsync("/api/stream", new { itemId = "song" });
        Assert.Equal(HttpStatusCode.Forbidden, stream.StatusCode); Assert.Equal(1, control.Executed);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        Assert.Contains("\"queueOnly\":false", await client.GetStringAsync("/api/access"));
        using var next = await client.PostAsJsonAsync("/api/command", new RemoteCommand("next"));
        Assert.Equal(HttpStatusCode.NoContent, next.StatusCode); Assert.Equal(2, control.Executed);
    }
    public static IEnumerable<object[]> ForbiddenCommands()
    {
        foreach (var action in new[] { "play", "pause", "toggle", "next", "previous", "clear-queue", "undo-queue", "play-results", "play-folder", "refresh-audio" })
            yield return [new RemoteCommand(action)];
        foreach (var action in new[] { "play-item", "queue-next" }) yield return [new RemoteCommand(action, ItemId: "song")];
        foreach (var action in new[] { "play-queue-item", "remove-queue-item" }) yield return [new RemoteCommand(action, ItemId: "song", QueueIndex: 0)];
        foreach (var action in new[] { "shuffle", "repeat", "repeat-one" }) yield return [new RemoteCommand(action, Enabled: true)];
        yield return [new RemoteCommand("audio-output", AudioOutputId: "")];
        yield return [new RemoteCommand("volume", Volume: 75)];
        yield return [new RemoteCommand("seek", PositionMs: 12000)];
        yield return [new RemoteCommand("play-playlist", PlaylistId: "playlist")];
    }

    [Theory]
    [MemberData(nameof(ForbiddenCommands))]
    public async Task ValidOwnerCommandsAreForbiddenBeforeGuestCanMutatePlayback(RemoteCommand command)
    {
        const string owner = "owner-synthetic-key-with-at-least-32-characters";
        const string guest = "guest-synthetic-key-with-at-least-32-characters";
        await using var server = new RemoteControlServer(); var control = new Control();
        await server.StartAsync("127.0.0.1", 0, owner, control, guestAccessToken: guest);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(server.BaseUrl!) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest);
        using var denied = await client.PostAsJsonAsync("/api/command", command);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, control.Executed);
        var state = await client.GetFromJsonAsync<RemoteState>("/api/state");
        Assert.False(state!.IsPlaying); Assert.Equal(0, state.PositionMs); Assert.Equal(10, state.Volume);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        using var allowed = await client.PostAsJsonAsync("/api/command", command);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode); // Same payload is valid, not merely malformed.
        Assert.Equal(1, control.Executed);
    }

    [Theory]
    [InlineData("GET", "/api/song/song")]
    [InlineData("GET", "/api/song/song/lyrics")]
    [InlineData("POST", "/api/stream")]
    public async Task GuestCannotReadPrivateInformationOrMintBrowserStreams(string method, string path)
    {
        await using var server = new RemoteControlServer(); var control = new Control();
        const string guest = "guest-synthetic-key-with-at-least-32-characters";
        await server.StartAsync("127.0.0.1", 0, "owner-synthetic-key-with-at-least-32-characters", control, guestAccessToken: guest);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), server.BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", guest);
        if (method == "POST") request.Content = JsonContent.Create(new { itemId = "song" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, control.Executed);
    }

    [Fact]
    public async Task ReplacingThenDisablingGuestKeyRevokesOldLinksWithoutRevokingOwner()
    {
        const string owner = "owner-synthetic-key-with-at-least-32-characters";
        const string oldGuest = "guest-old-synthetic-key-at-least-32-characters";
        const string newGuest = "guest-new-synthetic-key-at-least-32-characters";
        await using var server = new RemoteControlServer(); var control = new Control();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        async Task<HttpStatusCode> Append(string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "/api/command") { Content = JsonContent.Create(new RemoteCommand("queue-item", ItemId: "song")) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await client.SendAsync(request); return response.StatusCode;
        }
        await server.StartAsync("127.0.0.1", 0, owner, control, guestAccessToken: oldGuest);
        Assert.Equal(HttpStatusCode.NoContent, await Append(oldGuest));
        await server.StopAsync();
        await server.StartAsync("127.0.0.1", 0, owner, control, guestAccessToken: newGuest);
        Assert.Equal(HttpStatusCode.Unauthorized, await Append(oldGuest));
        Assert.Equal(1, control.Executed);
        Assert.Equal(HttpStatusCode.NoContent, await Append(newGuest));
        Assert.Equal(HttpStatusCode.NoContent, await Append(owner));
        await server.StopAsync();
        await server.StartAsync("127.0.0.1", 0, owner, control);
        Assert.Equal(HttpStatusCode.Unauthorized, await Append(newGuest));
        Assert.Equal(HttpStatusCode.Unauthorized, await Append(oldGuest));
        Assert.Equal(3, control.Executed);
        Assert.Equal(HttpStatusCode.NoContent, await Append(owner));
        Assert.Equal(4, control.Executed);
    }

    [Theory]
    [InlineData("queue-item")]
    [InlineData("queue-selection")]
    [InlineData("queue-folder")]
    public async Task GuestMayUseOnlyExplicitAppendCommands(string action)
    {
        await using var server = new RemoteControlServer(); var control = new Control();
        const string guest = "guest-synthetic-key-with-at-least-32-characters";
        await server.StartAsync("127.0.0.1", 0, "owner-synthetic-key-with-at-least-32-characters", control, guestAccessToken: guest);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(server.BaseUrl!) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guest);
        using var response = await client.PostAsJsonAsync("/api/command", new RemoteCommand(action, ItemId:"song", ItemIds:["song"]));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.Equal(1, control.Executed);
    }

    [Fact]
    public async Task CrossOriginGuestAppendIsRejectedBeforeExecution()
    {
        await using var server = new RemoteControlServer(); var control = new Control();
        const string guest = "guest-synthetic-key-with-at-least-32-characters";
        await server.StartAsync("127.0.0.1", 0, "owner-synthetic-key-with-at-least-32-characters", control, guestAccessToken: guest);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "/api/command") { Content = JsonContent.Create(new RemoteCommand("queue-item",ItemId:"song")) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",guest);
        request.Headers.Add("Origin", "http://unrelated.example");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(0,control.Executed);
    }

    private sealed class Control : IRemoteControl, IRemoteFolders
    {
        public int Executed;
        public Task<RemoteFolderPage?> GetFoldersAsync(string? folderId, CancellationToken cancellationToken) => Task.FromResult<RemoteFolderPage?>(new("Collection", null, null, [], [], 0, 0));
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { Executed++; return Task.CompletedTask; }
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => Task.FromResult(new RemoteLibrary([], 0));
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(new RemoteState(null, "", "", false, 0, 0, 10, "PC", [], [], BrowserPlaybackEnabled: true));
    }
}
