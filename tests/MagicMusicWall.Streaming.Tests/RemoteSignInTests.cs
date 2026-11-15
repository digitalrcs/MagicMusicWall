using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;

namespace MagicMusicWall.Streaming.Tests;

public sealed class RemoteSignInTests : IAsyncLifetime
{
    private const string Token = "synthetic-sign-in-test-key-at-least-32-characters";
    private const string Passphrase = "Synthetic music room 2026";
    private static readonly string Verifier = RemotePassphrase.CreateHash(Passphrase);
    private readonly RemoteControlServer _server = new();
    private readonly FakeControl _control = new();
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;

    public RemoteSignInTests() => _client = new(new HttpClientHandler { UseProxy = false, CookieContainer = _cookies })
    { Timeout = TimeSpan.FromSeconds(15) };
    public Task InitializeAsync() => _server.StartAsync("127.0.0.1", 0, Token, _control, Verifier);
    public async Task DisposeAsync() { _client.Dispose(); await _server.DisposeAsync(); }
    private string Url(string path) => _server.BaseUrl + path;
    private Task<HttpResponseMessage> Login(string phrase = Passphrase, bool remember = true) =>
        _client.PostAsJsonAsync(Url("/auth/login"), new { passphrase = phrase, remember });
    private async Task<string> SignIn(bool remember = true)
    {
        using var response = await Login(remember: remember);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return Assert.Single(response.Headers.GetValues("Set-Cookie"));
    }

