using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteQueuePagingTests : IAsyncLifetime
{
    private const string Token = "synthetic-queue-paging-key-32-characters";
    private readonly RemoteControlServer _server = new();
    private readonly QueueControl _control = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false });
    public async Task InitializeAsync()
    {
        await _server.StartAsync("127.0.0.1", 0, Token, _control);
        _client.BaseAddress = new Uri(_server.BaseUrl!);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }
    public async Task DisposeAsync() { _client.Dispose(); await _server.DisposeAsync(); }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(500, 100)]
    [InlineData(5900, 100)]
    [InlineData(5999, 1)]
    [InlineData(6000, 0)]
    public async Task PagesPreserveAbsoluteOccurrencesAndTotal(int offset, int count)
    {
        var page = await _client.GetFromJsonAsync<RemoteQueuePage>($"/api/queue?offset={offset}&limit=100");
        Assert.Equal(6000, page!.Total);
        Assert.Equal(offset, page.Offset);
        Assert.Equal(count, page.Items.Count);
        if (count > 0) Assert.Equal($"Occurrence {offset}", page.Items[0].Title);
        Assert.All(page.Items, item => Assert.Equal("duplicate-id", item.Id));
    }

    [Theory]
    [InlineData("offset=-1")]
    [InlineData("offset=2147483648")]
    [InlineData("offset=1&offset=2")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    [InlineData("limit=1&limit=2")]
    [InlineData("unknown=1")]
    public async Task InvalidPagingDoesNotReadQueue(string query)
    {
        using var response = await _client.GetAsync("/api/queue?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _control.Reads);
    }

    [Fact]
    public async Task LastOccurrenceCanBeControlledAndCurrentIsAlwaysExposed()
    {
        var state = await _client.GetFromJsonAsync<RemoteState>("/api/state");
        Assert.Equal(6000, state!.QueueTotal);
        Assert.Equal(5999, state.CurrentQueueIndex);
        Assert.Equal("Occurrence 5999", state.CurrentQueueItem!.Title);
        using var response = await _client.PostAsJsonAsync("/api/command", new RemoteCommand("play-queue-item", ItemId:"duplicate-id", QueueIndex:5999));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(5999, _control.LastCommand!.QueueIndex);
    }

    private sealed class QueueControl : IRemoteControl, IRemoteQueue
    {
        public int Reads;
        public RemoteCommand? LastCommand;
        private static RemoteItem Item(int index) => new("duplicate-id", $"Occurrence {index}", "Artist", "Album", "1:00", false);
        public Task<RemoteQueuePage> GetQueueAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new RemoteQueuePage(Enumerable.Range(0,6000).Skip(offset).Take(limit).Select(Item).ToArray(), offset,6000));
        }
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(new RemoteState("duplicate-id", "Occurrence 5999", "Artist", false,0,60000,50,"Default",[Item(0)],[],CurrentQueueIndex:5999,QueueTotal:6000,CurrentQueueItem:Item(5999)));
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => Task.FromResult(new RemoteLibrary([],0));
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { LastCommand=command; return Task.CompletedTask; }
    }
}
