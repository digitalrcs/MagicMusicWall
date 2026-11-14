namespace MagicMusicWall.Streaming;

/// <summary>Resolves only indexed library entries when browser playback is enabled.</summary>
public interface IRemoteMedia
{
    Task<RemoteMediaFile?> GetMediaFileAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Internal callback result. The server never serializes this path to the browser.</summary>
public sealed record RemoteMediaFile(string AbsolutePath);
public sealed record RemoteMediaTicket(string Url, DateTimeOffset ExpiresAt, string ContentType);
