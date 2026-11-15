using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MagicMusicWall.Streaming;

public interface IRemoteControl
{
    Task<RemoteState> GetStateAsync(CancellationToken cancellationToken);
    Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken);
    Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken);
}

public sealed record RemoteItem(string Id, string Title, string Artist, string Album, string Duration, bool IsVideo);
public sealed record RemotePlaylist(string Id, string Name, int Count);
public sealed record RemoteState(string? CurrentId, string Title, string Artist, bool IsPlaying,
    long PositionMs, long DurationMs, int Volume, string Output,
    IReadOnlyList<RemoteItem> Queue, IReadOnlyList<RemotePlaylist> Playlists, int CurrentQueueIndex = -1, bool BrowserPlaybackEnabled = false);
public sealed record RemoteLibrary(IReadOnlyList<RemoteItem> Items, int Total);
public sealed record RemoteCommand(string Action, string? ItemId = null, string? PlaylistId = null,
    string? Query = null, int? Volume = null, long? PositionMs = null, int? QueueIndex = null, string? FolderId = null);

/// <summary>
/// Optional LAN remote for the desktop player. The page is public, but every API request
/// requires an access key or a signed session from passphrase sign-in. Browser playback uses short-lived, file-scoped URLs;
/// media paths and general-purpose commands are never exposed.
/// </summary>
public sealed partial class RemoteControlServer : IAsyncDisposable
{
    private const int MaximumBodyBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateLock = new();
    private WebApplication? _application;
    private CancellationTokenSource? _requestsLifetime;
    private string? _baseUrl;
    private bool _disposed;

    public bool IsRunning => BaseUrl is not null;
    public string? BaseUrl { get { lock (_stateLock) return _baseUrl; } }

