using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MagicMusicWall.Streaming.Tests;

public sealed class LanMediaServerTests : IAsyncLifetime
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("MagicMusicWallStreamingTests-");
    private readonly LanMediaServer _server = new();
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly byte[] _mediaBytes = Encoding.ASCII.GetBytes("0123456789abcdefghijklmnopqrstuvwxyz");
    private string MediaPath => Path.Combine(_directory.FullName, "Björk & friends #1.mp3");

    public async Task InitializeAsync()
    {
        await File.WriteAllBytesAsync(MediaPath, _mediaBytes);
        await _server.StartAsync("127.0.0.1");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task GetAndHeadServeOnlyTheSelectedFileWithCorrectHeaders()
    {
        var url = _server.ShareFile(MediaPath);
        Assert.True(_server.IsRunning);
        Assert.StartsWith("http://127.0.0.1:", _server.BaseUrl);
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_mediaBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(_mediaBytes.Length, response.Content.Headers.ContentLength);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("Server", response.Headers.Select(header => header.Key));

        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var head = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(_mediaBytes.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("bytes=0-4", "01234", "bytes 0-4/36")]
    [InlineData("bytes=10-", "abcdefghijklmnopqrstuvwxyz", "bytes 10-35/36")]
    [InlineData("bytes=-3", "xyz", "bytes 33-35/36")]
    [InlineData("bytes=34-100", "yz", "bytes 34-35/36")]
    public async Task ByteRangesSupportSeeking(string range, string expected, string expectedContentRange)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _server.ShareFile(MediaPath));
        request.Headers.Range = RangeHeaderValue.Parse(range);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedContentRange, response.Content.Headers.ContentRange?.ToString());
        Assert.Equal(expected.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task HeadRangeReportsBytesWithoutReturningABody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, _server.ShareFile(MediaPath));
        request.Headers.Range = new RangeHeaderValue(5, 9);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 5-9/36", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal(5, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task OutOfBoundsRangeReturnsRequestedRangeNotSatisfiable()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _server.ShareFile(MediaPath));
        request.Headers.Range = new RangeHeaderValue(100, 200);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal("bytes */36", response.Content.Headers.ContentRange?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task InvalidTokensWrongNamesAndTraversalCannotReadOtherFiles()
    {
        var sibling = Path.Combine(_directory.FullName, "not-shared.mp3");
        await File.WriteAllTextAsync(sibling, "unshared file content");
        var url = new Uri(_server.ShareFile(MediaPath));
        var token = url.Segments[2].TrimEnd('/');
        string[] paths =
        [
            "/", "/media", "/media/not-a-token/not-shared.mp3",
            $"/media/{token}/not-shared.mp3",
            $"/media/{token}/%2E%2E%2Fnot-shared.mp3",
            $"/media/{token}/%252E%252E%252Fnot-shared.mp3",
            $"/media/{token}/../not-shared.mp3",
            "/" + Uri.EscapeDataString(sibling)
        ];
        foreach (var path in paths)
        {
            using var response = await _client.GetAsync(_server.BaseUrl + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("unshared file content", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task PostCannotReadSharedMedia()
    {
        using var response = await _client.PostAsync(_server.ShareFile(MediaPath), null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ClearSharesRevokesEveryOldLinkAndAllowsNewLinks()
    {
        var first = _server.ShareFile(MediaPath);
        var second = _server.ShareFile(MediaPath);
        Assert.NotEqual(first, second);
        _server.ClearShares();
        foreach (var url in new[] { first, second })
        {
            using var response = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var fresh = await _client.GetAsync(_server.ShareFile(MediaPath));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task ClearSharesAbortsAnActiveTransfer()
    {
        var largeFile = Path.Combine(_directory.FullName, "large.mp4");
        await using (var file = File.Create(largeFile)) file.SetLength(64 * 1024 * 1024);
        using var response = await _client.GetAsync(_server.ShareFile(largeFile), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        Assert.True(await stream.ReadAsync(new byte[1024]) > 0);

        _server.ClearShares();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var failure = await Record.ExceptionAsync(async () => await stream.CopyToAsync(Stream.Null, timeout.Token));
        Assert.NotNull(failure);
        Assert.False(timeout.IsCancellationRequested, "Revocation should abort the transfer without waiting for a client timeout.");
    }

    [Fact]
    public async Task StopClosesListenerAndRestartDoesNotRestoreTokens()
    {
        var oldUrl = new Uri(_server.ShareFile(MediaPath));
        await _server.StopAsync();
        Assert.False(_server.IsRunning);
        Assert.Null(_server.BaseUrl);
        Assert.Throws<InvalidOperationException>(() => _server.ShareFile(MediaPath));
        await Assert.ThrowsAsync<HttpRequestException>(async () => await _client.GetAsync(oldUrl));
        await _server.StartAsync("127.0.0.1");
        using var response = await _client.GetAsync(_server.BaseUrl + oldUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingMediaAfterSharingReturnsNotFound()
    {
        var url = _server.ShareFile(MediaPath);
        File.Delete(MediaPath);
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void SharingRejectsDirectoriesRelativePathsAndNonMediaFiles()
    {
        Assert.Throws<ArgumentException>(() => _server.ShareFile("track.mp3"));
        Assert.Throws<ArgumentException>(() => _server.ShareFile(_directory.FullName));
        var document = Path.Combine(_directory.FullName, "private.txt");
        File.WriteAllText(document, "document");
        Assert.Throws<ArgumentException>(() => _server.ShareFile(document));
        Assert.Throws<IOException>(() => _server.ShareFile(Path.Combine(_directory.FullName, "missing.mp3")));
        var mediaNamedDirectory = Directory.CreateDirectory(Path.Combine(_directory.FullName, "folder.mp3"));
        Assert.Throws<IOException>(() => _server.ShareFile(mediaNamedDirectory.FullName));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.1")]
    public async Task StartRejectsWildcardIpv6HostnamesAndNonLocalAddresses(string address)
    {
        await using var server = new LanMediaServer();
        await Assert.ThrowsAsync<ArgumentException>(() => server.StartAsync(address));
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StartAndStopLifecycleErrorsArePredictable()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _server.StartAsync("127.0.0.1"));
        await using var other = new LanMediaServer();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => other.StartAsync("127.0.0.1", new Uri(_server.BaseUrl!).Port));
        Assert.Contains("unused port", ex.Message);
        Assert.False(other.IsRunning);
        await other.StopAsync();
        await other.StopAsync();
        await other.DisposeAsync();
        await other.StopAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => other.StartAsync("127.0.0.1"));
    }

    [Theory]
    [InlineData("sample.MP4", "video/mp4")]
    [InlineData("sample.flac", "audio/flac")]
    [InlineData("sample.mkv", "video/x-matroska")]
    public async Task MediaTypeMatchesSharedFile(string fileName, string expected)
    {
        var path = Path.Combine(_directory.FullName, fileName);
        await File.WriteAllBytesAsync(path, _mediaBytes);
        using var response = await _client.GetAsync(_server.ShareFile(path));
        Assert.Equal(expected, response.Content.Headers.ContentType?.MediaType);
    }
}
