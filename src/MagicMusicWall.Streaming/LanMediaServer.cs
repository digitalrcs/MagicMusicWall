using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MagicMusicWall.Streaming;

/// <summary>
/// An opt-in HTTP server for explicitly shared media files. The URL is a bearer secret:
/// anyone on the reachable network with the URL can read that file while sharing is active.
/// No discovery, firewall changes, directory browsing, or transcoding is performed.
/// </summary>
public sealed class LanMediaServer : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, SharedFile> _shares = new(StringComparer.Ordinal);
    private CancellationTokenSource _shareLifetime = new();
    private WebApplication? _application;
    private string? _baseUrl;
    private bool _disposed;

    public bool IsRunning => BaseUrl is not null;

    public string? BaseUrl
    {
        get { lock (_stateLock) return _baseUrl; }
    }

    /// <summary>Listen on one IPv4 address assigned to this computer. Port zero selects a free port.</summary>
    public async Task StartAsync(string bindAddress, int port = 0)
    {
        var address = ValidateAddress(bindAddress);
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Choose a port between 1 and 65535, or 0 for an automatic port.");

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_application is not null)
                    throw new InvalidOperationException("Network sharing is already running. Stop it before choosing another address or port.");
            }

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(LanMediaServer).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "Production"
            });
            builder.Configuration.Sources.Clear();
            // Request logging can expose bearer tokens in URLs. The desktop app reports lifecycle errors.
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Listen(address, port);
            });

            var application = builder.Build();
            application.MapMethods("/media/{token}/{fileName}", ["GET", "HEAD"], ServeFileAsync);
            try
            {
                await application.StartAsync().ConfigureAwait(false);
                var addresses = application.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()?.Addresses;
                var actualAddress = addresses?.SingleOrDefault()
                    ?? throw new InvalidOperationException("The network sharing address could not be determined.");
                lock (_stateLock)
                {
                    _application = application;
                    _baseUrl = actualAddress.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "Network sharing could not start on the selected address and port. Check that the network is connected and choose an unused port.", ex);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Share exactly one existing audio or video file. The URL is valid only for this server session.</summary>
    public string ShareFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an existing media file using its full file path.", nameof(path));

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The selected media file path is not valid.", nameof(path), ex);
        }

        if (!MediaContentTypes.TryGet(fullPath, out var contentType))
            throw new ArgumentException("Choose a supported audio or video file. Folders, playlists, and other document types cannot be shared.", nameof(path));

        // Open once before publishing a URL so missing files and unreadable media fail immediately.
        try
        {
            using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The selected media file cannot be read. Check that the drive is connected and that you have permission to open the file.", ex);
        }

        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_baseUrl is null)
                throw new InvalidOperationException("Start network sharing before creating a media link.");
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var fileName = Path.GetFileName(fullPath);
            _shares.Add(token, new SharedFile(fullPath, fileName, contentType));
            return $"{_baseUrl}/media/{token}/{Uri.EscapeDataString(fileName)}";
        }
    }

    /// <summary>Revoke every existing URL and abort active transfers. Players may retain already buffered media.</summary>
    public void ClearShares()
    {
        CancellationTokenSource previous;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = RevokeSharesLocked();
        }
        CancelAndDispose(previous);
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_disposed) return;
            }
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_disposed) return;
                _disposed = true;
            }
            await StopCoreAsync().ConfigureAwait(false);
            lock (_stateLock) _shareLifetime.Dispose();
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        WebApplication? application;
        CancellationTokenSource previous;
        lock (_stateLock)
        {
            application = _application;
            _application = null;
            _baseUrl = null;
            previous = RevokeSharesLocked();
        }
        CancelAndDispose(previous);
        if (application is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await application.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally { await application.DisposeAsync().ConfigureAwait(false); }
    }

    private CancellationTokenSource RevokeSharesLocked()
    {
        var previous = _shareLifetime;
        _shareLifetime = new CancellationTokenSource();
        _shares.Clear();
        return previous;
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        finally { source.Dispose(); }
    }

    private async Task ServeFileAsync(HttpContext context)
    {
        SharedFile? share;
        CancellationTokenRegistration revocation;
        var token = context.Request.RouteValues["token"] as string;
        var fileName = context.Request.RouteValues["fileName"] as string;
        lock (_stateLock)
        {
            if (token is null || !_shares.TryGetValue(token, out share) ||
                !string.Equals(fileName, share.FileName, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            // Register under the same lock as revocation, so a cleared URL cannot miss cancellation.
            revocation = _shareLifetime.Token.Register(context.Abort);
        }

        using (revocation)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(share.FullPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // ASP.NET owns and disposes the stream after completing GET/HEAD, including byte ranges.
            await Results.File(stream, contentType: share.ContentType, enableRangeProcessing: true)
                .ExecuteAsync(context).ConfigureAwait(false);
        }
    }

    private static IPAddress ValidateAddress(string bindAddress)
    {
        if (!IPAddress.TryParse(bindAddress, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new ArgumentException("Choose one IPv4 address belonging to this computer, such as 192.168.1.20. All-interface addresses are not allowed.", nameof(bindAddress));

        var isLocal = address.Equals(IPAddress.Loopback) || NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Any(unicast => unicast.Address.Equals(address));
        if (!isLocal)
            throw new ArgumentException("The selected IPv4 address is not available on this computer. Reconnect to the network or choose a current address.", nameof(bindAddress));
        return address;
    }

    private sealed record SharedFile(string FullPath, string FileName, string ContentType);
}