    /// <summary>Starts on one local IPv4 address. Port zero chooses a free port for diagnostics/tests.</summary>
    public async Task StartAsync(string address, int port, string accessToken, IRemoteControl control, string passphraseHash = "")
    {
        var bindAddress = ValidateAddress(address);
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrEmpty(accessToken) || accessToken.Length is < 32 or > 512 || accessToken.Any(c => c <= 32 || c > 126))
            throw new ArgumentException("Create a remote access key before starting remote control.", nameof(accessToken));
        ArgumentNullException.ThrowIfNull(control);

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_application is not null) throw new InvalidOperationException("Remote control is already running. Stop it before changing its address or port.");
            }
            var page = ReadPage();
            var logo = ReadResource("MagicMusicWall.Streaming.digitalrcs-logo.png");
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
            var signIn = new RemoteSignIn(accessToken, passphraseHash);
            bool IsAuthorized(HttpRequest request) => Authorized(request, tokenHash) || signIn.Authorized(request);
            var lifetime = new CancellationTokenSource();
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [], ApplicationName = typeof(RemoteControlServer).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory, EnvironmentName = "Production"
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = MaximumBodyBytes;
                options.Listen(bindAddress, port);
            });
            var application = builder.Build();
            application.Use(async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
                // Restrict the Host as well as the listening interface; an unrelated DNS name
                // should not become a trusted origin for the local remote page.
                if (!string.Equals(context.Request.Host.Host, bindAddress.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    (context.Request.Host.Port ?? 80) != context.Connection.LocalPort)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                var protectedRoute = context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth");
                if ((protectedRoute || context.Request.Path.StartsWithSegments("/stream")) &&
                    context.Request.Headers.TryGetValue("Origin", out var origin) &&
                    (origin.Count != 1 || !string.Equals(origin[0], $"http://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (protectedRoute && context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    if (!IsAuthorized(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await context.Response.WriteAsJsonAsync(new { error = "Sign in with your remote passphrase, or open the QR link from Setup." });
                        return;
                    }
                }
                try { await next(context).ConfigureAwait(false); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested || lifetime.IsCancellationRequested)
                {
                    if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
                catch (Exception) when (!context.Response.HasStarted)
                {
                    // Desktop callbacks may mention a missing media path. Do not publish those details.
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { error = "The desktop player could not complete this request. Check the app on the PC." });
                }
            });
            application.MapGet("/", () => Results.Text(page, "text/html; charset=utf-8"));
            application.MapGet("/brand-logo.png", () => Results.Bytes(logo, "image/png"));
            signIn.MapRoutes(application, IsAuthorized);
            application.MapGet("/api/folders", async (HttpContext context) =>
            {
                var folder = context.Request.Query["folder"];
                if (context.Request.Query.Keys.Any(key => key != "folder") || folder.Count > 1 ||
                    (folder.Count == 1 && !ValidFolderId(folder[0])))
                {
                    await BadRequest(context, "Choose a folder from the collection.").ConfigureAwait(false);
                    return;
                }
                using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
                var page = control is IRemoteFolders folders
                    ? await folders.GetFoldersAsync(folder.Count == 0 ? null : folder[0], request.Token).ConfigureAwait(false) : null;
                if (page is null) { await FolderNotFound(context).ConfigureAwait(false); return; }
                await context.Response.WriteAsJsonAsync(page with { Items = page.Items.Take(500).ToArray() }, JsonOptions, request.Token).ConfigureAwait(false);
            });
            MapSongInformationRoutes(application, control, lifetime.Token);
            MapBrowserStreaming(application, control, lifetime.Token);
            application.MapGet("/api/state", async (HttpContext context) =>
            {
                using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
                var state = await control.GetStateAsync(request.Token).ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(state, JsonOptions, request.Token).ConfigureAwait(false);
            });
            application.MapGet("/api/library", async (HttpContext context) =>
            {
                var query = context.Request.Query["q"];
                var playlist = context.Request.Query["playlist"];
                if (query.Count > 1 || playlist.Count > 1 || query.ToString().Length > 256 || playlist.ToString().Length > 128)
                {
                    await BadRequest(context, "Search text or playlist is too long.");
                    return;
                }
                using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
                var result = await control.GetLibraryAsync(query.ToString(), string.IsNullOrEmpty(playlist) ? null : playlist.ToString(), request.Token).ConfigureAwait(false);
                var bounded = new RemoteLibrary(result.Items.Take(500).ToArray(), result.Total);
                await context.Response.WriteAsJsonAsync(bounded, JsonOptions, request.Token).ConfigureAwait(false);
            });
            application.MapPost("/api/command", async (HttpContext context) =>
            {
                if (!context.Request.HasJsonContentType())
                {
                    context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    return;
                }
                if (context.Request.ContentLength > MaximumBodyBytes)
                {
                    PayloadTooLarge(context);
                    return;
                }
                using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
                RemoteCommand? command;
                try
                {
                    var buffer = new byte[MaximumBodyBytes + 1];
                    var length = 0;
                    while (length < buffer.Length)
                    {
                        var read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), request.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        length += read;
                    }
                    if (length > MaximumBodyBytes)
                    {
                        PayloadTooLarge(context);
                        return;
                    }
                    command = JsonSerializer.Deserialize<RemoteCommand>(buffer.AsSpan(0, length), JsonOptions);
                }
                catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    PayloadTooLarge(context);
                    return;
                }
                catch (JsonException)
                {
                    await BadRequest(context, "Send a valid playback command.");
                    return;
                }
                if (!ValidCommand(command))
                {
                    await BadRequest(context, "The playback command or its values are not valid.");
                    return;
                }
                if (command!.Action == "play-folder")
                {
                    if (control is not IRemoteFolders folders || await folders.GetFoldersAsync(command.FolderId, request.Token).ConfigureAwait(false) is null)
                    {
                        await FolderNotFound(context).ConfigureAwait(false);
                        return;
                    }
                    try { await control.ExecuteAsync(command, request.Token).ConfigureAwait(false); }
                    catch (KeyNotFoundException) { await FolderNotFound(context).ConfigureAwait(false); return; }
                }
                else
                {
                    try { await control.ExecuteAsync(command, request.Token).ConfigureAwait(false); }
                    catch (ArgumentException) when (command.Action == "remove-queue-item")
                    {
                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        await context.Response.WriteAsJsonAsync(new { error = "The queue changed. Refresh it and try removing the song again." });
                        return;
                    }
                }
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
            try
            {
                await application.StartAsync().ConfigureAwait(false);
                var actual = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
                    ?? throw new InvalidOperationException("The remote address could not be determined.");
                lock (_stateLock)
                {
                    _application = application;
                    _requestsLifetime = lifetime;
                    _baseUrl = actual.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                lifetime.Cancel();
                await application.DisposeAsync().ConfigureAwait(false);
                lifetime.Dispose();
                throw new InvalidOperationException("Remote control could not start. Check the selected network address and choose an unused port.", ex);
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
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
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        WebApplication? application;
        CancellationTokenSource? lifetime;
        lock (_stateLock)
        {
            application = _application; _application = null;
            lifetime = _requestsLifetime; _requestsLifetime = null;
            _baseUrl = null;
        }
        if (application is null) return;
        try
        {
            lifetime?.Cancel();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await application.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
            lifetime?.Dispose();
        }
    }

    private static void MapSongInformationRoutes(WebApplication application, IRemoteControl control, CancellationToken lifetime)
    {
        application.MapGet("/api/song/{id}", (HttpContext context) => SongInformation(context, control, lifetime, SongOperation.Details));
        application.MapGet("/api/song/{id}/lookup", (HttpContext context) => SongInformation(context, control, lifetime, SongOperation.Recordings));
        application.MapGet("/api/song/{id}/lyrics", (HttpContext context) => SongInformation(context, control, lifetime, SongOperation.Lyrics));
        application.MapGet("/api/song/{id}/facts", (HttpContext context) => SongInformation(context, control, lifetime, SongOperation.Facts));
        application.MapGet("/api/song/{id}/facts/{pageId}", (HttpContext context) => SongInformation(context, control, lifetime, SongOperation.Article));
    }

    private static async Task SongInformation(HttpContext context, IRemoteControl control, CancellationToken lifetime, SongOperation operation)
    {
        if (control is not IRemoteSongInformation information)
        {
            await SongNotFound(context).ConfigureAwait(false);
            return;
        }
        var id = context.Request.RouteValues["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
        {
            await BadRequest(context, "Choose a song from the library first.").ConfigureAwait(false);
            return;
        }
        var hasSearchFields = operation is SongOperation.Recordings or SongOperation.Lyrics or SongOperation.Facts;
        if (context.Request.Query.Keys.Any(key => !hasSearchFields || (key != "artist" && key != "title")))
        {
            await BadRequest(context, "Only artist and title search fields are supported.").ConfigureAwait(false);
            return;
        }
        string? artist = null, title = null;
        if (hasSearchFields && (!TrySongSearchField(context.Request, "artist", out artist) || !TrySongSearchField(context.Request, "title", out title)))
        {
            await BadRequest(context, "Use one artist and title value, each 256 characters or fewer and without control characters.").ConfigureAwait(false);
            return;
        }
        string? pageId = null;
        if (operation == SongOperation.Article)
        {
            var rawPageId = context.Request.RouteValues["pageId"]?.ToString();
            if (!long.TryParse(rawPageId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPageId) || parsedPageId <= 0)
            {
                await BadRequest(context, "Choose an article from the Wikipedia search results.").ConfigureAwait(false);
                return;
            }
            pageId = parsedPageId.ToString(CultureInfo.InvariantCulture);
        }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime);
        try
        {
            // Validate against the local catalog before making any provider request.
            var details = await information.GetSongAsync(id, request.Token).ConfigureAwait(false);
            if (details is null)
            {
                await SongNotFound(context).ConfigureAwait(false);
                return;
            }
            if (hasSearchFields)
            {
                artist ??= details.Song.Artist.Trim();
                title ??= details.Song.Title.Trim();
                if (!ValidSongSearchField(artist) || !ValidSongSearchField(title) ||
                    (operation == SongOperation.Recordings && title.Length == 0) ||
                    (operation == SongOperation.Lyrics && (artist.Length == 0 || title.Length == 0)) ||
                    (operation == SongOperation.Facts && artist.Length == 0 && title.Length == 0))
                {
                    await BadRequest(context, operation == SongOperation.Lyrics ? "Enter an artist and title before looking up lyrics."
                        : operation == SongOperation.Recordings ? "Enter a title before looking up recordings."
                        : "Enter an artist or title before searching song facts.").ConfigureAwait(false);
                    return;
                }
            }
            object result = operation switch
            {
                SongOperation.Details => details,
                SongOperation.Recordings => await information.SearchRecordingsAsync(id, artist!, title!, request.Token).ConfigureAwait(false),
                SongOperation.Lyrics => await information.GetLyricsAsync(id, artist!, title!, request.Token).ConfigureAwait(false),
                SongOperation.Facts => await information.SearchFactsAsync(id, artist!, title!, request.Token).ConfigureAwait(false),
                SongOperation.Article => await information.GetFactAsync(id, pageId!, request.Token).ConfigureAwait(false),
                _ => throw new InvalidOperationException()
            };
            await context.Response.WriteAsJsonAsync(result, JsonOptions, request.Token).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // A library entry may have been removed after the initial local read.
            await SongNotFound(context).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            await BadRequest(context, "Check the artist, title, or selected article and try again.").ConfigureAwait(false);
        }
        catch (Exception ex) when (operation is SongOperation.Facts or SongOperation.Article && ex is InvalidOperationException or HttpRequestException)
        {
            // Provider failures are actionable, but arbitrary callback exception text may
            // contain local file paths or other details that do not belong in the browser.
            await BadRequest(context, "Wikipedia could not complete this lookup. Check your connection, wait a moment, or choose another article.").ConfigureAwait(false);
        }
    }

    private static bool TrySongSearchField(HttpRequest request, string name, out string? value)
    {
        value = null;
        if (!request.Query.TryGetValue(name, out var fields)) return true;
        if (fields.Count != 1 || !ValidSongSearchField(fields[0] ?? "")) return false;
        value = fields[0]?.Trim() ?? "";
        return true;
    }

    private static bool ValidSongSearchField(string value) => value.Length <= 256 && !value.Any(char.IsControl);

    private static Task SongNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsJsonAsync(new { error = "This song is no longer available. Refresh the library and choose a song." });
    }

    private enum SongOperation { Details, Recordings, Lyrics, Facts, Article }

    private static bool Authorized(HttpRequest request, byte[] expectedHash)
    {
        var values = request.Headers.Authorization;
        if (values.Count != 1) return false;
        var value = values[0];
        if (value is null || value.Length > 1024 || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(value[7..]));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static bool ValidCommand(RemoteCommand? command)
    {
        if (command is null || command.ItemId?.Length > 128 || command.PlaylistId?.Length > 128 || command.Query?.Length > 256 ||
            command.Volume is < 0 or > 100 || command.PositionMs < 0 || command.QueueIndex is < 0 or > 499 ||
            (command.FolderId is not null && !ValidFolderId(command.FolderId))) return false;
        return command.Action switch
        {
            "play" or "pause" or "toggle" or "next" or "previous" or "play-results" => true,
            "play-item" or "queue-item" => !string.IsNullOrWhiteSpace(command.ItemId),
            "play-queue-item" or "remove-queue-item" => !string.IsNullOrWhiteSpace(command.ItemId) && command.QueueIndex.HasValue,
            "play-playlist" => !string.IsNullOrWhiteSpace(command.PlaylistId),
            "play-folder" => true,
            "volume" => command.Volume.HasValue,
            "seek" => command.PositionMs.HasValue,
            _ => false
        };
    }

    private static bool ValidFolderId(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static Task FolderNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsJsonAsync(new { error = "This folder is no longer available. Return to the collection and choose a folder." });
    }

    private static Task BadRequest(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static void PayloadTooLarge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        // The rejected body is intentionally unread. Do not let a client reuse an HTTP/1
        // connection whose remaining request bytes Kestrel will refuse to drain.
        if (context.Request.Protocol.StartsWith("HTTP/1", StringComparison.Ordinal))
            context.Response.Headers.Connection = "close";
    }

    private static string ReadPage()
    {
        return Encoding.UTF8.GetString(ReadResource("MagicMusicWall.Streaming.RemotePage.html"));
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(RemoteControlServer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("A remote control page resource is missing from this app build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IPAddress ValidateAddress(string addressText)
    {
        if (!IPAddress.TryParse(addressText, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new ArgumentException("Choose a single IPv4 address belonging to this computer.", nameof(addressText));
        var isLocal = address.Equals(IPAddress.Loopback) || NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(n => n.Address.Equals(address));
        if (!isLocal) throw new ArgumentException("The selected network address is unavailable on this PC.", nameof(addressText));
        return address;
    }
}