    [Fact]
    public async Task PlainUrlOffersSignInWithoutExposingLibraryOrCredentials()
    {
        using var page = await _client.GetAsync(Url("/"));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, html);
        Assert.DoesNotContain(Passphrase, html);
        Assert.DoesNotContain(Verifier, html);
        Assert.DoesNotContain(FakeControl.PrivateTitle, html);
        using var status = await _client.GetAsync(Url("/auth/status"));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("passphraseEnabled").GetBoolean());
        Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
        using var state = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.Unauthorized, state.StatusCode);
        Assert.Equal(0, _control.ReadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PassphraseGrantsCookieOnlyPlaybackAndExpectedPersistence(bool remember)
    {
        var cookie = await SignIn(remember);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, cookie);
        Assert.DoesNotContain(Passphrase, cookie);
        Assert.DoesNotContain(Verifier, cookie);
        var expiry = cookie.Split(';').Select(x => x.Trim()).SingleOrDefault(x => x.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        if (remember)
        {
            Assert.NotNull(expiry);
            var date = DateTimeOffset.Parse(expiry[8..], CultureInfo.InvariantCulture);
            Assert.InRange(date - DateTimeOffset.UtcNow, TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(30));
        }
        else Assert.Null(expiry);
        using var state = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.Contains(FakeControl.PrivateTitle, await state.Content.ReadAsStringAsync());
        using var command = await _client.PostAsJsonAsync(Url("/api/command"), new { action = "next" });
        Assert.Equal(HttpStatusCode.NoContent, command.StatusCode);
        Assert.Equal("next", Assert.Single(_control.Commands).Action);
        using var status = await _client.GetAsync(Url("/auth/status"));
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task WrongPassphraseNeverGrantsAccessAndSixthAttemptIsRateLimited()
    {
        for (var i = 0; i < 5; i++)
        {
            using var response = await Login("Wrong music room passphrase");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.DoesNotContain(FakeControl.PrivateTitle, await response.Content.ReadAsStringAsync());
        }
        using var limited = await Login();
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var state = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.Unauthorized, state.StatusCode);
        Assert.Equal(0, _control.ReadCount);
    }

    [Fact]
    public async Task TamperedCookieCannotReadLibrary()
    {
        var header = await SignIn();
        var cookie = header.Split(';')[0];
        var last = cookie[^1];
        cookie = cookie[..^1] + (last == '0' ? '1' : '0');
        using var other = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/library"));
        request.Headers.Add("Cookie", cookie);
        using var response = await other.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _control.ReadCount);
    }

    [Fact]
    public async Task SigningOutClearsThisBrowsersAccess()
    {
        await SignIn();
        using var response = await _client.PostAsJsonAsync(Url("/auth/logout"), new { });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        using var state = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.Unauthorized, state.StatusCode);
        Assert.Equal(0, _control.ReadCount);
    }

    [Fact]
    public async Task RememberedAccessSurvivesRestartButChangingEitherCredentialRevokesIt()
    {
        await SignIn();
        var port = new Uri(_server.BaseUrl!).Port;
        await _server.StopAsync();
        await _server.StartAsync("127.0.0.1", port, Token, _control, Verifier);
        using (var unchanged = await _client.GetAsync(Url("/api/state"))) Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        await _server.StopAsync();
        var changed = RemotePassphrase.CreateHash("A different synthetic room");
        await _server.StartAsync("127.0.0.1", port, Token, _control, changed);
        using (var revoked = await _client.GetAsync(Url("/api/state"))) Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using (var login = await Login("A different synthetic room")) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await _server.StopAsync();
        await _server.StartAsync("127.0.0.1", port, Token + "rotated", _control, changed);
        using var rotated = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.Unauthorized, rotated.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    public async Task DisabledOrMalformedPassphraseSettingsFailClosed(string hash)
    {
        var port = new Uri(_server.BaseUrl!).Port;
        await _server.StopAsync();
        await _server.StartAsync("127.0.0.1", port, Token, _control, hash);
        using var status = await _client.GetAsync(Url("/auth/status"));
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("passphraseEnabled").GetBoolean());
        using var response = await Login();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/state"));
        request.Headers.Authorization = new("Bearer", Token);
        using var legacy = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    [Theory]
    [InlineData("/auth/login", "Origin", "https://unrelated.example")]
    [InlineData("/auth/logout", "Origin", "https://unrelated.example")]
    [InlineData("/api/command", "Origin", "https://unrelated.example")]
    [InlineData("/auth/login", "Sec-Fetch-Site", "cross-site")]
    public async Task CrossSiteRequestsCannotSignInSignOutOrControl(string path, string header, string value)
    {
        await SignIn();
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path));
        request.Content = JsonContent.Create(new { passphrase = Passphrase, remember = true, action = "next" });
        request.Headers.Add(header, value);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(_control.Commands);
        using var state = await _client.GetAsync(Url("/api/state"));
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
    }

    [Fact]
    public async Task LoginRejectsFormsMalformedAndOversizedBodies()
    {
        using var form = await _client.PostAsync(Url("/auth/login"), new FormUrlEncodedContent(new Dictionary<string, string> { ["passphrase"] = Passphrase }));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);
        using var malformed = await _client.PostAsync(Url("/auth/login"), new StringContent("{bad json}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var oversized = await Login(new string('x', 17000));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.False(form.Headers.Contains("Set-Cookie"));
        Assert.False(malformed.Headers.Contains("Set-Cookie"));
        Assert.False(oversized.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, _control.ReadCount);
    }

    [Fact]
    public async Task StaleQueueRemovalReturnsConflictWithoutLeakingDesktopDetails()
    {
        await SignIn();
        _control.RejectRemoval = true;
        using var response = await _client.PostAsJsonAsync(Url("/api/command"), new { action = "remove-queue-item", itemId = "song-1", queueIndex = 2 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("queue changed", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-folder", body);
        Assert.Empty(_control.Commands);
    }

    [Fact]
    public void PassphraseHashIsSaltedAndRejectsInvalidValues()
    {
        var second = RemotePassphrase.CreateHash(Passphrase);
        Assert.NotEqual(Verifier, second);
        Assert.True(RemotePassphrase.Verify(Passphrase, second));
        Assert.False(RemotePassphrase.Verify("Wrong synthetic passphrase", second));
        Assert.False(RemotePassphrase.Verify(Passphrase, "v1." + new string('Z', 32) + "." + new string('0', 64)));
        Assert.Throws<ArgumentException>(() => RemotePassphrase.CreateHash("short"));
        Assert.Throws<ArgumentException>(() => RemotePassphrase.CreateHash(new string('x', 129)));
        Assert.Throws<ArgumentException>(() => RemotePassphrase.CreateHash(new string(' ', 10)));
    }

    private sealed class FakeControl : IRemoteControl
    {
        public const string PrivateTitle = "Private synthetic music title";
        private static readonly RemoteItem Item = new("song-1", PrivateTitle, "Artist", "Album", "3:00", false);
        public int ReadCount { get; private set; }
        public bool RejectRemoval { get; set; }
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(new RemoteState(Item.Id, Item.Title, Item.Artist, false, 0, 180000, 60, "Test output", [Item], []));
        }
        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken)
        { ReadCount++; return Task.FromResult(new RemoteLibrary([Item], 1)); }
        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            if (RejectRemoval && command.Action == "remove-queue-item") throw new ArgumentException("Changed C:/private-folder/song.mp3");
            Commands.Add(command); return Task.CompletedTask;
        }
    }
}
