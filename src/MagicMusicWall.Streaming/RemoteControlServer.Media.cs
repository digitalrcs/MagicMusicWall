using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MagicMusicWall.Streaming;

public sealed partial class RemoteControlServer
{
    private static void MapBrowserStreaming(WebApplication application, IRemoteControl control, CancellationToken lifetime)
    {
        // A new store is captured by each listener, so restarting cannot revive an old URL.
        var tickets = new BrowserMediaTickets(lifetime);
        application.MapPost("/api/stream", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType())
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return;
            }
            if (context.Request.ContentLength > MaximumBodyBytes) { PayloadTooLarge(context); return; }
            using var request = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime);
            string? itemId;
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
                if (length > MaximumBodyBytes) { PayloadTooLarge(context); return; }
                using var document = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 4 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                    !root.TryGetProperty("itemId", out var value) || value.ValueKind != JsonValueKind.String)
                {
                    await BadRequest(context, "Choose one library item to play in this browser.").ConfigureAwait(false);
                    return;
                }
                itemId = value.GetString();
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                PayloadTooLarge(context); return;
            }
            catch (JsonException)
            {
                await BadRequest(context, "Choose one library item to play in this browser.").ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 128 || itemId.Any(char.IsControl))
            {
                await BadRequest(context, "Choose a valid library item to play in this browser.").ConfigureAwait(false);
                return;
            }
            var media = control is IRemoteMedia source ? await source.GetMediaFileAsync(itemId, request.Token).ConfigureAwait(false) : null;
            var file = media is null ? null : BrowserMediaFile.Openable(media.AbsolutePath);
            if (file is null) { await MediaNotFound(context).ConfigureAwait(false); return; }
            var ticket = tickets.Add(file);
            await context.Response.WriteAsJsonAsync(ticket, JsonOptions, request.Token).ConfigureAwait(false);
        });
        application.MapMethods("/stream/{token}", ["GET", "HEAD"], tickets.Serve);
    }

    private static Task MediaNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return context.Response.WriteAsJsonAsync(new { error = "Browser playback is unavailable for this item. Check the PC app's settings and that the media drive is connected." });
    }

    private sealed record BrowserMediaFile(string FullPath, string ContentType)
    {
        public static BrowserMediaFile? Openable(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
                var fullPath = Path.GetFullPath(path);
                if (!MediaContentTypes.TryGet(fullPath, out var contentType)) return null;
                // Fail before minting a capability if the indexed file cannot be read.
                using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new(fullPath, contentType);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }

    private sealed class BrowserMediaTickets(CancellationToken lifetime)
    {
        private const int MaximumTickets = 128;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public RemoteMediaTicket Add(BrowserMediaFile file)
        {
            lock (_gate)
            {
                lifetime.ThrowIfCancellationRequested();
                var now = DateTimeOffset.UtcNow;
                foreach (var expired in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray()) _entries.Remove(expired);
                if (_entries.Count >= MaximumTickets) _entries.Remove(_entries.MinBy(pair => pair.Value.ExpiresAt).Key);
                string token;
                do { token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); } while (_entries.ContainsKey(token));
                var expires = now.AddHours(8);
                _entries.Add(token, new(file, expires));
                return new("/stream/" + token, expires, file.ContentType);
            }
        }

        public async Task Serve(HttpContext context)
        {
            var token = context.Request.RouteValues["token"] as string;
            Entry? entry;
            CancellationTokenRegistration revocation;
            lock (_gate)
            {
                if (lifetime.IsCancellationRequested || token is not { Length: 64 } || !_entries.TryGetValue(token, out entry))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound; return;
                }
                if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    _entries.Remove(token);
                    context.Response.StatusCode = StatusCodes.Status404NotFound; return;
                }
                // Registration also invokes Abort immediately if shutdown raced this lookup.
                revocation = lifetime.Register(context.Abort);
            }
            using (revocation)
            {
                FileStream stream;
                try
                {
                    stream = new FileStream(entry.File.FullPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound; return;
                }
                // ASP.NET disposes the stream for complete, cancelled, HEAD and range responses.
                // No download filename is supplied, keeping Content-Disposition absent.
                await Results.File(stream, contentType: entry.File.ContentType, enableRangeProcessing: true)
                    .ExecuteAsync(context).ConfigureAwait(false);
            }
        }

        private sealed record Entry(BrowserMediaFile File, DateTimeOffset ExpiresAt);
    }
}
